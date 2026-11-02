// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.29 — reading order: tagged order, bands and columns (D-104)

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Documents;

namespace Chuvadi.Pdf.Text;

/// <summary>Where a page's reading order comes from.</summary>
public enum ReadingOrderSource
{
    /// <summary>Lines from the top of the page to the bottom, each read along its baseline (as in 3.28).</summary>
    Rows,

    /// <summary>
    /// Bands from the top of the page to the bottom; inside a band with more
    /// than one column, each column is read before the next.
    /// </summary>
    Columns,

    /// <summary>The order of the document's structure tree (a Tagged PDF).</summary>
    Tagged,
}

/// <summary>
/// The reading order of one page for layout-aware text extraction: where it
/// comes from and, for <see cref="ReadingOrderSource.Columns"/>, the bands and
/// columns in the order they are read.
/// </summary>
/// <remarks>
/// <see cref="TextExtractor.AnalyzeLayout(PdfPage)"/> returns what extraction
/// would use, so an application can show it; an application can also build
/// its own and pass it to <see cref="TextExtractor.ExtractText(PdfPage, PageLayout)"/>
/// to override it. Rectangles are in PDF user space (origin bottom-left).
/// </remarks>
public sealed class PageLayout
{
    /// <summary>
    /// Initialises a page layout.
    /// </summary>
    /// <param name="source">Where the reading order comes from.</param>
    /// <param name="bands">
    /// The bands in reading order; used only when <paramref name="source"/> is
    /// <see cref="ReadingOrderSource.Columns"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="bands"/> is null.</exception>
    public PageLayout(ReadingOrderSource source, IReadOnlyList<LayoutBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);
        Source = source;
        Bands = bands;
    }

    /// <summary>Gets a layout that reads plain rows, top to bottom (3.28 order).</summary>
    public static PageLayout Rows { get; } = new PageLayout(ReadingOrderSource.Rows, Array.Empty<LayoutBand>());

    /// <summary>Gets a layout that uses the structure tree's order when the page has one.</summary>
    public static PageLayout Tagged { get; } = new PageLayout(ReadingOrderSource.Tagged, Array.Empty<LayoutBand>());

    /// <summary>Gets where the reading order comes from.</summary>
    public ReadingOrderSource Source { get; }

    /// <summary>
    /// Gets the bands in reading order (empty unless <see cref="Source"/> is
    /// <see cref="ReadingOrderSource.Columns"/>). Text outside every band is
    /// read as rows, placed by its height among the bands.
    /// </summary>
    public IReadOnlyList<LayoutBand> Bands { get; }
}

/// <summary>
/// A horizontal band of a page: a region read as a whole before the next
/// band, holding one column (read as rows) or several (read one after another).
/// </summary>
public sealed class LayoutBand
{
    /// <summary>
    /// Initialises a band.
    /// </summary>
    /// <param name="bounds">The band's rectangle in user space.</param>
    /// <param name="columns">The band's columns in reading order (one or more).</param>
    /// <exception cref="ArgumentNullException"><paramref name="columns"/> is null.</exception>
    public LayoutBand(PdfRectangle bounds, IReadOnlyList<LayoutColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        Bounds = bounds;
        Columns = columns;
    }

    /// <summary>Gets the band's rectangle in user space.</summary>
    public PdfRectangle Bounds { get; }

    /// <summary>Gets the band's columns in reading order.</summary>
    public IReadOnlyList<LayoutColumn> Columns { get; }
}

/// <summary>A column of a <see cref="LayoutBand"/>, read top to bottom.</summary>
public sealed class LayoutColumn
{
    /// <summary>
    /// Initialises a column.
    /// </summary>
    /// <param name="bounds">The column's rectangle in user space.</param>
    public LayoutColumn(PdfRectangle bounds)
    {
        Bounds = bounds;
    }

    /// <summary>Gets the column's rectangle in user space.</summary>
    public PdfRectangle Bounds { get; }
}
