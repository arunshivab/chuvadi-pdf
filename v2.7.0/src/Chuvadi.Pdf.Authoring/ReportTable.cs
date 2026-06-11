// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8 — Graphics (table rendering primitives)
// PHASE: Phase 2.7 — Report layout
// The report table model: columns, rows, cells (with spans), and table styles.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Images;

namespace Chuvadi.Pdf.Authoring;

/// <summary>How a column's width is specified.</summary>
public enum ColumnWidthMode
{
    /// <summary>Width is a fraction (0..1] of the table width.</summary>
    Fraction = 0,

    /// <summary>Width is an absolute number of points.</summary>
    Points = 1,

    /// <summary>Width is an equal share of the space left after fixed and fractional columns.</summary>
    Auto = 2,
}

/// <summary>How cell text that exceeds the cell width is handled.</summary>
public enum CellOverflow
{
    /// <summary>Wrap onto additional lines; the row grows to fit (auto-height rows).</summary>
    Wrap = 0,

    /// <summary>Cut the text at the cell edge.</summary>
    Truncate = 1,

    /// <summary>Cut the text and append an ellipsis.</summary>
    Ellipsis = 2,
}

/// <summary>Which grid lines a table draws.</summary>
public enum TableBorderMode
{
    /// <summary>No lines at all.</summary>
    None = 0,

    /// <summary>The full grid: outline plus every interior row and column line.</summary>
    Grid = 1,

    /// <summary>The outer rectangle only.</summary>
    Outline = 2,

    /// <summary>Horizontal lines only (row separators plus top and bottom edges).</summary>
    HorizontalOnly = 3,

    /// <summary>A single line under the header row only.</summary>
    HeaderUnderlineOnly = 4,
}

/// <summary>A table column: optional header text, width, and default cell styling.</summary>
public sealed class ReportColumn
{
    /// <summary>Gets or initialises the header label drawn in the header row. Default: empty.</summary>
    public string Header { get; init; } = string.Empty;

    /// <summary>Gets or initialises how <see cref="Width"/> is interpreted. Default: Auto.</summary>
    public ColumnWidthMode WidthMode { get; init; } = ColumnWidthMode.Auto;

    /// <summary>
    /// Gets or initialises the width value: a fraction of the table width under
    /// <see cref="ColumnWidthMode.Fraction"/>, points under
    /// <see cref="ColumnWidthMode.Points"/>, ignored under
    /// <see cref="ColumnWidthMode.Auto"/>.
    /// </summary>
    public double Width { get; init; }

    /// <summary>Gets or initialises the default horizontal alignment of the column's cells. Default: left.</summary>
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;

    /// <summary>Gets or initialises the default overflow behaviour of the column's cells. Default: wrap.</summary>
    public CellOverflow Overflow { get; init; } = CellOverflow.Wrap;
}

/// <summary>
/// One table cell: text or an image, an optional column/row span, and
/// optional per-cell style overrides.
/// </summary>
public sealed class ReportCell
{
    /// <summary>Creates an empty cell.</summary>
    public ReportCell()
    {
    }

    /// <summary>Creates a text cell.</summary>
    public ReportCell(string text)
    {
        Text = text ?? string.Empty;
    }

    /// <summary>Gets or initialises the cell text. Default: empty.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initialises an image drawn inside the cell instead of text
    /// (scaled to the cell's inner width, preserving aspect ratio).
    /// Accepts JPEG, PNG, TIFF, or BMP bytes.
    /// </summary>
    public byte[]? ImageBytes { get; init; }

    /// <summary>Gets or initialises a decoded image frame drawn inside the cell instead of text.</summary>
    public ImageFrame? ImageFrame { get; init; }

    /// <summary>Gets or initialises how many columns the cell spans. Default: 1.</summary>
    public int ColSpan { get; init; } = 1;

    /// <summary>
    /// Gets or initialises how many rows the cell spans. Default: 1. Rows tied
    /// together by a span paginate as a unit and are kept on the same page.
    /// </summary>
    public int RowSpan { get; init; } = 1;

    /// <summary>Gets or initialises a font override; null inherits the table font.</summary>
    public ReportFont? Font { get; init; }

    /// <summary>Gets or initialises a font-size override; null inherits the table font size.</summary>
    public double? FontSize { get; init; }

    /// <summary>Gets or initialises a text-colour override; null inherits the table text colour.</summary>
    public Color? TextColor { get; init; }

    /// <summary>Gets or initialises a background fill; null means no fill (row/alternating fills show through).</summary>
    public Color? Background { get; init; }

    /// <summary>Gets or initialises a horizontal-alignment override; null inherits the column alignment.</summary>
    public TextAlignment? Alignment { get; init; }

    /// <summary>Gets or initialises the vertical alignment inside the cell. Default: top.</summary>
    public VerticalAlignment VerticalAlignment { get; init; } = VerticalAlignment.Top;

    /// <summary>Gets or initialises an overflow override; null inherits the column overflow.</summary>
    public CellOverflow? Overflow { get; init; }
}

