// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.6 — Colour spaces
// PHASE: Phase 2 — item 13, non-device colour spaces (shared rendering model)
//
// Resolves a PDF colour-space object (a name, or an array such as
// [/Separation name alt tintFn]) into a small, Graphics-free model that reports
// its component count and converts sc / scn operands to sRGB in [0, 1]. Device
// spaces map directly; CalGray / CalRGB / Lab use their defining parameters;
// ICCBased falls back to its alternate (or component count); Indexed resolves a
// palette entry through its base; and Separation / DeviceN run the tint
// transform through the alternate space. Returning a plain double[] keeps the
// model usable by every renderer sink (raster and SVG alike).

using System;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Content;

/// <summary>
/// A resolved PDF colour space that converts its colour components to sRGB.
/// </summary>
public sealed class ResolvedColorSpace
{
    /// <summary>The family of a <see cref="ResolvedColorSpace"/>.</summary>
    public enum Family
    {
        /// <summary>DeviceGray (1 component).</summary>
        DeviceGray,

        /// <summary>DeviceRGB (3 components).</summary>
        DeviceRgb,

        /// <summary>DeviceCMYK (4 components).</summary>
        DeviceCmyk,

        /// <summary>CalGray (1 component).</summary>
        CalGray,

        /// <summary>CalRGB (3 components).</summary>
        CalRgb,

        /// <summary>CIE 1976 L*a*b* (3 components).</summary>
        Lab,

        /// <summary>ICC-based; converted through its alternate space.</summary>
        IccBased,

        /// <summary>Indexed palette (1 component: the index).</summary>
        Indexed,

        /// <summary>Separation (1 tint component).</summary>
        Separation,

        /// <summary>DeviceN (N tint components).</summary>
        DeviceN,

        /// <summary>Pattern; carries no directly paintable colour.</summary>
        Pattern,
    }

    private readonly Family _kind;
    private readonly int _componentCount;

    // Indexed.
    private readonly ResolvedColorSpace? _base;
    private readonly int _hival;
    private readonly byte[]? _lookup;

    // Separation / DeviceN.
    private readonly ResolvedColorSpace? _alternate;
    private readonly PdfFunction? _tint;

    // Lab / CalRGB / CalGray.
    private readonly double[] _whitePoint;
    private readonly double[]? _gamma;
    private readonly double[]? _matrix;
    private readonly double[]? _labRange;

    private ResolvedColorSpace(
        Family kind,
        int componentCount,
        ResolvedColorSpace? baseSpace = null,
        int hival = 0,
        byte[]? lookup = null,
        ResolvedColorSpace? alternate = null,
        PdfFunction? tint = null,
        double[]? whitePoint = null,
        double[]? gamma = null,
        double[]? matrix = null,
        double[]? labRange = null)
    {
        _kind = kind;
        _componentCount = componentCount;
        _base = baseSpace;
        _hival = hival;
        _lookup = lookup;
        _alternate = alternate;
        _tint = tint;
        _whitePoint = whitePoint ?? new double[] { 0.9505, 1.0, 1.0890 };
        _gamma = gamma;
        _matrix = matrix;
        _labRange = labRange;
    }

    /// <summary>Gets the colour-space family.</summary>
    public Family Kind => _kind;

    /// <summary>Gets the number of colour components the space expects.</summary>
    public int ComponentCount => _componentCount;

    /// <summary>Gets a value indicating whether this is a Pattern space.</summary>
    public bool IsPattern => _kind == Family.Pattern;

    /// <summary>
    /// Converts colour components to sRGB. Components shorter than
    /// <see cref="ComponentCount"/> are treated as zero; longer inputs ignore the
    /// surplus. The result is three channels in [0, 1].
    /// </summary>
    /// <param name="components">The colour components (sc / scn operands).</param>
    /// <returns>An sRGB triple in [0, 1].</returns>
    public double[] ToRgb(double[] components)
    {
        ArgumentNullException.ThrowIfNull(components);

        switch (_kind)
        {
            case Family.DeviceGray:
                {
                    double g = At(components, 0);
                    return new double[] { g, g, g };
                }

            case Family.DeviceRgb:
                return new double[] { At(components, 0), At(components, 1), At(components, 2) };

            case Family.DeviceCmyk:
                return CmykToRgb(
                    At(components, 0), At(components, 1), At(components, 2), At(components, 3));

            case Family.CalGray:
                return CalGrayToRgb(At(components, 0));

            case Family.CalRgb:
                return CalRgbToRgb(At(components, 0), At(components, 1), At(components, 2));

            case Family.Lab:
                return LabToRgb(At(components, 0), At(components, 1), At(components, 2));

            case Family.IccBased:
                return _alternate is not null
                    ? _alternate.ToRgb(components)
                    : FallbackByCount(components);

            case Family.Indexed:
                return IndexedToRgb(At(components, 0));

            case Family.Separation:
            case Family.DeviceN:
                return TintToRgb(components);

            case Family.Pattern:
            default:
                return new double[] { 0.0, 0.0, 0.0 };
        }
    }

