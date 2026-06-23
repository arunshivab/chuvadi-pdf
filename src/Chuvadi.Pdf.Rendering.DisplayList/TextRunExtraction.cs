// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 (text objects)
//
// Flat text-run view of a page's text content. The companion to
// ExtractLineSegments: where that surfaces vector geometry, this surfaces the
// page's text as reading-order runs, each carrying its page-space bounding box,
// per-glyph positions, font presentation, and optional-content layers. Domain
// semantics (which run is a room label, which is a dimension value) stay in the
// consumer; this is generic.

using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Extension surface for extracting text runs from a <see cref="PageDisplayList"/>.
/// </summary>
public static class TextRunExtraction
{
    /// <summary>
    /// Extracts the page's text as a reading-order sequence of
    /// <see cref="TextRun"/>s. Each run carries its page-space
    /// <see cref="TextRun.BoundingBox"/>, per-glyph positions, resolved font
    /// presentation, and the optional-content <see cref="TextRun.Layers"/> it
    /// belongs to. Symmetric with
    /// <see cref="LineSegmentExtraction.ExtractLineSegments"/>.
    /// </summary>
    /// <param name="list">The page display list to read.</param>
    /// <returns>The text runs in reading order. Never null.</returns>
    public static IReadOnlyList<TextRun> ExtractTextRuns(this PageDisplayList list)
    {
        return TextRunExtractor.Extract(list);
    }
}
