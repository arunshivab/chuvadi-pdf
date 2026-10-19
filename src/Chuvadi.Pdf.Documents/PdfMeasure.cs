// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.9 — Measurement properties: §12.9.2 viewports
//        (/VP), §12.9.3 measure dictionaries (/Measure, rectilinear /RL),
//        Table 261 number format dictionaries (/NumberFormat)
// PHASE: 3.21 (PDF-15) — real-world measurement on scaled drawings.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>How a number format displays fractional values (Table 261 <c>/F</c>).</summary>
public enum PdfFractionFormat
{
    /// <summary>Decimal, with the precision given by <see cref="PdfNumberFormat.Denominator"/>.</summary>
    Decimal,

    /// <summary>A fraction whose denominator is <see cref="PdfNumberFormat.Denominator"/>.</summary>
    Fraction,

    /// <summary>Rounded to the nearest whole unit.</summary>
    Round,

    /// <summary>Truncated to a whole unit.</summary>
    Truncate,
}

/// <summary>
/// One step of a measurement unit chain (a <c>/NumberFormat</c> dictionary):
/// its label and the factor converting the previous unit into it — the first
/// format converts default user-space units into the first display unit.
/// </summary>
public sealed class PdfNumberFormat
{
    internal PdfNumberFormat(
        string unit, double conversionFactor, PdfFractionFormat fraction, int denominator,
        bool forceDenominator, string thousandsSeparator, string decimalSeparator,
        string prefixSpacing, string suffixSpacing, bool labelBefore)
    {
        Unit = unit;
        ConversionFactor = conversionFactor;
        Fraction = fraction;
        Denominator = denominator;
        ForceDenominator = forceDenominator;
        ThousandsSeparator = thousandsSeparator;
        DecimalSeparator = decimalSeparator;
        PrefixSpacing = prefixSpacing;
        SuffixSpacing = suffixSpacing;
        LabelBefore = labelBefore;
    }

    /// <summary>Gets the unit label (<c>/U</c>), for example <c>"mm"</c> or <c>"ft"</c>.</summary>
    public string Unit { get; }

    /// <summary>Gets the conversion factor (<c>/C</c>) from the previous unit into this one.</summary>
    public double ConversionFactor { get; }

    /// <summary>Gets how fractional values are shown (<c>/F</c>).</summary>
    public PdfFractionFormat Fraction { get; }

    /// <summary>
    /// Gets the precision (<c>/D</c>): for decimals, a power of ten (100 = two
    /// places); for fractions, the denominator.
    /// </summary>
    public int Denominator { get; }

    /// <summary>Gets whether fractions keep the full denominator (<c>/FD</c>) rather than being reduced.</summary>
    public bool ForceDenominator { get; }

    /// <summary>Gets the thousands separator (<c>/RT</c>).</summary>
    public string ThousandsSeparator { get; }

    /// <summary>Gets the decimal separator (<c>/RD</c>).</summary>
    public string DecimalSeparator { get; }

    /// <summary>Gets the text placed before the label (<c>/PS</c>).</summary>
    public string PrefixSpacing { get; }

    /// <summary>Gets the text placed after the label (<c>/SS</c>).</summary>
    public string SuffixSpacing { get; }

    /// <summary>Gets whether the label goes before the value (<c>/O /P</c>).</summary>
    public bool LabelBefore { get; }

    internal static IReadOnlyList<PdfNumberFormat> ReadArray(PdfPrimitive? value, IPdfObjectResolver resolver)
    {
        if (value is null || resolver.Resolve(value) is not PdfArray array)
        {
            return Array.Empty<PdfNumberFormat>();
        }

        List<PdfNumberFormat> formats = new List<PdfNumberFormat>(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            if (resolver.Resolve(array[i]) is PdfDictionary dict)
            {
                formats.Add(Read(dict, resolver));
            }
        }

        return formats;
    }