/// <summary>One table row: its cells plus optional height and background overrides.</summary>
public sealed class ReportRow
{
    /// <summary>Creates an empty row.</summary>
    public ReportRow()
    {
    }

    /// <summary>Creates a row of plain text cells.</summary>
    public ReportRow(params string[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        foreach (string cell in cells)
        {
            Cells.Add(new ReportCell(cell));
        }
    }

    /// <summary>Gets the row's cells, left to right. Spanned-over grid positions are skipped, HTML-style.</summary>
    public List<ReportCell> Cells { get; } = new();

    /// <summary>Gets or initialises a fixed row height in points; 0 (the default) sizes the row to its content.</summary>
    public double Height { get; init; }

    /// <summary>Gets or initialises a background fill for the whole row; null lets table-level fills apply.</summary>
    public Color? Background { get; init; }
}

/// <summary>Table-level styling: fonts, borders, padding, header and fills.</summary>
public sealed class TableStyle
{
    /// <summary>Default style: 10-point Helvetica, full grid in light gray, bold gray header.</summary>
    public static TableStyle Default { get; } = new TableStyle();

    /// <summary>Gets or initialises the body font. Default: regular Helvetica.</summary>
    public ReportFont Font { get; init; } = ReportFont.Helvetica;

    /// <summary>Gets or initialises the body font size in points. Default: 10.</summary>
    public double FontSize { get; init; } = 10;

    /// <summary>Gets or initialises the body text colour. Default: black.</summary>
    public Color TextColor { get; init; } = Colors.Black;

    /// <summary>Gets or initialises whether the header row is drawn at all. Default: true.</summary>
    public bool ShowHeader { get; init; } = true;

    /// <summary>Gets or initialises whether the header row repeats at the top of every continuation page. Default: true.</summary>
    public bool RepeatHeaderOnEveryPage { get; init; } = true;

    /// <summary>Gets or initialises the header font. Default: bold Helvetica.</summary>
    public ReportFont HeaderFont { get; init; } = ReportFont.HelveticaBold;

    /// <summary>Gets or initialises the header font size in points; 0 (the default) inherits <see cref="FontSize"/>.</summary>
    public double HeaderFontSize { get; init; }

    /// <summary>Gets or initialises the header text colour. Default: black.</summary>
    public Color HeaderTextColor { get; init; } = Colors.Black;

    /// <summary>Gets or initialises the header background fill. Default: light gray.</summary>
    public Color? HeaderBackground { get; init; } = Colors.LightGray;

    /// <summary>Gets or initialises which grid lines are drawn. Default: full grid.</summary>
    public TableBorderMode BorderMode { get; init; } = TableBorderMode.Grid;

    /// <summary>Gets or initialises the grid line colour. Default: mid gray.</summary>
    public Color BorderColor { get; init; } = Colors.Gray;

    /// <summary>Gets or initialises the grid line width in points. Default: 0.5.</summary>
    public double BorderWidth { get; init; } = 0.5;

    /// <summary>Gets or initialises the padding, in points, inside every cell. Default: 4.</summary>
    public double CellPadding { get; init; } = 4;

    /// <summary>
    /// Gets or initialises an alternating fill applied to every second body row
    /// (the 2nd, 4th, …). Null (the default) disables row banding.
    /// </summary>
    public Color? AlternatingRowBackground { get; init; }

    /// <summary>Gets or initialises the line spacing of wrapped cell text as a multiple of the font size. Default: 1.2.</summary>
    public double LineSpacing { get; init; } = 1.2;

    /// <summary>Gets or initialises the vertical space, in points, after the table. Default: 8.</summary>
    public double SpaceAfter { get; init; } = 8;
}

/// <summary>
/// A report table: columns, rows, and a style. Add to a report with
/// <see cref="ReportBuilder.AddTable(ReportTable)"/>; long tables paginate
/// automatically with the header repeated per page (when enabled).
/// </summary>
public sealed class ReportTable
{
    /// <summary>Gets the column definitions, left to right.</summary>
    public List<ReportColumn> Columns { get; } = new();

    /// <summary>Gets the body rows, top to bottom.</summary>
    public List<ReportRow> Rows { get; } = new();

    /// <summary>Gets or initialises the table style. Default: <see cref="TableStyle.Default"/>.</summary>
    public TableStyle Style { get; init; } = TableStyle.Default;

    /// <summary>Adds a column and returns the table for chaining.</summary>
    public ReportTable AddColumn(ReportColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        Columns.Add(column);
        return this;
    }

    /// <summary>Adds an auto-width column with the given header and returns the table for chaining.</summary>
    public ReportTable AddColumn(string header)
    {
        ArgumentNullException.ThrowIfNull(header);
        Columns.Add(new ReportColumn
        {
            Header = header,
        });
        return this;
    }

    /// <summary>Adds a row of plain text cells and returns the table for chaining.</summary>
    public ReportTable AddRow(params string[] cells)
    {
        Rows.Add(new ReportRow(cells));
        return this;
    }

    /// <summary>Adds a row and returns the table for chaining.</summary>
    public ReportTable AddRow(ReportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Rows.Add(row);
        return this;
    }
}