    /// <summary>
    /// Parses a colour-space object: a name (such as <c>/DeviceRGB</c>) or an
    /// array (such as <c>[/ICCBased stream]</c>). Returns <see langword="null"/>
    /// when the object cannot be understood.
    /// </summary>
    /// <param name="colorSpace">The colour-space primitive.</param>
    /// <param name="objects">The object store used to resolve references.</param>
    /// <returns>The resolved space, or <see langword="null"/>.</returns>
    public static ResolvedColorSpace? Parse(PdfPrimitive colorSpace, PdfObjectStore objects)
    {
        ArgumentNullException.ThrowIfNull(colorSpace);
        ArgumentNullException.ThrowIfNull(objects);

        PdfPrimitive resolved = objects.Resolve(colorSpace);

        if (resolved is PdfName name)
        {
            return FromName(name.Value);
        }

        if (resolved is PdfArray array && array.Count > 0
            && objects.Resolve(array[0]) is PdfName family)
        {
            return FromArray(family.Value, array, objects);
        }

        return null;
    }

    private static ResolvedColorSpace? FromName(string name)
    {
        return name switch
        {
            "DeviceGray" or "G" => new ResolvedColorSpace(Family.DeviceGray, 1),
            "DeviceRGB" or "RGB" => new ResolvedColorSpace(Family.DeviceRgb, 3),
            "DeviceCMYK" or "CMYK" => new ResolvedColorSpace(Family.DeviceCmyk, 4),
            "Pattern" => new ResolvedColorSpace(Family.Pattern, 1),
            _ => null,
        };
    }

    private static ResolvedColorSpace? FromArray(string family, PdfArray array, PdfObjectStore objects)
    {
        switch (family)
        {
            case "ICCBased":
                return ParseIccBased(array, objects);
            case "Indexed":
            case "I":
                return ParseIndexed(array, objects);
            case "Separation":
                return ParseSeparation(array, objects);
            case "DeviceN":
                return ParseDeviceN(array, objects);
            case "Lab":
                return ParseLab(array, objects);
            case "CalRGB":
                return ParseCalRgb(array, objects);
            case "CalGray":
                return ParseCalGray(array, objects);
            case "Pattern":
                return new ResolvedColorSpace(Family.Pattern, 1);
            case "DeviceGray":
            case "DeviceRGB":
            case "DeviceCMYK":
                return FromName(family);
            default:
                return null;
        }
    }

    private static ResolvedColorSpace? ParseIccBased(PdfArray array, PdfObjectStore objects)
    {
        if (array.Count < 2 || objects.Resolve(array[1]) is not PdfStream stream)
        {
            return null;
        }

        int n = stream.Dictionary.GetInteger(PdfName.Intern("N"), 3);

        ResolvedColorSpace? alternate = null;
        if (stream.Dictionary.TryGetValue(PdfName.Intern("Alternate"), out PdfPrimitive? alt)
            && alt is not null)
        {
            alternate = Parse(alt, objects);
        }

        alternate ??= n switch
        {
            1 => new ResolvedColorSpace(Family.DeviceGray, 1),
            4 => new ResolvedColorSpace(Family.DeviceCmyk, 4),
            _ => new ResolvedColorSpace(Family.DeviceRgb, 3),
        };

        return new ResolvedColorSpace(Family.IccBased, n, alternate: alternate);
    }

    private static ResolvedColorSpace? ParseIndexed(PdfArray array, PdfObjectStore objects)
    {
        if (array.Count < 4)
        {
            return null;
        }

        ResolvedColorSpace? baseSpace = Parse(array[1], objects);
        if (baseSpace is null)
        {
            return null;
        }

        int hival = (int)ToDouble(objects.Resolve(array[2]));
        byte[] lookup = ReadLookup(objects.Resolve(array[3]));

        return new ResolvedColorSpace(
            Family.Indexed, 1, baseSpace: baseSpace, hival: hival, lookup: lookup);
    }

    private static ResolvedColorSpace? ParseSeparation(PdfArray array, PdfObjectStore objects)
    {
        if (array.Count < 4)
        {
            return null;
        }

        ResolvedColorSpace? alternate = Parse(array[2], objects);
        if (alternate is null)
        {
            return null;
        }

        PdfFunction tint = PdfFunction.Parse(array[3], objects);
        return new ResolvedColorSpace(Family.Separation, 1, alternate: alternate, tint: tint);
    }

