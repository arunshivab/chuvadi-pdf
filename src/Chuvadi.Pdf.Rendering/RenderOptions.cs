// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Rendering
// Rendering options: DPI, scale, background colour.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Documents;
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
    /// Gets or initialises whether the anti-aliased edge coverage of opaque
    /// fills blends in linear light (gamma-correct). When false, edges blend
    /// directly in sRGB space (the legacy behaviour, which renders edges
    /// slightly lighter). Transparency (/ca, /CA, soft masks, image alpha)
    /// always composites in sRGB, the blending colour space Acrobat uses,
    /// so 50% black over white is 128. Has no effect when
    /// <see cref="AntiAlias"/> is false. Default: true.
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

    /// <summary>
    /// Gets or initialises a clockwise view rotation applied to the rendered
    /// page, in degrees: 0 (default), 90, 180 or 270. The output buffer takes
    /// the rotated size (width and height swapped for 90 and 270). Values are
    /// normalised modulo 360, so -90 means 270.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the value is not a multiple of 90.
    /// </exception>
    public int Rotation
    {
        get => _rotation;
        init => _rotation = NormalizeRotation(value, nameof(Rotation));
    }

    /// <summary>
    /// Gets or initialises whether the page's own <c>/Rotate</c> entry is
    /// applied, so the page renders upright the way PDF viewers display it.
    /// It adds to <see cref="Rotation"/>. Default: false (the page renders in
    /// its unrotated user space, as in earlier releases).
    /// </summary>
    public bool ApplyPageRotation { get; init; }

    /// <summary>
    /// Gets or initialises whether optional content (layers) is ignored, so
    /// every layer draws whatever its state. Default: false — layers hidden in
    /// the document's default configuration are not drawn, as in Acrobat.
    /// </summary>
    public bool IgnoreOptionalContent { get; init; }

    /// <summary>
    /// Gets or initialises per-layer visibility overrides (layer name →
    /// visible), applied on top of the document's default configuration —
    /// for example to drive a layers panel. Ignored when
    /// <see cref="IgnoreOptionalContent"/> is true. Default: null (no overrides).
    /// </summary>
    public IReadOnlyDictionary<string, bool>? LayerVisibility { get; init; }

    // The layer visibility these options select for a page.
    internal OptionalContentVisibility VisibilityFor(PdfPage page) =>
        IgnoreOptionalContent
            ? OptionalContentVisibility.AllVisible
            : page.OptionalContent.WithOverrides(LayerVisibility);

    internal static int NormalizeRotation(int value, string name)
    {
        if (value % 90 != 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Rotation must be a multiple of 90 degrees.");
        }

        return ((value % 360) + 360) % 360;
    }

    private readonly int _rotation;
}
