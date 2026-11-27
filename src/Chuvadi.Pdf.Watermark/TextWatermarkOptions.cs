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
    /// Gets or initialises the font name. Default: Helvetica.
    /// </summary>
    /// <remarks>
    /// Since 3.36.0 (D-412) the watermark is shaped and subset-embedded through
    /// the shared mark-text composer, so this is no longer used to select a
    /// standard font or restrict the text to a standard-font repertoire. It is
    /// retained for source compatibility.
    /// </remarks>
    public string FontName { get; init; }

    /// <summary>
    /// Gets or initialises the bytes of a TrueType/OpenType font to use as the
    /// primary face for this watermark. When null (the default) the LiPi Sans
    /// face for each script is used. When supplied, it is the primary face for
    /// every code point it covers (the LiPi face covers the rest).
    /// </summary>
    /// <remarks>
    /// Since 3.36.0 (D-412) the text is shaped (GSUB/GPOS) and subset-embedded as
    /// a composite Type0/Identity-H font, with a monochrome Noto Emoji fallback
    /// for code points the primary face lacks; the watermark is always embedded.
    /// </remarks>
    public byte[]? FontData { get; init; }

    /// <summary>
    /// Gets or initialises which pages to watermark.
    /// Null means all pages. Otherwise a zero-based page index set.
    /// </summary>
    public int[]? PageIndices { get; init; }

    /// <summary>
    /// Gets or initialises whether the watermark is centred on the page as a
    /// viewer shows it (3.34.0, D-335): on the shown box (the CropBox
    /// intersected with the MediaBox), with <see cref="RotationDegrees"/>
    /// measured from the upright view after the page's <c>/Rotate</c>.
    /// Default: false, the historical placement on the unrotated MediaBox.
    /// </summary>
    public bool PlaceInDisplaySpace { get; init; }
}