    private static PdfNumberFormat Read(PdfDictionary dict, IPdfObjectResolver resolver)
    {
        string unit = Text(dict, "U", resolver) ?? string.Empty;
        double factor = Number(dict, "C", resolver, 1.0);
        PdfFractionFormat fraction = NameValue(dict, "F", resolver) switch
        {
            "F" => PdfFractionFormat.Fraction,
            "R" => PdfFractionFormat.Round,
            "T" => PdfFractionFormat.Truncate,
            _ => PdfFractionFormat.Decimal,
        };
        int denominator = (int)Math.Clamp(Number(dict, "D", resolver, fraction == PdfFractionFormat.Fraction ? 16 : 100), 1, 1_000_000);
        bool forceDenominator = Get(dict, "FD") is PdfPrimitive fdValue && resolver.Resolve(fdValue) is PdfBoolean { Value: true };
        string thousands = Text(dict, "RT", resolver) ?? ",";
        string decimals = Text(dict, "RD", resolver) ?? ".";
        string prefix = Text(dict, "PS", resolver) ?? " ";
        string suffix = Text(dict, "SS", resolver) ?? " ";
        bool before = NameValue(dict, "O", resolver) == "P";
        return new PdfNumberFormat(unit, factor, fraction, denominator, forceDenominator, thousands, decimals, prefix, suffix, before);
    }

    internal static string? NameValue(PdfDictionary dict, string key, IPdfObjectResolver resolver) =>
        Get(dict, key) is PdfPrimitive value && resolver.Resolve(value) is PdfName name ? name.Value : null;

    internal static PdfPrimitive? Get(PdfDictionary dict, string key) =>
        dict.TryGetValue(PdfName.Intern(key), out PdfPrimitive? value) ? value : null;

    internal static double Number(PdfDictionary dict, string key, IPdfObjectResolver resolver, double fallback) =>
        Get(dict, key) is PdfPrimitive value ? resolver.Resolve(value) switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => fallback,
        } : fallback;

    internal static string? Text(PdfDictionary dict, string key, IPdfObjectResolver resolver) =>
        Get(dict, key) is PdfPrimitive value && resolver.Resolve(value) is PdfString s ? s.ToTextString() : null;
}

/// <summary>A measured value in a drawing's real-world units.</summary>
public readonly struct PdfMeasurement : IEquatable<PdfMeasurement>
{
    internal PdfMeasurement(double value, string unit, string formatted)
    {
        Value = value;
        Unit = unit;
        Formatted = formatted;
    }

    /// <summary>Gets the value in the first (largest) unit of the chain.</summary>
    public double Value { get; }

    /// <summary>Gets the first unit's label.</summary>
    public string Unit { get; }

    /// <summary>
    /// Gets the value formatted as the document asks (precision, fractions,
    /// separators, multi-unit chains such as <c>5 ft 3 in</c>).
    /// </summary>
    public string Formatted { get; }

    /// <inheritdoc />
    public bool Equals(PdfMeasurement other) =>
        Value.Equals(other.Value) && Unit == other.Unit && Formatted == other.Formatted;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PdfMeasurement other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Value, Unit, Formatted);

    /// <inheritdoc />
    public override string ToString() => Formatted;

    /// <summary>Compares two measurements for equality.</summary>
    /// <param name="left">The first measurement.</param>
    /// <param name="right">The second measurement.</param>
    /// <returns>Whether they are equal.</returns>
    public static bool operator ==(PdfMeasurement left, PdfMeasurement right) => left.Equals(right);

    /// <summary>Compares two measurements for inequality.</summary>
    /// <param name="left">The first measurement.</param>
    /// <param name="right">The second measurement.</param>
    /// <returns>Whether they differ.</returns>
    public static bool operator !=(PdfMeasurement left, PdfMeasurement right) => !left.Equals(right);
}

/// <summary>
/// A measure dictionary (<c>/Measure</c>): how distances and areas in a
/// region of a page map to real-world units — for example a 1:100 floor plan.
/// Rectilinear measures (<c>/Subtype /RL</c>, the default) are converted by
/// <see cref="Distance"/> and <see cref="Area"/>; geospatial measures
/// (<c>/GEO</c>) are reported but not converted.
/// </summary>
public sealed class PdfMeasure
{
    private PdfMeasure(
        string subtype, string? scaleRatio,
        IReadOnlyList<PdfNumberFormat> x, IReadOnlyList<PdfNumberFormat> y,
        IReadOnlyList<PdfNumberFormat> distance, IReadOnlyList<PdfNumberFormat> area,
        double originX, double originY, double yToX)
    {
        Subtype = subtype;
        ScaleRatio = scaleRatio;
        X = x;
        Y = y;
        DistanceFormats = distance;
        AreaFormats = area;
        OriginX = originX;
        OriginY = originY;
        YToX = yToX;
    }

