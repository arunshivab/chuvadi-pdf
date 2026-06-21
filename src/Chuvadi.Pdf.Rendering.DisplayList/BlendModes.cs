// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.3.5 — Blend modes
// PHASE: Phase 2 — item 12, ExtGState blend modes
//
// Maps PDF /BM names to the supported separable blend modes. The non-separable
// modes (Hue, Saturation, Color, Luminosity) and Compatible are treated as
// Normal (source-over), which is a conservative, spec-permitted fallback.

using System;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Helpers for the PDF blend-mode (/BM) names.</summary>
public static class BlendModes
{
    /// <summary>
    /// Maps a PDF blend-mode name to a <see cref="PdfBlendMode"/>, returning
    /// <see cref="PdfBlendMode.Normal"/> for unknown or non-separable names.
    /// </summary>
    /// <param name="name">The blend-mode name, without the leading slash.</param>
    /// <returns>The mapped blend mode.</returns>
    public static PdfBlendMode FromName(string name)
    {
        return TryFromName(name, out PdfBlendMode mode) ? mode : PdfBlendMode.Normal;
    }

    /// <summary>
    /// Attempts to map a PDF blend-mode name to a supported separable
    /// <see cref="PdfBlendMode"/>.
    /// </summary>
    /// <param name="name">The blend-mode name, without the leading slash.</param>
    /// <param name="mode">The mapped blend mode when supported.</param>
    /// <returns>
    /// True when <paramref name="name"/> is a supported separable mode; false
    /// for Normal, Compatible, the non-separable modes, or any unknown name.
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
            default:
                // Normal, Compatible, Hue, Saturation, Color, Luminosity, unknown.
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
