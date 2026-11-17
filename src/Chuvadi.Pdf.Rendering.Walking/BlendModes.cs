// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// MOVED 3.31: from Chuvadi.Pdf.Rendering.DisplayList to Walking, same namespace (D-143).
// SPEC:  PDF 32000-1:2008 §11.3.5 — Blend modes
// PHASE: Phase 2 — item 12, ExtGState blend modes
//
// Maps PDF /BM names to blend modes. Compatible is treated as Normal. Since
// 3.35.0 the non-separable modes (Hue, Saturation, Color, Luminosity) are
// supported too (§11.3.5.3); before, they were treated as Normal.

using System;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Helpers for the PDF blend-mode (/BM) names.</summary>
public static class BlendModes
{
    /// <summary>
    /// Maps a PDF blend-mode name to a <see cref="PdfBlendMode"/>, returning
    /// <see cref="PdfBlendMode.Normal"/> for Compatible and unknown names
    /// (and, before 3.35.0, for the non-separable names).
    /// </summary>
    /// <param name="name">The blend-mode name, without the leading slash.</param>
    /// <returns>The mapped blend mode.</returns>
    public static PdfBlendMode FromName(string name)
    {
        return TryFromName(name, out PdfBlendMode mode) ? mode : PdfBlendMode.Normal;
    }

    /// <summary>
    /// Attempts to map a PDF blend-mode name to a supported
    /// <see cref="PdfBlendMode"/> other than Normal.
    /// </summary>
    /// <param name="name">The blend-mode name, without the leading slash.</param>
    /// <param name="mode">The mapped blend mode when supported.</param>
    /// <returns>
    /// True when <paramref name="name"/> is a supported mode other than
    /// Normal (the non-separable modes since 3.35.0); false for Normal,
    /// Compatible, or any unknown name.
    /// </returns>
    public static bool TryFromName(string name, out PdfBlendMode mode)
    {
        switch (name)
        {
            case "Multiply":
                mode = PdfBlendMode.Multiply;
                return true;
            case "Screen":
                mode = PdfBlendMode.Screen;
                return true;
            case "Overlay":
                mode = PdfBlendMode.Overlay;
                return true;
            case "Darken":
                mode = PdfBlendMode.Darken;
                return true;
            case "Lighten":
                mode = PdfBlendMode.Lighten;
                return true;
            case "ColorDodge":
                mode = PdfBlendMode.ColorDodge;
                return true;
            case "ColorBurn":
                mode = PdfBlendMode.ColorBurn;
                return true;
            case "HardLight":
                mode = PdfBlendMode.HardLight;
                return true;
            case "SoftLight":
                mode = PdfBlendMode.SoftLight;
                return true;
            case "Difference":
                mode = PdfBlendMode.Difference;
                return true;
            case "Exclusion":
                mode = PdfBlendMode.Exclusion;
                return true;
            case "Hue":
                mode = PdfBlendMode.Hue;
                return true;
            case "Saturation":
                mode = PdfBlendMode.Saturation;
                return true;
            case "Color":
                mode = PdfBlendMode.Color;
                return true;
            case "Luminosity":
                mode = PdfBlendMode.Luminosity;
                return true;
            default:
                // Normal, Compatible, unknown.
                mode = PdfBlendMode.Normal;
                return false;
        }
    }

    /// <summary>
    /// Applies a separable blend function to a single colour channel, per
    /// PDF §11.3.5 / the W3C compositing model. Operands are in [0, 1].
    /// </summary>
    /// <param name="mode">The blend mode (Normal returns the source unchanged).</param>
    /// <param name="cb">The backdrop channel value, in [0, 1].</param>
    /// <param name="cs">The source channel value, in [0, 1].</param>
    /// <returns>The blended channel value, in [0, 1].</returns>
    public static double Blend(PdfBlendMode mode, double cb, double cs)
    {
        switch (mode)
        {
            case PdfBlendMode.Multiply:
                return cb * cs;
            case PdfBlendMode.Screen:
                return cb + cs - (cb * cs);
            case PdfBlendMode.Overlay:
                return HardLight(cs, cb);
            case PdfBlendMode.Darken:
                return Math.Min(cb, cs);
            case PdfBlendMode.Lighten:
                return Math.Max(cb, cs);
            case PdfBlendMode.ColorDodge:
                if (cb <= 0.0) { return 0.0; }
                if (cs >= 1.0) { return 1.0; }
                return Math.Min(1.0, cb / (1.0 - cs));
            case PdfBlendMode.ColorBurn:
                if (cb >= 1.0) { return 1.0; }
                if (cs <= 0.0) { return 0.0; }
                return 1.0 - Math.Min(1.0, (1.0 - cb) / cs);
            case PdfBlendMode.HardLight:
                return HardLight(cb, cs);
            case PdfBlendMode.SoftLight:
                return SoftLight(cb, cs);
            case PdfBlendMode.Difference:
                return Math.Abs(cb - cs);
            case PdfBlendMode.Exclusion:
                return cb + cs - (2.0 * cb * cs);
            default:
                return cs;
        }
    }