    /// <summary>Gets the measure subtype: <c>"RL"</c> (rectilinear) or <c>"GEO"</c>.</summary>
    public string Subtype { get; }

    /// <summary>Gets whether this is a rectilinear measure that the conversions support.</summary>
    public bool IsRectilinear => Subtype == "RL";

    /// <summary>Gets the human-readable scale ratio (<c>/R</c>), for example <c>"1 in = 10 ft"</c>.</summary>
    public string? ScaleRatio { get; }

    /// <summary>Gets the x-axis unit chain (<c>/X</c>).</summary>
    public IReadOnlyList<PdfNumberFormat> X { get; }

    /// <summary>Gets the y-axis unit chain (<c>/Y</c>; the x chain when absent).</summary>
    public IReadOnlyList<PdfNumberFormat> Y { get; }

    /// <summary>Gets the distance unit chain (<c>/D</c>; the x chain when absent).</summary>
    public IReadOnlyList<PdfNumberFormat> DistanceFormats { get; }

    /// <summary>Gets the area unit chain (<c>/A</c>; empty when absent).</summary>
    public IReadOnlyList<PdfNumberFormat> AreaFormats { get; }

    /// <summary>Gets the origin x (<c>/O</c>) in default user space, for coordinate readouts.</summary>
    public double OriginX { get; }

    /// <summary>Gets the origin y (<c>/O</c>) in default user space.</summary>
    public double OriginY { get; }

    /// <summary>Gets the y-to-x unit factor (<c>/CYX</c>), 1 when absent.</summary>
    public double YToX { get; }

    /// <summary>
    /// Reads a measure dictionary (from a viewport, or an annotation's
    /// <c>/Measure</c> entry). Returns null when the value is not a dictionary
    /// or a rectilinear measure has no x-axis format.
    /// </summary>
    /// <param name="value">The <c>/Measure</c> value.</param>
    /// <param name="resolver">Resolves indirect references.</param>
    /// <returns>The measure, or null.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static PdfMeasure? FromDictionary(PdfPrimitive value, IPdfObjectResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(resolver);
        if (resolver.Resolve(value) is not PdfDictionary dict)
        {
            return null;
        }

        string subtype = PdfNumberFormat.NameValue(dict, "Subtype", resolver) ?? "RL";
        string? ratio = PdfNumberFormat.Text(dict, "R", resolver);
        IReadOnlyList<PdfNumberFormat> x = PdfNumberFormat.ReadArray(PdfNumberFormat.Get(dict, "X"), resolver);
        if (subtype == "RL" && x.Count == 0)
        {
            return null;
        }

        IReadOnlyList<PdfNumberFormat> y = PdfNumberFormat.ReadArray(PdfNumberFormat.Get(dict, "Y"), resolver);
        IReadOnlyList<PdfNumberFormat> d = PdfNumberFormat.ReadArray(PdfNumberFormat.Get(dict, "D"), resolver);
        IReadOnlyList<PdfNumberFormat> a = PdfNumberFormat.ReadArray(PdfNumberFormat.Get(dict, "A"), resolver);

        double ox = 0, oy = 0;
        if (PdfNumberFormat.Get(dict, "O") is PdfPrimitive o && resolver.Resolve(o) is PdfArray origin && origin.Count >= 2)
        {
            ox = NumberOf(origin[0], resolver);
            oy = NumberOf(origin[1], resolver);
        }

