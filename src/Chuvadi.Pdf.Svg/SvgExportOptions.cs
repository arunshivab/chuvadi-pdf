// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.0 — SVG export

using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Svg;

/// <summary>How text is rendered to SVG.</summary>
public enum SvgTextStrategy
{
    /// <summary>
    /// Emit real <c>&lt;text&gt;</c> elements. Text is selectable, searchable,
    /// and accessible. Default.
    /// </summary>
    Selectable = 0,

    /// <summary>
    /// Emit one positioned glyph per character. Pixel-faithful but text is
    /// not selectable as a unit.
    /// </summary>
    PerGlyph = 1,
}

/// <summary>
/// What happens when a page's vector-operation count exceeds
/// <see cref="SvgExportOptions.MaxVectorOps"/>. Dense vector pages (CAD
/// exports, large plots) can otherwise produce SVGs with tens of thousands of
/// elements that overwhelm viewers.
/// </summary>
public enum SvgDensityFallback
{
    /// <summary>
    /// Stay vector but force path simplification: straight-segment runs are
    /// simplified (Ramer–Douglas–Peucker) at
    /// <see cref="SvgExportOptions.PathSimplifyTolerance"/> or, when that is
    /// unset, an automatic 0.3-point tolerance. Reduces output size while
    /// keeping text selectable. Default.
    /// </summary>
    Simplify = 0,

    /// <summary>
    /// Rasterize the whole page at <see cref="SvgExportOptions.RasterFallbackDpi"/>
    /// and emit a single <c>&lt;image&gt;</c> SVG instead of vector elements.
    /// Bounded output size for any density; text is no longer selectable.
    /// Only the document-based entry points (<c>RenderPage</c>) can rasterize —
    /// rendering a pre-built display list falls back to
    /// <see cref="Simplify"/>.
    /// </summary>
    Rasterize = 1,
}

/// <summary>How embedded fonts are handled.</summary>
public enum SvgFontStrategy
{
    /// <summary>
    /// Embed via <c>@font-face</c> blocks. TrueType, OpenType, CFF supported.
    /// Type 1 falls back to a CSS family. Default.
    /// </summary>
    EmbedAsWebFont = 0,

    /// <summary>Skip embedding; rely on CSS family fallbacks.</summary>
    CssFallbackOnly = 1,
}

/// <summary>Options for PDF → SVG export.</summary>
public sealed class SvgExportOptions
{
    /// <summary>Embed images as base64 data URLs (default: true).</summary>
    public bool InlineImages { get; init; } = true;

    /// <summary>Text rendering strategy. Defaults to <see cref="SvgTextStrategy.Selectable"/>.</summary>
    public SvgTextStrategy TextStrategy { get; init; } = SvgTextStrategy.Selectable;

    /// <summary>Font embedding strategy. Defaults to <see cref="SvgFontStrategy.EmbedAsWebFont"/>.</summary>
    public SvgFontStrategy FontStrategy { get; init; } = SvgFontStrategy.EmbedAsWebFont;

    /// <summary>Number of decimal places for emitted coordinates. Default 4.</summary>
    public int Precision { get; init; } = 4;

    /// <summary>Indent the SVG output (default: false, compact).</summary>
    public bool PrettyPrint { get; init; }

    /// <summary>
    /// Opaque colour for the page sheet, emitted as a full-page background
    /// rectangle behind all content. Defaults to <see cref="ColorF.White"/>,
    /// matching the rasteriser's default white paper. Set to <see langword="null"/>
    /// for a transparent SVG (useful when compositing over another background).
    /// </summary>
    public ColorF? Background { get; init; } = ColorF.White;

    /// <summary>
    /// Tolerance (page points) for simplifying straight-segment runs in path
    /// geometry (Ramer–Douglas–Peucker) before emission. <c>0</c> (default)
    /// disables simplification. Curves and clip paths are never simplified.
    /// Typical values: 0.1–0.5 for CAD-style line work.
    /// </summary>
    public double PathSimplifyTolerance { get; init; }

    /// <summary>
    /// Budget on the number of vector painting operations (paths and shadings)
    /// per page before <see cref="DensityFallback"/> kicks in. <c>0</c>
    /// (default) means unlimited — every page renders fully vector.
    /// </summary>
    public int MaxVectorOps { get; init; }

    /// <summary>
    /// What happens when a page exceeds <see cref="MaxVectorOps"/>. Defaults
    /// to <see cref="SvgDensityFallback.Simplify"/>.
    /// </summary>
    public SvgDensityFallback DensityFallback { get; init; } = SvgDensityFallback.Simplify;

    /// <summary>
    /// Resolution used when <see cref="DensityFallback"/> is
    /// <see cref="SvgDensityFallback.Rasterize"/> and the budget is exceeded.
    /// Default: 150 DPI.
    /// </summary>
    public double RasterFallbackDpi { get; init; } = 150;
}
