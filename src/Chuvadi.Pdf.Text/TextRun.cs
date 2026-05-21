// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 — Text-showing operators
//        Unicode Bidirectional Algorithm (UAX #9)
// PHASE: v2.0.0 R3 — Text run extraction

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Text;

/// <summary>
/// A contiguous run of text on a PDF page, characterised by its Unicode
/// string, screen-space bounding box, font size, directional flow, and
/// per-glyph positions.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="TextRun"/> typically corresponds to one PDF text-showing
/// operator (<c>Tj</c>, <c>TJ</c>, <c>'</c>, or <c>"</c>) but may be the
/// product of several operators when the builder groups consecutive
/// fragments at the same baseline into a single logical run.
/// </para>
/// <para>
/// The bounding box is in PDF user space — origin bottom-left, units in
/// points (1/72 inch), Y up.
/// </para>
/// </remarks>
public sealed class TextRun
{
    /// <summary>Initialises a <see cref="TextRun"/> with the given fields.</summary>
    /// <param name="unicode">The Unicode text of the run.</param>
    /// <param name="boundingBox">The rectangle covered by the run in PDF user space.</param>
    /// <param name="fontSize">The font size in PDF user-space points.</param>
    /// <param name="direction">The directional flow of the run.</param>
    /// <param name="glyphs">Per-glyph positions, one entry per visible glyph.</param>
    /// <param name="readingOrderIndex">
    /// The zero-based index of this run in the natural reading order of
    /// the page; assigned by <see cref="TextRunBuilder"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="unicode"/> or <paramref name="glyphs"/> is null.
    /// </exception>
    public TextRun(
        string unicode,
        RectangleF boundingBox,
        double fontSize,
        TextDirection direction,
        IReadOnlyList<GlyphPosition> glyphs,
        int readingOrderIndex)
    {
        ArgumentNullException.ThrowIfNull(unicode);
        ArgumentNullException.ThrowIfNull(glyphs);

        Unicode = unicode;
        BoundingBox = boundingBox;
        FontSize = fontSize;
        Direction = direction;
        Glyphs = glyphs;
        ReadingOrderIndex = readingOrderIndex;
    }

    /// <summary>Gets the Unicode text content of the run.</summary>
    public string Unicode { get; }

    /// <summary>Gets the bounding rectangle in PDF user space.</summary>
    public RectangleF BoundingBox { get; }

    /// <summary>Gets the font size in PDF user-space points.</summary>
    public double FontSize { get; }

    /// <summary>Gets the directional flow inferred from the Unicode content.</summary>
    public TextDirection Direction { get; }

    /// <summary>Gets the per-glyph positions for the run.</summary>
    /// <remarks>
    /// In v2.0.0 the positions are estimated from the bounding box and the
    /// font size using a fixed average-advance heuristic; exact per-glyph
    /// advances arrive with font-metric integration in v2.1.
    /// </remarks>
    public IReadOnlyList<GlyphPosition> Glyphs { get; }

    /// <summary>
    /// Gets the zero-based index of this run in the page's natural reading
    /// order, as inferred by <see cref="TextRunBuilder"/>.
    /// </summary>
    public int ReadingOrderIndex { get; }
}