        double cyx = PdfNumberFormat.Number(dict, "CYX", resolver, 1.0);
        return new PdfMeasure(subtype, ratio, x, y.Count > 0 ? y : x, d.Count > 0 ? d : x, a, ox, oy, cyx);
    }

    /// <summary>
    /// Converts a straight-line distance between two points in default user
    /// space into the drawing's units (the <c>/D</c> chain).
    /// </summary>
    /// <param name="x1">First point x, in default user space.</param>
    /// <param name="y1">First point y.</param>
    /// <param name="x2">Second point x.</param>
    /// <param name="y2">Second point y.</param>
    /// <returns>The measured distance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the measure is not rectilinear.</exception>
    public PdfMeasurement Distance(double x1, double y1, double x2, double y2)
    {
        RequireRectilinear();
        double dx = x2 - x1;
        double dy = y2 - y1;
        return Convert(Math.Sqrt((dx * dx) + (dy * dy)), DistanceFormats);
    }

    /// <summary>
    /// Converts an area in square default-user-space units into the drawing's
    /// units: the <c>/A</c> chain when present, otherwise the x and y factors
    /// (labelled with the x unit, squared).
    /// </summary>
    /// <param name="userSpaceArea">The area in square default user-space units.</param>
    /// <returns>The measured area.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the measure is not rectilinear.</exception>
    public PdfMeasurement Area(double userSpaceArea)
    {
        RequireRectilinear();
        if (AreaFormats.Count > 0)
        {
            return Convert(userSpaceArea, AreaFormats);
        }

        double value = userSpaceArea * X[0].ConversionFactor * Y[0].ConversionFactor;
        string unit = X[0].Unit.Length > 0 ? "sq " + X[0].Unit : string.Empty;
        return new PdfMeasurement(value, unit, FormatSingle(value, X[0], unit));
    }

    /// <summary>
    /// Converts a point in default user space into the drawing's x/y
    /// coordinates, measured from the measure's origin.
    /// </summary>
    /// <param name="x">Point x, in default user space.</param>
    /// <param name="y">Point y, in default user space.</param>
    /// <returns>The x and y coordinates in the drawing's units.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the measure is not rectilinear.</exception>
    public (PdfMeasurement X, PdfMeasurement Y) Coordinates(double x, double y)
    {
        RequireRectilinear();
        return (Convert(x - OriginX, X), Convert(y - OriginY, Y));
    }

    private void RequireRectilinear()
    {
        if (!IsRectilinear)
        {
            throw new InvalidOperationException(
                "Only rectilinear (/RL) measures can be converted; this measure is /" + Subtype + ".");
        }
    }

    private static double NumberOf(PdfPrimitive value, IPdfObjectResolver resolver) => resolver.Resolve(value) switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };

    // Applies the first format's factor, then formats through the chain:
    // whole units of each format, the remainder converted by the next.
    private static PdfMeasurement Convert(double userSpaceValue, IReadOnlyList<PdfNumberFormat> chain)
    {
        double value = userSpaceValue * chain[0].ConversionFactor;
        if (chain.Count == 1)
        {
            return new PdfMeasurement(value, chain[0].Unit, FormatSingle(value, chain[0], chain[0].Unit));
        }

        StringBuilder sb = new StringBuilder();
        bool negative = value < 0;
        double remaining = Math.Abs(value);
        for (int i = 0; i < chain.Count; i++)
        {
            PdfNumberFormat format = chain[i];
            bool last = i == chain.Count - 1;
            double shown = last ? remaining : Math.Floor(remaining);
            if (shown != 0 || last || sb.Length == 0)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                // Every unit but the last shows whole units only.
                sb.Append(last
                    ? FormatSingle(shown, format, format.Unit)
                    : WithLabel(GroupThousands(shown, 0, format), format, format.Unit));
            }

            if (!last)
            {
                remaining = (remaining - Math.Floor(remaining)) * chain[i + 1].ConversionFactor;
            }
        }

        string text = negative ? "-" + sb : sb.ToString();
        return new PdfMeasurement(value, chain[0].Unit, text);
    }

    private static string FormatSingle(double value, PdfNumberFormat format, string unit)
    {
        string number = format.Fraction switch
        {
            PdfFractionFormat.Round => GroupThousands(Math.Round(value, MidpointRounding.AwayFromZero), 0, format),
            PdfFractionFormat.Truncate => GroupThousands(Math.Truncate(value), 0, format),
            PdfFractionFormat.Fraction => FormatFraction(value, format),
            _ => GroupThousands(value, DecimalPlaces(format.Denominator), format),
        };

        return WithLabel(number, format, unit);
    }

    private static string WithLabel(string number, PdfNumberFormat format, string unit)
    {
        if (unit.Length == 0)
        {
            return number;
        }

        return format.LabelBefore
            ? unit + format.SuffixSpacing + number
            : number + format.PrefixSpacing + unit;
    }

    private static int DecimalPlaces(int precision)
    {
        int places = 0;
        while (precision >= 10 && places < 12)
        {
            precision /= 10;
            places++;
        }

        return places;
    }

    private static string GroupThousands(double value, int places, PdfNumberFormat format)
    {
        double rounded = Math.Round(value, places, MidpointRounding.AwayFromZero);
        string raw = rounded.ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        bool negative = raw.StartsWith('-');
        if (negative)
        {
            raw = raw.Substring(1);
        }

        int dot = raw.IndexOf('.', StringComparison.Ordinal);
        string whole = dot >= 0 ? raw.Substring(0, dot) : raw;
        string fraction = dot >= 0 ? raw.Substring(dot + 1) : string.Empty;

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < whole.Length; i++)
        {
            if (i > 0 && (whole.Length - i) % 3 == 0)
            {
                sb.Append(format.ThousandsSeparator);
            }

            sb.Append(whole[i]);
        }

        if (fraction.Length > 0)
        {
            sb.Append(format.DecimalSeparator).Append(fraction);
        }

        return negative ? "-" + sb : sb.ToString();
    }

    private static string FormatFraction(double value, PdfNumberFormat format)
    {
        bool negative = value < 0;
        double magnitude = Math.Abs(value);
        long whole = (long)Math.Floor(magnitude);
        long numerator = (long)Math.Round((magnitude - whole) * format.Denominator, MidpointRounding.AwayFromZero);
        long denominator = format.Denominator;
        if (numerator == denominator)
        {
            whole++;
            numerator = 0;
        }

        if (!format.ForceDenominator && numerator > 0)
        {
            long g = Gcd(numerator, denominator);
            numerator /= g;
            denominator /= g;
        }

        string wholeText = GroupThousands(whole, 0, format);
        string text = numerator == 0
            ? wholeText
            : (whole == 0 ? string.Empty : wholeText + " ")
                + numerator.ToString(CultureInfo.InvariantCulture) + "/" + denominator.ToString(CultureInfo.InvariantCulture);
        return negative && (whole != 0 || numerator != 0) ? "-" + text : text;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return Math.Max(a, 1);
    }
}

