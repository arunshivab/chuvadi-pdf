// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — public text-run API

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Documents;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Extensions on <see cref="PdfDocument"/> and <see cref="PdfPage"/> for the
/// display-list and text-run APIs.
/// </summary>
public static class PdfPageExtensions
{
    /// <summary>
    /// Builds a <see cref="PageDisplayList"/> for the given page index.
    /// </summary>
    public static PageDisplayList BuildDisplayList(this PdfDocument document, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        return DisplayListBuilder.Build(document, pageIndex);
    }

    /// <summary>
    /// Returns whether page <paramref name="pageIndex"/> paints anything
    /// visible (text, paths, images or shadings, including form XObjects and
    /// annotation appearances). See <see cref="PageContentInventory"/>.
    /// </summary>
    public static bool HasDrawableContent(this PdfDocument document, int pageIndex)
    {
        return PageContentInventory.Inspect(document, pageIndex).HasDrawableContent;
    }

    /// <summary>
    /// Returns what page <paramref name="pageIndex"/> paints, by category.
    /// See <see cref="PageContentInventory"/>.
    /// </summary>
    public static PageContentInventory GetContentInventory(this PdfDocument document, int pageIndex)
    {
        return PageContentInventory.Inspect(document, pageIndex);
    }

    /// <summary>
    /// Returns the text runs of <paramref name="pageIndex"/> in reading order.
    /// </summary>
    /// <remarks>
    /// Glyph-level positions are derived from font-metric data (PDF /Widths
    /// or /W tables); selection-overlay consumers can use these for native
    /// browser text selection.
    /// </remarks>
    public static IReadOnlyList<TextRun> GetTextRuns(this PdfDocument document, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        PageDisplayList list = DisplayListBuilder.Build(document, pageIndex);
        return TextRunExtractor.Extract(list);
    }

    /// <summary>
    /// Searches the document for occurrences of <paramref name="query"/>,
    /// streaming matches as they are found.
    /// </summary>
    public static System.Collections.Generic.IAsyncEnumerable<SearchMatch> SearchAsync(
        this PdfDocument document,
        string query,
        SearchOptions? options = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return DocumentSearch.SearchAsync(document, query, options, cancellationToken);
    }
}
