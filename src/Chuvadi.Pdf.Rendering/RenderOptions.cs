// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Rendering
// Rendering options: DPI, scale, background colour.

using System;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering;

/// <summary>
/// Controls how strongly the TrueType bytecode hinting interpreter adjusts
/// glyph outlines before rasterization.
/// </summary>
public enum HintingMode
{
    /// <summary>No hinting: outlines are scaled and rendered as-is.</summary>
    Off = 0,

    /// <summary>
    /// Light hinting: grid-fit the vertical (Y) axis only, leaving horizontal
    /// positions at their naturally scaled values. This keeps baselines and
    /// stem heights crisp without the horizontal stem snapping that can look
    /// heavy under grayscale anti-aliasing. Recommended for anti-aliased
    /// output.
    /// </summary>
    Light = 1,

    /// <summary>
    /// Full classic hinting: execute the complete bytecode interpreter on both
    /// axes. Best for black-and-white or very low-resolution output.
    /// </summary>
    Full = 2,
}

/// <summary>
/// Options that control how a PDF page is rasterized.
/// </summary>
public sealed class RenderOptions
{
    /// <summary>Default options: 150 DPI, opaque white background, light hinting.</summary>
    public static RenderOptions Default { get; } = new RenderOptions();

    /// <summary>Initialises <see cref="RenderOptions"/> with default values.</summary>
    public RenderOptions()
    {
        Dpi = 150;
        Background = ColorF.White;
        FlatnessTolerance = 0.25;
        SuperSample = 1;
        AntiAlias = true;
        GammaCorrect = true;
        Hinting = HintingMode.Light;
        AutohintUnhintedFonts = true;
    }

    /// <summary>
    /// Gets or initialises the output resolution in dots per inch.
    /// Higher values produce larger, sharper images.
    /// Typical values: 72 (screen), 96 (Windows default), 150, 300 (print).
    /// Default: 150.
    /// </summary>
    public double Dpi { get; init; }

    /// <summary>
    /// Gets or initialises the background colour painted before page content.
    /// Default: opaque white.
    /// </summary>
    public ColorF Background { get; init; }

    /// <summary>
    /// Gets or initialises the flatness tolerance for Bezier curve flattening
    /// in device pixels. Smaller = smoother curves, more segments.
    /// Default: 0.25 pixels.
    /// </summary>
    public double FlatnessTolerance { get; init; }

    /// <summary>
    /// Computes the pixel dimensions for a page of the given PDF point size
    /// at this option's DPI.
    /// </summary>
    public (int Width, int Height) PixelSize(double pageWidthPt, double pageHeightPt)
    {
        if (pageWidthPt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageWidthPt), "Page width must be positive.");
        }

        if (pageHeightPt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageHeightPt), "Page height must be positive.");
        }

        int w = Math.Max(1, (int)Math.Round(pageWidthPt * Dpi / 72.0));
        int h = Math.Max(1, (int)Math.Round(pageHeightPt * Dpi / 72.0));
        return (w, h);
    }

    /// <summary>
    /// Gets or initialises the supersampling factor for anti-aliasing.
    /// The page is rendered at this multiple of the target resolution and
    /// box-filtered down, smoothing glyph and path edges. 1 disables
    /// supersampling (pixel-identical to the single-sample rasterizer).
    /// Typical quality value: 3 or 4. Default: 1.
    /// </summary>
    public int SuperSample { get; init; }

    /// <summary>
    /// Gets or initialises whether the scanline fill computes fractional
    /// pixel coverage (anti-aliasing). When false, fills are binary
    /// (pixel-identical to the original rasterizer). Default: true.
    /// </summary>
    public bool AntiAlias { get; init; }

    /// <summary>
    /// Gets or initialises whether anti-aliased fills blend colour channels
    /// in linear light (gamma-correct). When false, channels are blended
    /// directly in sRGB space (the legacy behaviour, which renders edges
    /// slightly lighter). Has no effect when <see cref="AntiAlias"/> is false.
    /// Default: true.
    /// </summary>
    public bool GammaCorrect { get; init; }

    /// <summary>
    /// Gets or initialises the glyph hinting mode. Default: <see cref="HintingMode.Light"/>.
    /// </summary>
    public HintingMode Hinting { get; init; }

    /// <summary>
    /// Gets or initialises whether fonts that carry no hinting programs are
    /// grid-fitted by the geometric autohinter (Y axis only) when hinting is
    /// enabled. Fonts with their own bytecode are unaffected. Default: true.
    /// </summary>
    public bool AutohintUnhintedFonts { get; init; }

    /// <summary>
    /// Computes the scale factor from PDF points to device pixels for this DPI.
    /// </summary>
    public double Scale => Dpi / 72.0;
}
