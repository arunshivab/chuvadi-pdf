// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Watermark
// Options for text watermark stamping.

using System;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Watermark;

/// <summary>
/// Options for stamping a text watermark onto PDF pages.
/// </summary>
public sealed class TextWatermarkOptions
{
    /// <summary>
    /// Initialises a <see cref="TextWatermarkOptions"/> with required text.
    /// </summary>
    /// <param name="text">The watermark text (e.g., "CONFIDENTIAL").</param>
    public TextWatermarkOptions(string text)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        FontSize = 48.0;
        Color = ColorF.FromGray(0.5f);
        Opacity = 0.3f;
        RotationDegrees = 45.0;
        FontName = "Helvetica";
    }

    /// <summary>Gets or initialises the watermark text.</summary>
    public string Text { get; init; }

    /// <summary>
    /// Gets or initialises the font size in PDF points. Default: 48.
    /// </summary>
    public double FontSize { get; init; }

    /// <summary>
    /// Gets or initialises the text colour. Default: 50% gray.
    /// </summary>
    public ColorF Color { get; init; }

    /// <summary>
    /// Gets or initialises the opacity from 0 (transparent) to 1 (opaque).
    /// Default: 0.3.
    /// </summary>
    public float Opacity { get; init; }

    /// <summary>
    /// Gets or initialises the rotation angle in degrees (counter-clockwise).
    /// Default: 45 (diagonal).
    /// </summary>
    public double RotationDegrees { get; init; }

    /// <summary>
    /// Gets or initialises the font name. When <see cref="FontData"/> is null
    /// this must be one of the 14 standard PDF fonts (the watermark is drawn
    /// with Helvetica metrics). When <see cref="FontData"/> is supplied this is
    /// recorded as the embedded font's PostScript base-font name. Default:
    /// Helvetica.
    /// </summary>
    public string FontName { get; init; }

    /// <summary>
    /// Gets or initialises the bytes of a TrueType/OpenType (glyf-based) font to
    /// embed for this watermark. When null (the default) the watermark uses the
    /// standard Helvetica font. When supplied, the font is embedded as a
    /// composite Type0/CIDFontType2 font with Identity-H encoding, so watermark
    /// text in non-Latin scripts (e.g. Tamil, Devanagari) renders with the
    /// caller's own face. The text is emitted as glyphs in logical order without
    /// complex shaping (no GSUB/GPOS reordering).
    /// </summary>
    public byte[]? FontData { get; init; }

    /// <summary>
    /// Gets or initialises which pages to watermark.
    /// Null means all pages. Otherwise a zero-based page index set.
    /// </summary>
    public int[]? PageIndices { get; init; }
}
