// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.3.6 — Text rendering mode
// PHASE: Phase 2.1 — display-list intermediate

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// PDF text-rendering mode: how glyph outlines are converted to marks on
/// the page (filled, stroked, clipped, or invisible). PDF 32000-1:2008
/// §9.3.6, set by the <c>Tr</c> content-stream operator.
/// </summary>
public enum TextRenderingMode
{
    /// <summary>Fill the glyph outlines (default).</summary>
    Fill = 0,

    /// <summary>Stroke the glyph outlines.</summary>
    Stroke = 1,

    /// <summary>Fill, then stroke the glyph outlines.</summary>
    FillThenStroke = 2,

    /// <summary>Render no visible marks (invisible text — useful for searchable OCR).</summary>
    Invisible = 3,

    /// <summary>Fill the glyph outlines and add them to the clipping path.</summary>
    FillAndClip = 4,

    /// <summary>Stroke the glyph outlines and add them to the clipping path.</summary>
    StrokeAndClip = 5,

    /// <summary>Fill, stroke, then add the outlines to the clipping path.</summary>
    FillThenStrokeAndClip = 6,

    /// <summary>Add the glyph outlines to the clipping path only (no marks).</summary>
    ClipOnly = 7,
}
