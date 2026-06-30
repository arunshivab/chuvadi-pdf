// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 §"Measurements" — coordinate and length units.
// PHASE: LA-23b Phase A — template model.

using System;
using System.Globalization;

namespace Chuvadi.Pdf.Xfa.Model;

/// <summary>
/// A linear measurement parsed from an XFA template attribute (for example
/// "12.7mm", "0.5in", "36pt"). Stored internally in PDF points (1/72 inch),
/// which is the unit used by the authoring and rendering layers.
/// </summary>
public readonly struct XfaMeasurement : IEquatable<XfaMeasurement>
{
    private const double PointsPerInch = 72.0;
    private const double MmPerInch = 25.4;
    private const double CmPerInch = 2.54;
    private const double PicasPerInch = 6.0;

    /// <summary>Initializes a measurement from a value already expressed in points.</summary>
    /// <param name="points">The length in PDF points.</param>
    public XfaMeasurement(double points)
    {
        Points = points;
    }

    /// <summary>Gets the measurement value in PDF points (1/72 inch).</summary>
    public double Points { get; }

    /// <summary>A zero-length measurement.</summary>
    public static XfaMeasurement Zero => new XfaMeasurement(0.0);

    /// <summary>
    /// Parses an XFA measurement string into points. Supported units: in, pt, mm,
    /// cm, pc (picas), px (treated as points at 72 dpi), em (relative to
    /// <paramref name="emPoints"/>), and % (relative to <paramref name="percentBasePoints"/>).
    /// A bare number with no unit is interpreted as points. Returns
    /// <see cref="Zero"/> when the input is null or empty.
    /// </summary>
    /// <param name="text">The measurement text, e.g. "12.7mm".</param>
    /// <param name="emPoints">The em size used to resolve "em" units, in points.</param>
    /// <param name="percentBasePoints">The base length used to resolve "%" units, in points.</param>
    /// <returns>The parsed measurement.</returns>
    /// <exception cref="FormatException">The numeric portion could not be parsed.</exception>
    public static XfaMeasurement Parse(string? text, double emPoints = 0.0, double percentBasePoints = 0.0)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Zero;
        }

        string trimmed = text.Trim();
        int unitStart = trimmed.Length;
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            bool numeric = (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '+';
            if (!numeric)
            {
                unitStart = i;
                break;
            }
        }

        string numberPart = trimmed.Substring(0, unitStart);
        string unitPart = trimmed.Substring(unitStart).Trim().ToUpperInvariant();

        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new FormatException($"Invalid XFA measurement '{text}'.");
        }

        double points = unitPart switch
        {
            "" => value,
            "PT" => value,
            "PX" => value,
            "IN" => value * PointsPerInch,
            "MM" => value / MmPerInch * PointsPerInch,
            "CM" => value / CmPerInch * PointsPerInch,
            "PC" => value / PicasPerInch * PointsPerInch,
            "EM" => value * emPoints,
            "%" => value / 100.0 * percentBasePoints,
            _ => throw new FormatException($"Unknown XFA measurement unit '{unitPart}' in '{text}'."),
        };

        return new XfaMeasurement(points);
    }

    /// <summary>
    /// Attempts to parse an XFA measurement string into points, returning
    /// <see cref="Zero"/> rather than throwing when the input is malformed.
    /// </summary>
    /// <param name="text">The measurement text.</param>
    /// <param name="result">The parsed measurement, or <see cref="Zero"/> on failure.</param>
    /// <returns><see langword="true"/> when parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? text, out XfaMeasurement result)
    {
        try
        {
            result = Parse(text);
            return true;
        }
        catch (FormatException)
        {
            result = Zero;
            return false;
        }
    }

    /// <inheritdoc />
    public bool Equals(XfaMeasurement other) => Points.Equals(other.Points);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is XfaMeasurement other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Points.GetHashCode();

    /// <summary>Equality operator.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when the two measurements are equal.</returns>
    public static bool operator ==(XfaMeasurement left, XfaMeasurement right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when the two measurements differ.</returns>
    public static bool operator !=(XfaMeasurement left, XfaMeasurement right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Points.ToString("0.###", CultureInfo.InvariantCulture) + "pt";
}