/// <summary>
/// A viewport (<c>/VP</c> entry): a rectangular region of a page with its
/// own name and measurement scale.
/// </summary>
public sealed class PdfViewport
{
    internal PdfViewport(PdfRectangle bounds, string? name, PdfMeasure? measure)
    {
        Bounds = bounds;
        Name = name;
        Measure = measure;
    }

    /// <summary>Gets the region (<c>/BBox</c>) in default user space.</summary>
    public PdfRectangle Bounds { get; }

    /// <summary>Gets the viewport's name (<c>/Name</c>), or null.</summary>
    public string? Name { get; }

    /// <summary>Gets the region's measure (<c>/Measure</c>), or null when it has none.</summary>
    public PdfMeasure? Measure { get; }

    /// <summary>Gets whether the point (default user space) lies inside the region.</summary>
    /// <param name="x">Point x.</param>
    /// <param name="y">Point y.</param>
    /// <returns>Whether the point is inside (edges included).</returns>
    public bool Contains(double x, double y)
    {
        double x0 = Math.Min(Bounds.X1, Bounds.X2);
        double x1 = Math.Max(Bounds.X1, Bounds.X2);
        double y0 = Math.Min(Bounds.Y1, Bounds.Y2);
        double y1 = Math.Max(Bounds.Y1, Bounds.Y2);
        return x >= x0 && x <= x1 && y >= y0 && y <= y1;
    }

    internal static IReadOnlyList<PdfViewport> ReadArray(PdfPrimitive? value, IPdfObjectResolver resolver)
    {
        if (value is null || resolver.Resolve(value) is not PdfArray array)
        {
            return Array.Empty<PdfViewport>();
        }

        List<PdfViewport> viewports = new List<PdfViewport>(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            if (resolver.Resolve(array[i]) is not PdfDictionary dict
                || PdfNumberFormat.Get(dict, "BBox") is not PdfPrimitive bboxValue
                || resolver.Resolve(bboxValue) is not PdfArray bbox
                || bbox.Count < 4)
            {
                continue;
            }

            PdfRectangle bounds = new PdfRectangle(
                Num(bbox[0], resolver), Num(bbox[1], resolver), Num(bbox[2], resolver), Num(bbox[3], resolver));
            string? name = PdfNumberFormat.Text(dict, "Name", resolver);
            PdfMeasure? measure = PdfNumberFormat.Get(dict, "Measure") is PdfPrimitive m
                ? PdfMeasure.FromDictionary(m, resolver)
                : null;
            viewports.Add(new PdfViewport(bounds, name, measure));
        }

        return viewports;
    }

    private static double Num(PdfPrimitive value, IPdfObjectResolver resolver) => resolver.Resolve(value) switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };
}
