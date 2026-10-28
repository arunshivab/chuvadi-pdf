// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4.2 (text matrix), §9.3.6 (text rendering mode)

using System;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Text-matrix and render-mode options for a shaped glyph run: character
/// scaling, a synthetic italic slant and a synthetic bold stroke.
/// </summary>
public sealed class GlyphRunStyle
{
    /// <summary>
    /// Gets the horizontal scale (1 = 100%). Glyphs are drawn this much wider
    /// or narrower, and the run's advances and x offsets are multiplied by it
    /// too (as Word's character scaling and PDF's <c>Tz</c> do), so pass the
    /// shaper's unscaled advances. Must be positive. Default 1.
    /// </summary>
    public double HorizontalScale { get; init; } = 1.0;

    /// <summary>
    /// Gets the slant: the tangent of the lean angle (the text matrix's
    /// <c>c</c>). Positive leans right; 0.2 (about 11°) is a usual synthetic
    /// italic. Default 0.
    /// </summary>
    public double Skew { get; init; }

    /// <summary>
    /// Gets the outline stroke width in points for synthetic bold. When
    /// positive, glyphs are filled and then stroked in the fill colour (render
    /// mode 2). Default 0 (fill only).
    /// </summary>
    public double StrokeWidth { get; init; }

    // Throws when the style cannot be written as valid PDF.
    internal void Validate()
    {
        if (!double.IsFinite(HorizontalScale) || HorizontalScale <= 0)
        {
            throw new ArgumentException("GlyphRunStyle.HorizontalScale must be a finite, positive number.");
        }

        if (!double.IsFinite(Skew))
        {
            throw new ArgumentException("GlyphRunStyle.Skew must be finite.");
        }

        if (!double.IsFinite(StrokeWidth) || StrokeWidth < 0)
        {
            throw new ArgumentException("GlyphRunStyle.StrokeWidth must be a finite, non-negative number.");
        }
    }
}