    private static ResolvedColorSpace? ParseDeviceN(PdfArray array, PdfObjectStore objects)
    {
        if (array.Count < 4 || objects.Resolve(array[1]) is not PdfArray names)
        {
            return null;
        }

        ResolvedColorSpace? alternate = Parse(array[2], objects);
        if (alternate is null)
        {
            return null;
        }

        PdfFunction tint = PdfFunction.Parse(array[3], objects);
        return new ResolvedColorSpace(Family.DeviceN, names.Count, alternate: alternate, tint: tint);
    }

    private static ResolvedColorSpace? ParseLab(PdfArray array, PdfObjectStore objects)
    {
        PdfDictionary? dict = array.Count >= 2
            ? objects.ResolveAs<PdfDictionary>(array[1])
            : null;

        double[] white = ReadDoubles(dict?.GetArray(PdfName.Intern("WhitePoint")), objects)
            ?? new double[] { 0.9505, 1.0, 1.0890 };
        double[] range = ReadDoubles(dict?.GetArray(PdfName.Intern("Range")), objects)
            ?? new double[] { -100.0, 100.0, -100.0, 100.0 };

        return new ResolvedColorSpace(Family.Lab, 3, whitePoint: white, labRange: range);
    }

    private static ResolvedColorSpace? ParseCalRgb(PdfArray array, PdfObjectStore objects)
    {
        PdfDictionary? dict = array.Count >= 2
            ? objects.ResolveAs<PdfDictionary>(array[1])
            : null;

        double[] white = ReadDoubles(dict?.GetArray(PdfName.Intern("WhitePoint")), objects)
            ?? new double[] { 0.9505, 1.0, 1.0890 };
        double[] gamma = ReadDoubles(dict?.GetArray(PdfName.Intern("Gamma")), objects)
            ?? new double[] { 1.0, 1.0, 1.0 };
        double[] matrix = ReadDoubles(dict?.GetArray(PdfName.Intern("Matrix")), objects)
            ?? new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

        return new ResolvedColorSpace(
            Family.CalRgb, 3, whitePoint: white, gamma: gamma, matrix: matrix);
    }

    private static ResolvedColorSpace? ParseCalGray(PdfArray array, PdfObjectStore objects)
    {
        PdfDictionary? dict = array.Count >= 2
            ? objects.ResolveAs<PdfDictionary>(array[1])
            : null;

        double[] white = ReadDoubles(dict?.GetArray(PdfName.Intern("WhitePoint")), objects)
            ?? new double[] { 0.9505, 1.0, 1.0890 };
        double gammaValue = dict?.GetNumber(PdfName.Intern("Gamma"), 1.0) ?? 1.0;
        if (gammaValue <= 0.0)
        {
            gammaValue = 1.0;
        }

        return new ResolvedColorSpace(
            Family.CalGray, 1, whitePoint: white, gamma: new double[] { gammaValue });
    }

    // ── Conversions ───────────────────────────────────────────────────────────

    private static double[] CmykToRgb(double c, double m, double y, double k)
    {
        return new double[]
        {
            (1.0 - c) * (1.0 - k),
            (1.0 - m) * (1.0 - k),
            (1.0 - y) * (1.0 - k),
        };
    }

    private double[] CalGrayToRgb(double a)
    {
        double gamma = _gamma is { Length: > 0 } ? _gamma[0] : 1.0;
        double ag = Math.Pow(Clamp01(a), gamma);
        return XyzToSrgb(_whitePoint[0] * ag, _whitePoint[1] * ag, _whitePoint[2] * ag);
    }

    private double[] CalRgbToRgb(double a, double b, double c)
    {
        double[] g = _gamma ?? new double[] { 1.0, 1.0, 1.0 };
        double[] m = _matrix ?? new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

        double ga = Math.Pow(Clamp01(a), g[0]);
        double gb = Math.Pow(Clamp01(b), g[1]);
        double gc = Math.Pow(Clamp01(c), g[2]);

        // Matrix columns are [XA YA ZA  XB YB ZB  XC YC ZC].
        double x = (m[0] * ga) + (m[3] * gb) + (m[6] * gc);
        double y = (m[1] * ga) + (m[4] * gb) + (m[7] * gc);
        double z = (m[2] * ga) + (m[5] * gb) + (m[8] * gc);

        return XyzToSrgb(x, y, z);
    }