    /// <summary>
    /// Gets whether <paramref name="mode"/> is one of the non-separable modes
    /// (Hue, Saturation, Color, Luminosity), which blend the three channels
    /// together. Since 3.35.0.
    /// </summary>
    /// <param name="mode">The blend mode.</param>
    /// <returns>True for a non-separable mode.</returns>
    public static bool IsNonSeparable(PdfBlendMode mode) =>
        mode is PdfBlendMode.Hue or PdfBlendMode.Saturation or PdfBlendMode.Color or PdfBlendMode.Luminosity;

    /// <summary>
    /// Blends an RGB source colour with an RGB backdrop, per PDF §11.3.5:
    /// channel by channel for the separable modes, and with the
    /// non-separable formulas of §11.3.5.3 for Hue, Saturation, Color and
    /// Luminosity. Components are in [0, 1]. Since 3.35.0.
    /// </summary>
    /// <param name="mode">The blend mode.</param>
    /// <param name="backdrop">The backdrop colour.</param>
    /// <param name="source">The source colour.</param>
    /// <returns>The blended colour.</returns>
    public static (double R, double G, double B) BlendRgb(
        PdfBlendMode mode, (double R, double G, double B) backdrop, (double R, double G, double B) source)
    {
        switch (mode)
        {
            case PdfBlendMode.Hue:
                return SetLum(SetSat(source, Sat(backdrop)), Lum(backdrop));
            case PdfBlendMode.Saturation:
                return SetLum(SetSat(backdrop, Sat(source)), Lum(backdrop));
            case PdfBlendMode.Color:
                return SetLum(source, Lum(backdrop));
            case PdfBlendMode.Luminosity:
                return SetLum(backdrop, Lum(source));
            default:
                return (
                    Blend(mode, backdrop.R, source.R),
                    Blend(mode, backdrop.G, source.G),
                    Blend(mode, backdrop.B, source.B));
        }
    }

    private static double Lum((double R, double G, double B) c) => (0.3 * c.R) + (0.59 * c.G) + (0.11 * c.B);

    private static (double R, double G, double B) SetLum((double R, double G, double B) c, double l)
    {
        double d = l - Lum(c);
        return ClipColor((c.R + d, c.G + d, c.B + d));
    }

    private static (double R, double G, double B) ClipColor((double R, double G, double B) c)
    {
        double l = Lum(c);
        double n = Math.Min(c.R, Math.Min(c.G, c.B));
        double x = Math.Max(c.R, Math.Max(c.G, c.B));
        double r = c.R;
        double g = c.G;
        double b = c.B;
        if (n < 0.0 && l - n > 1e-12)
        {
            r = l + ((r - l) * l / (l - n));
            g = l + ((g - l) * l / (l - n));
            b = l + ((b - l) * l / (l - n));
        }

        if (x > 1.0 && x - l > 1e-12)
        {
            r = l + ((r - l) * (1.0 - l) / (x - l));
            g = l + ((g - l) * (1.0 - l) / (x - l));
            b = l + ((b - l) * (1.0 - l) / (x - l));
        }

        return (Math.Clamp(r, 0.0, 1.0), Math.Clamp(g, 0.0, 1.0), Math.Clamp(b, 0.0, 1.0));
    }

    private static double Sat((double R, double G, double B) c) =>
        Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));

    // SetSat (§11.3.5.3): the largest component becomes s, the smallest 0,
    // and the middle one is scaled between them.
    private static (double R, double G, double B) SetSat((double R, double G, double B) c, double s)
    {
        double[] v = { c.R, c.G, c.B };
        int max = 0;
        int min = 0;
        for (int i = 1; i < 3; i++)
        {
            if (v[i] > v[max])
            {
                max = i;
            }

            if (v[i] < v[min])
            {
                min = i;
            }
        }

        if (max == min)
        {
            return (0.0, 0.0, 0.0);
        }

        int mid = 3 - max - min;
        double[] o = new double[3];
        o[mid] = (v[mid] - v[min]) * s / (v[max] - v[min]);
        o[max] = s;
        o[min] = 0.0;
        return (o[0], o[1], o[2]);
    }

    private static double HardLight(double cb, double cs)
    {
        return cs <= 0.5
            ? 2.0 * cb * cs
            : 1.0 - (2.0 * (1.0 - cb) * (1.0 - cs));
    }

    private static double SoftLight(double cb, double cs)
    {
        if (cs <= 0.5)
        {
            return cb - ((1.0 - (2.0 * cs)) * cb * (1.0 - cb));
        }

        double d = cb <= 0.25
            ? ((((16.0 * cb) - 12.0) * cb) + 4.0) * cb
            : Math.Sqrt(cb);
        return cb + (((2.0 * cs) - 1.0) * (d - cb));
    }
}
