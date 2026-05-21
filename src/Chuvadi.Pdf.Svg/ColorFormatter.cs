// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  W3C CSS Color Module Level 3 — basic color syntax used by SVG
// PHASE: v2.0.0 R2 — SVG renderer

using System.Globalization;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Formats a <see cref="ColorF"/> value into an SVG-compatible colour
/// string. Always converts to DeviceRGB first (using
/// <see cref="ColorF.ToRgb"/>) so CMYK inputs are reduced via the standard
/// subtractive approximation.
/// </summary>
internal static class ColorFormatter
{
    /// <summary>
    /// Returns an SVG colour attribute value for <paramref name="color"/>.
    /// Uses three named SVG colours where they exactly match
    /// (<c>black</c>, <c>white</c>, <c>none</c>) and a six-digit hex
    /// literal otherwise.
    /// </summary>
    internal static string ToSvgColor(ColorF color)
    {
        ColorF rgb = color.ToRgb();

        int r = ToByte(rgb.R);
        int g = ToByte(rgb.G);
        int b = ToByte(rgb.B);

        if (r == 0 && g == 0 && b == 0)
        {
            return "black";
        }

        if (r == 255 && g == 255 && b == 255)
        {
            return "white";
        }

        return "#" +
            r.ToString("x2", CultureInfo.InvariantCulture) +
            g.ToString("x2", CultureInfo.InvariantCulture) +
            b.ToString("x2", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns the alpha channel of <paramref name="color"/> in the range
    /// [0,1], or 1.0 when the colour space carries no alpha channel.
    /// </summary>
    internal static double Alpha(ColorF color)
    {
        return color.Alpha;
    }

    private static int ToByte(float component)
    {
        int v = (int)(component * 255f + 0.5f);

        if (v < 0)
        {
            return 0;
        }

        if (v > 255)
        {
            return 255;
        }

        return v;
    }
}