    private double[] LabToRgb(double l, double a, double b)
    {
        double[] range = _labRange ?? new double[] { -100.0, 100.0, -100.0, 100.0 };
        double aa = Math.Clamp(a, range[0], range[1]);
        double bb = Math.Clamp(b, range[2], range[3]);

        double fy = (l + 16.0) / 116.0;
        double fx = fy + (aa / 500.0);
        double fz = fy - (bb / 200.0);

        double xr = LabInverse(fx);
        double yr = LabInverse(fy);
        double zr = LabInverse(fz);

        double x = xr * _whitePoint[0];
        double y = yr * _whitePoint[1];
        double z = zr * _whitePoint[2];

        return XyzToSrgb(x, y, z);
    }

    private double[] IndexedToRgb(double indexValue)
    {
        if (_base is null || _lookup is null)
        {
            return new double[] { 0.0, 0.0, 0.0 };
        }

        int index = (int)Math.Round(indexValue);
        index = Math.Clamp(index, 0, _hival);

        int channels = _base.ComponentCount;
        int offset = index * channels;
        double[] baseComponents = new double[channels];
        for (int i = 0; i < channels; i++)
        {
            int p = offset + i;
            baseComponents[i] = p < _lookup.Length ? _lookup[p] / 255.0 : 0.0;
        }

        return _base.ToRgb(baseComponents);
    }

    private double[] TintToRgb(double[] components)
    {
        if (_tint is null || _alternate is null)
        {
            return new double[] { 0.0, 0.0, 0.0 };
        }

        double[] input = new double[_componentCount];
        for (int i = 0; i < input.Length; i++)
        {
            input[i] = At(components, i);
        }

        double[] altComponents = _tint.Evaluate(input);
        return _alternate.ToRgb(altComponents);
    }

    private double[] FallbackByCount(double[] components)
    {
        switch (_componentCount)
        {
            case 1:
                {
                    double g = At(components, 0);
                    return new double[] { g, g, g };
                }

            case 4:
                return CmykToRgb(
                    At(components, 0), At(components, 1), At(components, 2), At(components, 3));

            default:
                return new double[] { At(components, 0), At(components, 1), At(components, 2) };
        }
    }

    // ── Shared maths ──────────────────────────────────────────────────────────

    private static double[] XyzToSrgb(double x, double y, double z)
    {
        double r = (3.2406 * x) - (1.5372 * y) - (0.4986 * z);
        double g = (-0.9689 * x) + (1.8758 * y) + (0.0415 * z);
        double b = (0.0557 * x) - (0.2040 * y) + (1.0570 * z);

        return new double[]
        {
            Clamp01(GammaEncode(r)),
            Clamp01(GammaEncode(g)),
            Clamp01(GammaEncode(b)),
        };
    }

    private static double GammaEncode(double linear)
    {
        double c = Math.Max(0.0, linear);
        return c <= 0.0031308
            ? 12.92 * c
            : (1.055 * Math.Pow(c, 1.0 / 2.4)) - 0.055;
    }

    private static double LabInverse(double f)
    {
        // g(t) = t^3 for t > 6/29, else linearised near zero.
        const double delta = 6.0 / 29.0;
        return f > delta
            ? f * f * f
            : 3.0 * delta * delta * (f - (4.0 / 29.0));
    }

    private static double Clamp01(double v)
    {
        return Math.Clamp(v, 0.0, 1.0);
    }

    private static double At(double[] components, int index)
    {
        return index >= 0 && index < components.Length ? components[index] : 0.0;
    }

    private static double ToDouble(PdfPrimitive? primitive)
    {
        return primitive switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => 0.0,
        };
    }

    private static double[]? ReadDoubles(PdfArray? array, PdfObjectStore objects)
    {
        if (array is null)
        {
            return null;
        }

        double[] values = new double[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            values[i] = ToDouble(objects.Resolve(array[i]));
        }

        return values;
    }

    private static byte[] ReadLookup(PdfPrimitive lookup)
    {
        if (lookup is PdfString s)
        {
            return s.Bytes;
        }

        if (lookup is PdfStream stream)
        {
            return DecodeStream(stream);
        }

        return Array.Empty<byte>();
    }

    private static byte[] DecodeStream(PdfStream stream)
    {
        if (!stream.IsFiltered)
        {
            return stream.RawBytes;
        }

        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        byte[] data = stream.RawBytes;
        PdfPrimitive? filter = stream.Filter;

        if (filter is PdfName name)
        {
            return pipeline.Decode(FilterRegistry.ResolveAlias(name.Value), data, null);
        }

        if (filter is PdfArray chain)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                if (chain[i] is PdfName element)
                {
                    data = pipeline.Decode(FilterRegistry.ResolveAlias(element.Value), data, null);
                }
            }
        }

        return data;
    }
}
