// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.4.5 — Shading dictionaries
//        Type 2 (axial / linear), Type 3 (radial)
// PHASE: Phase 2 — rendering conformance (shadings)
// Parses an axial or radial shading dictionary and evaluates its colour along
// the parametric axis via its /Function. Geometry (/Coords, /Domain, /Extend)
// is exposed for the renderers to place the gradient; colour resolution is
// shared so both the SVG and raster sinks agree on stop colours.

using System;
using System.Globalization;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Content;

/// <summary>
/// An axial (Type 2) or radial (Type 3) shading (PDF 32000-1:2008 §8.7.4.5).
/// Use <see cref="Parse"/> to build one from a shading dictionary, then read
/// <see cref="Coords"/> for geometry and <see cref="EvaluateRgb"/> for the
/// colour at a normalised position along the axis.
/// </summary>
public sealed class PdfShading
{
    private readonly PdfFunction _function;
    private readonly int _componentCount;

    private PdfShading(
        int shadingType,
        double[] coords,
        double domainStart,
        double domainEnd,
        bool extendStart,
        bool extendEnd,
        PdfFunction function,
        PdfPrimitive? colorSpace)
    {
        ShadingType = shadingType;
        Coords = coords;
        DomainStart = domainStart;
        DomainEnd = domainEnd;
        ExtendStart = extendStart;
        ExtendEnd = extendEnd;
        _function = function;
        _componentCount = function.OutputCount;
        ColorSpace = colorSpace;
    }

    /// <summary>The shading type: 2 (axial) or 3 (radial).</summary>
    public int ShadingType { get; }

    /// <summary>
    /// Axis geometry. Axial: [x0, y0, x1, y1]. Radial: [x0, y0, r0, x1, y1, r1].
    /// </summary>
    public double[] Coords { get; }

    /// <summary>Lower bound of the parametric domain (default 0).</summary>
    public double DomainStart { get; }

    /// <summary>Upper bound of the parametric domain (default 1).</summary>
    public double DomainEnd { get; }

    /// <summary>Whether the shading extends beyond the start of the axis.</summary>
    public bool ExtendStart { get; }

    /// <summary>Whether the shading extends beyond the end of the axis.</summary>
    public bool ExtendEnd { get; }

    /// <summary>The raw /ColorSpace entry, or null when absent.</summary>
    public PdfPrimitive? ColorSpace { get; }

    /// <summary>True for an axial (linear) shading.</summary>
    public bool IsAxial => ShadingType == 2;

    /// <summary>True for a radial shading.</summary>
    public bool IsRadial => ShadingType == 3;

    /// <summary>
    /// Parses an axial or radial shading dictionary (or shading stream, whose
    /// dictionary is used).
    /// </summary>
    /// <param name="shading">The shading object or reference.</param>
    /// <param name="objects">The object store for resolving references.</param>
    /// <returns>The parsed shading.</returns>
    /// <exception cref="ContentException">
    /// Thrown for an unsupported shading type or a malformed dictionary.
    /// </exception>
    public static PdfShading Parse(PdfPrimitive shading, PdfObjectStore objects)
    {
        ArgumentNullException.ThrowIfNull(shading);
        ArgumentNullException.ThrowIfNull(objects);

        PdfPrimitive resolved = objects.Resolve(shading);
        PdfDictionary dict = (resolved as PdfStream)?.Dictionary
            ?? resolved as PdfDictionary
            ?? throw new ContentException("Shading object is not a dictionary or stream.");

        int shadingType = (int)dict.GetNumber(PdfName.Intern("ShadingType"), -1);
        if (shadingType != 2 && shadingType != 3)
        {
            throw new ContentException(
                "Unsupported /ShadingType " +
                shadingType.ToString(CultureInfo.InvariantCulture) +
                " (only axial 2 and radial 3 are supported).");
        }

        if (!dict.TryGetValue(PdfName.Intern("Coords"), out PdfPrimitive? coordsValue) ||
            objects.Resolve(coordsValue) is not PdfArray coordsArray)
        {
            throw new ContentException("Shading is missing the required /Coords array.");
        }

        int expected = shadingType == 2 ? 4 : 6;
        if (coordsArray.Count < expected)
        {
            throw new ContentException("Shading /Coords has too few entries for its type.");
        }

        double[] coords = new double[expected];
        for (int i = 0; i < expected; i++)
        {
            coords[i] = PdfReal.ToDouble(objects.Resolve(coordsArray[i]));
        }

        double domainStart = 0.0;
        double domainEnd = 1.0;
        if (dict.TryGetValue(PdfName.Intern("Domain"), out PdfPrimitive? domainValue) &&
            objects.Resolve(domainValue) is PdfArray domain && domain.Count >= 2)
        {
            domainStart = PdfReal.ToDouble(objects.Resolve(domain[0]));
            domainEnd = PdfReal.ToDouble(objects.Resolve(domain[1]));
        }

        bool extendStart = false;
        bool extendEnd = false;
        if (dict.TryGetValue(PdfName.Intern("Extend"), out PdfPrimitive? extendValue) &&
            objects.Resolve(extendValue) is PdfArray extend && extend.Count >= 2)
        {
            extendStart = extend.GetAs<PdfBoolean>(0)?.Value ?? false;
            extendEnd = extend.GetAs<PdfBoolean>(1)?.Value ?? false;
        }

        if (!dict.TryGetValue(PdfName.Intern("Function"), out PdfPrimitive? functionObject))
        {
            throw new ContentException("Axial/radial shading is missing the required /Function.");
        }
        PdfFunction function = PdfFunction.Parse(functionObject, objects);

        PdfPrimitive? colorSpace = dict.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? cs)
            ? objects.Resolve(cs)
            : null;

