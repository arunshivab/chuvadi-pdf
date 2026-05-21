// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R3 — Search

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Text;

/// <summary>
/// A single match returned by
/// <see cref="PdfDocumentTextExtensions.SearchAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// The page text used for matching is the concatenation of each
/// <see cref="TextRun"/>'s <see cref="TextRun.Unicode"/> string with a
/// single ASCII space inserted between consecutive runs whose baselines
/// differ. <see cref="CharacterOffset"/> indexes into that concatenated
/// page text, not into any individual run.
/// </para>
/// <para>
/// <see cref="BoundingBoxes"/> contains one entry per <see cref="TextRun"/>
/// the match overlaps; for in-line matches this is usually a single
/// rectangle, but matches that span a line break or column boundary
/// produce one rectangle per traversed run.
/// </para>
/// </remarks>
public sealed class SearchMatch
{
    /// <summary>Initialises a <see cref="SearchMatch"/>.</summary>
    /// <param name="pageNumber">One-based page number containing the match.</param>
    /// <param name="characterOffset">Zero-based character offset into the page text.</param>
    /// <param name="length">Length of the matched substring in UTF-16 code units.</param>
    /// <param name="boundingBoxes">Bounding rectangles of every traversed run.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="boundingBoxes"/> is null.
    /// </exception>
    public SearchMatch(
        int pageNumber,
        int characterOffset,
        int length,
        IReadOnlyList<RectangleF> boundingBoxes)
    {
        ArgumentNullException.ThrowIfNull(boundingBoxes);

        PageNumber = pageNumber;
        CharacterOffset = characterOffset;
        Length = length;
        BoundingBoxes = boundingBoxes;
    }

    /// <summary>Gets the one-based page number containing the match.</summary>
    public int PageNumber { get; }

    /// <summary>
    /// Gets the zero-based character offset of the match within the
    /// concatenated page text.
    /// </summary>
    public int CharacterOffset { get; }

    /// <summary>Gets the length of the matched substring in UTF-16 code units.</summary>
    public int Length { get; }

    /// <summary>
    /// Gets the bounding rectangles of every <see cref="TextRun"/> the
    /// match overlaps, in reading order. Coordinates are in PDF user
    /// space (Y up, points).
    /// </summary>
    public IReadOnlyList<RectangleF> BoundingBoxes { get; }
}
