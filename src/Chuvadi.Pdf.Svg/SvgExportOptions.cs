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
}