        return new PdfShading(
            shadingType, coords, domainStart, domainEnd, extendStart, extendEnd, function, colorSpace);
    }

    /// <summary>
    /// Evaluates the shading colour at a normalised axis position
    /// <paramref name="s"/> in [0, 1], which is mapped onto the parametric
    /// domain before the /Function is applied.
    /// </summary>
    /// <param name="s">Normalised position along the axis, clamped to [0, 1].</param>
    /// <returns>The colour components in the shading's colour space.</returns>
    public double[] EvaluateColor(double s)
    {
        if (s < 0.0)
        {
            s = 0.0;
        }
        else if (s > 1.0)
        {
            s = 1.0;
        }

        double t = DomainStart + (s * (DomainEnd - DomainStart));
        return _function.Evaluate(new double[] { t });
    }

    /// <summary>
    /// Evaluates the shading colour at a normalised axis position and converts
    /// it to sRGB. Device gray (1), RGB (3), and CMYK (4) component counts are
    /// converted directly; other counts are mapped to gray or truncated to RGB.
    /// </summary>
    /// <param name="s">Normalised position along the axis, clamped to [0, 1].</param>
    /// <returns>Red, green, and blue each in [0, 1].</returns>
    public (double R, double G, double B) EvaluateRgb(double s)
    {
        double[] c = EvaluateColor(s);
        return ToRgb(c);
    }

    /// <summary>
    /// Converts colour components in the shading's colour space to sRGB by
    /// component count: 1 = gray, 3 = RGB, 4 = CMYK.
    /// </summary>
    /// <param name="components">The colour components, each in [0, 1].</param>
    /// <returns>Red, green, and blue each in [0, 1].</returns>
    public (double R, double G, double B) ToRgb(double[] components)
    {
        ArgumentNullException.ThrowIfNull(components);

        int n = _componentCount;
        if (n == 1 && components.Length >= 1)
        {
            double g = Clamp01(components[0]);
            return (g, g, g);
        }

        if (n == 3 && components.Length >= 3)
        {
            return (Clamp01(components[0]), Clamp01(components[1]), Clamp01(components[2]));
        }

        if (n == 4 && components.Length >= 4)
        {
            // Standard naive CMYK -> RGB (components in [0,1]). Both render sinks
            // call this same path, so stop colours stay consistent between them.
            double c = Clamp01(components[0]);
            double m = Clamp01(components[1]);
            double y = Clamp01(components[2]);
            double k = Clamp01(components[3]);
            return ((1.0 - c) * (1.0 - k), (1.0 - m) * (1.0 - k), (1.0 - y) * (1.0 - k));
        }

        // Fallback: gray from the first component, or black.
        double v = components.Length > 0 ? Clamp01(components[0]) : 0.0;
        return (v, v, v);
    }

    private static double Clamp01(double value)
    {
        if (value < 0.0)
        {
            return 0.0;
        }
        if (value > 1.0)
        {
            return 1.0;
        }
        return value;
    }
}
