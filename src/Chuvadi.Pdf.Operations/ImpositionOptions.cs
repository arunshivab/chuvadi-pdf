// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10 (form XObjects); §8.4.4 (clipping). LA-11.

using Chuvadi.Pdf.Authoring;

namespace Chuvadi.Pdf.Operations;

/// <summary>The order in which source pages fill the cells of an N-up sheet.</summary>
public enum NUpOrder
{
    /// <summary>Fill left-to-right, then top-to-bottom.</summary>
    RowMajor = 0,

    /// <summary>Fill top-to-bottom, then left-to-right.</summary>
    ColumnMajor = 1,
}

/// <summary>
/// Options for <see cref="Imposition.NUp(System.IO.Stream, Chuvadi.Pdf.Documents.PdfDocument, NUpOptions)"/>:
/// how many source pages to place per sheet, the sheet size, and the spacing
/// around and between cells.
/// </summary>
public sealed class NUpOptions
{
    /// <summary>The number of cell rows per sheet. Default 1.</summary>
    public int Rows { get; init; } = 1;

    /// <summary>The number of cell columns per sheet. Default 2.</summary>
    public int Columns { get; init; } = 2;

    /// <summary>The output sheet size. Default <see cref="PageSize.A4"/>.</summary>
    public PageSize SheetSize { get; init; } = PageSize.A4;

    /// <summary>The margin, in points, around the grid of cells. Default 0.</summary>
    public double Margin { get; init; }

    /// <summary>The gutter, in points, between adjacent cells. Default 0.</summary>
    public double Gutter { get; init; }

    /// <summary>The order in which source pages fill cells. Default <see cref="NUpOrder.RowMajor"/>.</summary>
    public NUpOrder Order { get; init; } = NUpOrder.RowMajor;
}

/// <summary>
/// Options for <see cref="Imposition.Booklet(System.IO.Stream, Chuvadi.Pdf.Documents.PdfDocument, BookletOptions)"/>:
/// the size of each source-page slot (the output sheet is twice this width) and
/// the margin around each slot.
/// </summary>
public sealed class BookletOptions
{
    /// <summary>The size of each page slot. The sheet is twice this width. Default <see cref="PageSize.A4"/>.</summary>
    public PageSize PageSize { get; init; } = PageSize.A4;

    /// <summary>The margin, in points, around each page slot. Default 0.</summary>
    public double Margin { get; init; }
}
