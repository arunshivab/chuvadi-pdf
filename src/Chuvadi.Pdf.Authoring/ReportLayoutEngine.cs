// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8 — Graphics, §9 — Text (layout over the
//        authoring primitives)
// PHASE: Phase 2.7 — Report layout
// The layout engine behind ReportBuilder: walks content blocks, paginates,
// wraps and justifies text, and lays out span-aware tables.

using System;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Lays report blocks onto pages: maintains a vertical cursor, starts new
/// pages when content runs past the bottom margin, and renders each block
/// type with its style.
/// </summary>
internal sealed class ReportLayoutEngine
{
    private readonly PdfDocumentBuilder _doc;
    private readonly ReportPageSetup _setup;
    private PageBuilder? _page;
    private double _y;

    internal ReportLayoutEngine(PdfDocumentBuilder doc, ReportPageSetup setup)
    {
        _doc = doc;
        _setup = setup;
    }

    private double Bottom => _setup.PageSize.Height - _setup.MarginBottom;

    private double ContentLeft => _setup.MarginLeft;

    private double ContentWidth => _setup.ContentWidth;

    /// <summary>Measures single-line text width via the Standard-14 metrics.</summary>
    internal static double Measure(string text, string font, double size)
        => FontMetrics.MeasureText(text, font, size);

    /// <summary>Renders all blocks. At least one page is always produced.</summary>
    internal void Run(IReadOnlyList<ReportBlock> blocks)
    {
        foreach (ReportBlock block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock p:
                    RenderParagraph(p);
                    break;
                case ListBlock l:
                    RenderList(l);
                    break;
                case TableBlock t:
                    RenderTable(t.Table);
                    break;
                case ImageBlock i:
                    RenderImage(i);
                    break;
                case SpacerBlock s:
                    EnsurePage();
                    _y += s.Height;
                    break;
                case PageBreakBlock:
                    NewPage();
                    break;
                case RuleBlock r:
                    RenderRule(r);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown report block: {block.GetType().Name}.");
            }
        }

        EnsurePage();
    }

    // ── Page management ───────────────────────────────────────────────────

    private void EnsurePage()
    {
        if (_page is null)
        {
            NewPage();
        }
    }

    private void NewPage()
    {
        _page = _doc.AddPage(_setup.PageSize);
        _y = _setup.MarginTop;
    }

    private void EnsureRoom(double height)
    {
        EnsurePage();
        if (_y + height > Bottom)
        {
            NewPage();
        }
    }

    // ── Paragraphs ────────────────────────────────────────────────────────

    private void RenderParagraph(ParagraphBlock block)
    {
        ParagraphStyle style = block.Style;
        string font = style.Font.Resolve();
        double lineHeight = style.FontSize * style.LineSpacing;
        double width = ContentWidth - style.LeftIndent - style.RightIndent;
        double firstWidth = Math.Max(10, width - style.FirstLineIndent);
        width = Math.Max(10, width);

        EnsurePage();
        _y += style.SpaceBefore;

        List<string> lines = WrapText(
            block.Text, font, style.FontSize, firstLineWidth: firstWidth, width: width);

        for (int i = 0; i < lines.Count; i++)
        {
            EnsureRoom(lineHeight);
            bool firstLine = i == 0;
            double lineLeft = ContentLeft + style.LeftIndent + (firstLine ? style.FirstLineIndent : 0);
            double lineWidth = firstLine ? firstWidth : width;
            bool lastLine = i == lines.Count - 1;

            DrawAlignedLine(lines[i], lineLeft, lineWidth, font, style.FontSize,
                style.Color, style.Alignment, justify: style.Alignment == TextAlignment.Justify && !lastLine);

            _y += lineHeight;
        }

        _y += style.SpaceAfter;
    }

    private void DrawAlignedLine(
        string line, double left, double width,
        string font, double size, Color color, TextAlignment alignment, bool justify)
    {
        if (line.Length == 0)
        {
            return;
        }

        if (justify)
        {
            DrawJustifiedLine(line, left, width, font, size, color);
            return;
        }

        double textWidth = Measure(line, font, size);
        double x = left;
        if (alignment == TextAlignment.Center)
        {
            x = left + ((width - textWidth) / 2);
        }
        else if (alignment == TextAlignment.Right)
        {
            x = left + width - textWidth;
        }

        _page!.DrawText(WinAnsiText.Map(line), x, _y, font, size, color);
    }

    private void DrawJustifiedLine(
        string line, double left, double width, string font, double size, Color color)
    {
        string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
        {
            _page!.DrawText(WinAnsiText.Map(line), left, _y, font, size, color);
            return;
        }

        double wordsWidth = 0;
        foreach (string word in words)
        {
            wordsWidth += Measure(word, font, size);
        }

        double gap = (width - wordsWidth) / (words.Length - 1);
        double spaceWidth = Measure(" ", font, size);
        if (gap < spaceWidth)
        {
            gap = spaceWidth;
        }

        double x = left;
        foreach (string word in words)
        {
            _page!.DrawText(WinAnsiText.Map(word), x, _y, font, size, color);
            x += Measure(word, font, size) + gap;
        }
    }

    // ── Lists ─────────────────────────────────────────────────────────────

    private void RenderList(ListBlock block)
    {
        ListStyle style = block.Style;
        string font = style.Font.Resolve();
        double lineHeight = style.FontSize * style.LineSpacing;
        double textLeft = ContentLeft + style.TextIndent;
        double textWidth = Math.Max(10, ContentWidth - style.TextIndent);

        EnsurePage();

        for (int item = 0; item < block.Items.Count; item++)
        {
            string marker = block.Ordered
                ? PageNumberFormatter.Format(style.StartAt + item, style.Numbering) + style.NumberSuffix
                : style.Bullet;

            List<string> lines = WrapText(
                block.Items[item], font, style.FontSize, textWidth, textWidth);

            for (int i = 0; i < lines.Count; i++)
            {
                EnsureRoom(lineHeight);
                if (i == 0)
                {
                    _page!.DrawText(WinAnsiText.Map(marker),
                        ContentLeft + style.MarkerIndent, _y, font, style.FontSize, style.Color);
                }
                _page!.DrawText(WinAnsiText.Map(lines[i]),
                    textLeft, _y, font, style.FontSize, style.Color);
                _y += lineHeight;
            }

            _y += style.ItemSpacing;
        }

        _y += style.SpaceAfter - style.ItemSpacing;
    }

    // ── Rules and images ──────────────────────────────────────────────────

    private void RenderRule(RuleBlock block)
    {
        EnsureRoom(block.Width + 2);
        _page!.DrawLine(ContentLeft, _y + 1, ContentLeft + ContentWidth, _y + 1,
            block.Color, block.Width);
        _y += block.Width + 2 + block.SpaceAfter;
    }

    private void RenderImage(ImageBlock block)
    {
        (double pxW, double pxH) = block.Frame is not null
            ? (block.Frame.Width, block.Frame.Height)
            : MeasurePixels(block.Bytes!);

        double naturalW = pxW * 72.0 / 96.0;
        double naturalH = pxH * 72.0 / 96.0;
        double aspect = pxW / Math.Max(1, pxH);

        double w;
        double h;
        if (block.Width is double bw && block.Height is double bh)
        {
            w = bw;
            h = bh;
        }
        else if (block.Width is double onlyW)
        {
            w = onlyW;
            h = onlyW / aspect;
        }
        else if (block.Height is double onlyH)
        {
            h = onlyH;
            w = onlyH * aspect;
        }
        else
        {
            w = naturalW;
            h = naturalH;
        }

        // Cap to the content box, preserving aspect ratio.
        if (w > ContentWidth)
        {
            double scale = ContentWidth / w;
            w *= scale;
            h *= scale;
        }
        double maxH = _setup.ContentHeight;
        if (h > maxH)
        {
            double scale = maxH / h;
            w *= scale;
            h *= scale;
        }

        EnsureRoom(h);

        double x = ContentLeft;
        if (block.Alignment == TextAlignment.Center)
        {
            x = ContentLeft + ((ContentWidth - w) / 2);
        }
        else if (block.Alignment == TextAlignment.Right)
        {
            x = ContentLeft + ContentWidth - w;
        }

        if (block.Frame is not null)
        {
            _page!.DrawImage(block.Frame, x, _y, w, h);
        }
        else
        {
            _page!.DrawImage(block.Bytes!, x, _y, w, h);
        }

        _y += h + block.SpaceAfter;
    }

    private static (double W, double H) MeasurePixels(byte[] bytes)
    {
        (int w, int h) = ImageEmbedder.Measure(bytes);
        return (w, h);
    }

    // ── Tables ────────────────────────────────────────────────────────────

    private sealed class PlacedCell
    {
        internal required ReportCell Cell { get; init; }

        internal required ReportRow Row { get; init; }

        internal required int RowIndex { get; init; }

        internal required int ColIndex { get; init; }

        internal required int RowSpan { get; init; }

        internal required int ColSpan { get; init; }

        internal int BodyRowIndex { get; init; }
    }

    private void RenderTable(ReportTable table)
    {
        TableStyle style = table.Style;
        double[] colWidths = ResolveColumnWidths(table);
        double tableWidth = 0;
        foreach (double cw in colWidths)
        {
            tableWidth += cw;
        }

        List<PlacedCell> placed = PlaceCells(table);
        double[] rowHeights = ComputeRowHeights(table, placed, colWidths);
        double headerHeight = style.ShowHeader ? ComputeHeaderHeight(table, colWidths) : 0;

        // Row groups: rows tied together by row spans paginate as a unit.
        List<(int Start, int End)> groups = ComputeRowGroups(table, placed);

        EnsurePage();
        bool headerDrawnOnPage = false;
        if (style.ShowHeader)
        {
            if (_y + headerHeight + MinFirstRowHeight(rowHeights) > Bottom)
            {
                NewPage();
            }
            DrawTableHeader(table, colWidths, headerHeight);
            headerDrawnOnPage = true;
        }

        foreach ((int start, int end) in groups)
        {
            double groupHeight = 0;
            for (int r = start; r <= end; r++)
            {
                groupHeight += rowHeights[r];
            }

            double freshCapacity = _setup.ContentHeight - headerHeight;
            if (_y + groupHeight > Bottom && groupHeight <= freshCapacity)
            {
                NewPage();
                headerDrawnOnPage = false;
                if (style.ShowHeader && style.RepeatHeaderOnEveryPage)
                {
                    DrawTableHeader(table, colWidths, headerHeight);
                    headerDrawnOnPage = true;
                }
            }

            // Draw the group's rows. Groups taller than a fresh page split at
            // row boundaries; spanning cells then clamp to the page bottom.
            for (int r = start; r <= end; r++)
            {
                if (_y + rowHeights[r] > Bottom)
                {
                    NewPage();
                    headerDrawnOnPage = false;
                    if (style.ShowHeader && style.RepeatHeaderOnEveryPage)
                    {
                        DrawTableHeader(table, colWidths, headerHeight);
                        headerDrawnOnPage = true;
                    }
                }

                DrawBodyRow(table, placed, colWidths, rowHeights, r);
                _y += rowHeights[r];
            }
        }

        _ = headerDrawnOnPage;
        _y += style.SpaceAfter;
    }

    private static double MinFirstRowHeight(double[] rowHeights)
        => rowHeights.Length > 0 ? rowHeights[0] : 0;

    private double[] ResolveColumnWidths(ReportTable table)
    {
        double total = ContentWidth;
        double[] widths = new double[table.Columns.Count];
        double used = 0;
        int autos = 0;

        for (int i = 0; i < table.Columns.Count; i++)
        {
            ReportColumn col = table.Columns[i];
            switch (col.WidthMode)
            {
                case ColumnWidthMode.Points:
                    widths[i] = Math.Max(1, col.Width);
                    used += widths[i];
                    break;
                case ColumnWidthMode.Fraction:
                    if (col.Width <= 0 || col.Width > 1)
                    {
                        throw new InvalidOperationException(
                            $"Column {i}: fraction width must be in (0, 1], got {col.Width}.");
                    }
                    widths[i] = col.Width * total;
                    used += widths[i];
                    break;
                default:
                    autos++;
                    break;
            }
        }

        if (autos > 0)
        {
            double share = Math.Max(20, (total - used) / autos);
            for (int i = 0; i < table.Columns.Count; i++)
            {
                if (table.Columns[i].WidthMode == ColumnWidthMode.Auto)
                {
                    widths[i] = share;
                }
            }
        }

        return widths;
    }

    private static List<PlacedCell> PlaceCells(ReportTable table)
    {
        int cols = table.Columns.Count;
        int rows = table.Rows.Count;
        bool[,] occupied = new bool[rows, cols];
        List<PlacedCell> placed = new();

        for (int r = 0; r < rows; r++)
        {
            ReportRow row = table.Rows[r];
            int c = 0;
            foreach (ReportCell cell in row.Cells)
            {
                while (c < cols && occupied[r, c])
                {
                    c++;
                }
                if (c >= cols)
                {
                    throw new InvalidOperationException(
                        $"Row {r}: more cells than available column positions ({cols}).");
                }

                int colSpan = Math.Max(1, cell.ColSpan);
                int rowSpan = Math.Max(1, cell.RowSpan);
                if (c + colSpan > cols)
                {
                    throw new InvalidOperationException(
                        $"Row {r}: cell at column {c} spans {colSpan} columns past the table edge.");
                }
                int maxRowSpan = Math.Min(rowSpan, rows - r);

                for (int rr = r; rr < r + maxRowSpan; rr++)
                {
                    for (int cc = c; cc < c + colSpan; cc++)
                    {
                        if (occupied[rr, cc])
                        {
                            throw new InvalidOperationException(
                                $"Row {rr}: overlapping spans at column {cc}.");
                        }
                        occupied[rr, cc] = true;
                    }
                }

                placed.Add(new PlacedCell
                {
                    Cell = cell,
                    Row = row,
                    RowIndex = r,
                    ColIndex = c,
                    RowSpan = maxRowSpan,
                    ColSpan = colSpan,
                    BodyRowIndex = r,
                });
                c += colSpan;
            }
        }

        return placed;
    }

    private double[] ComputeRowHeights(
        ReportTable table, List<PlacedCell> placed, double[] colWidths)
    {
        TableStyle style = table.Style;
        double[] heights = new double[table.Rows.Count];
        double minHeight = (style.FontSize * style.LineSpacing) + (style.CellPadding * 2);

        for (int r = 0; r < table.Rows.Count; r++)
        {
            if (table.Rows[r].Height > 0)
            {
                heights[r] = table.Rows[r].Height;
            }
            else
            {
                heights[r] = minHeight;
            }
        }

        // Single-row cells size their own row.
        foreach (PlacedCell pc in placed)
        {
            if (pc.RowSpan != 1 || pc.Row.Height > 0)
            {
                continue;
            }
            double inner = InnerWidth(pc, colWidths, style);
            double content = CellContentHeight(pc.Cell, table, inner);
            double needed = content + (style.CellPadding * 2);
            if (needed > heights[pc.RowIndex])
            {
                heights[pc.RowIndex] = needed;
            }
        }

        // Spanning cells expand the last spanned row when they need more space.
        foreach (PlacedCell pc in placed)
        {
            if (pc.RowSpan <= 1)
            {
                continue;
            }
            double inner = InnerWidth(pc, colWidths, style);
            double needed = CellContentHeight(pc.Cell, table, inner) + (style.CellPadding * 2);
            double have = 0;
            for (int r = pc.RowIndex; r < pc.RowIndex + pc.RowSpan; r++)
            {
                have += heights[r];
            }
            if (needed > have)
            {
                heights[pc.RowIndex + pc.RowSpan - 1] += needed - have;
            }
        }

        return heights;
    }

    private static double InnerWidth(PlacedCell pc, double[] colWidths, TableStyle style)
    {
        double w = 0;
        for (int c = pc.ColIndex; c < pc.ColIndex + pc.ColSpan; c++)
        {
            w += colWidths[c];
        }
        return Math.Max(4, w - (style.CellPadding * 2));
    }

    private double CellContentHeight(ReportCell cell, ReportTable table, double innerWidth)
    {
        TableStyle style = table.Style;
        double size = cell.FontSize ?? style.FontSize;
        double lineHeight = size * style.LineSpacing;

        if (cell.ImageFrame is not null || cell.ImageBytes is not null)
        {
            (double pxW, double pxH) = cell.ImageFrame is not null
                ? (cell.ImageFrame.Width, cell.ImageFrame.Height)
                : MeasurePixels(cell.ImageBytes!);
            double aspect = pxW / Math.Max(1, pxH);
            return innerWidth / aspect;
        }

        CellOverflow overflow = cell.Overflow ?? CellOverflow.Wrap;
        if (overflow != CellOverflow.Wrap)
        {
            return lineHeight;
        }

        string font = (cell.Font ?? style.Font).Resolve();
        List<string> lines = WrapText(cell.Text, font, size, innerWidth, innerWidth);
        return Math.Max(1, lines.Count) * lineHeight;
    }

    private static List<(int Start, int End)> ComputeRowGroups(
        ReportTable table, List<PlacedCell> placed)
    {
        int rows = table.Rows.Count;
        List<(int, int)> groups = new();
        if (rows == 0)
        {
            return groups;
        }

        // A boundary between r and r+1 is "welded" when any cell spans across it.
        bool[] welded = new bool[Math.Max(0, rows - 1)];
        foreach (PlacedCell pc in placed)
        {
            for (int r = pc.RowIndex; r < pc.RowIndex + pc.RowSpan - 1; r++)
            {
                welded[r] = true;
            }
        }

        int start = 0;
        for (int r = 0; r < rows; r++)
        {
            bool boundary = r == rows - 1 || !welded[r];
            if (boundary)
            {
                groups.Add((start, r));
                start = r + 1;
            }
        }
        return groups;
    }

    private double ComputeHeaderHeight(ReportTable table, double[] colWidths)
    {
        TableStyle style = table.Style;
        double size = style.HeaderFontSize > 0 ? style.HeaderFontSize : style.FontSize;
        string font = style.HeaderFont.Resolve();
        double lineHeight = size * style.LineSpacing;
        double height = lineHeight + (style.CellPadding * 2);

        for (int c = 0; c < table.Columns.Count; c++)
        {
            double inner = Math.Max(4, colWidths[c] - (style.CellPadding * 2));
            List<string> lines = WrapText(table.Columns[c].Header, font, size, inner, inner);
            double needed = (Math.Max(1, lines.Count) * lineHeight) + (style.CellPadding * 2);
            if (needed > height)
            {
                height = needed;
            }
        }
        return height;
    }

    private void DrawTableHeader(ReportTable table, double[] colWidths, double headerHeight)
    {
        TableStyle style = table.Style;
        double size = style.HeaderFontSize > 0 ? style.HeaderFontSize : style.FontSize;
        string font = style.HeaderFont.Resolve();
        double lineHeight = size * style.LineSpacing;

        EnsureRoom(headerHeight);
        double x = ContentLeft;
        double tableWidth = 0;
        foreach (double cw in colWidths)
        {
            tableWidth += cw;
        }

        if (style.HeaderBackground is Color bg)
        {
            _page!.DrawRectangle(ContentLeft, _y, tableWidth, headerHeight, fill: bg, stroke: null);
        }

        for (int c = 0; c < table.Columns.Count; c++)
        {
            double inner = Math.Max(4, colWidths[c] - (style.CellPadding * 2));
            List<string> lines = WrapText(table.Columns[c].Header, font, size, inner, inner);
            double yText = _y + style.CellPadding;
            foreach (string line in lines)
            {
                double lw = Measure(line, font, size);
                double lx = table.Columns[c].Alignment switch
                {
                    TextAlignment.Center => x + ((colWidths[c] - lw) / 2),
                    TextAlignment.Right => x + colWidths[c] - style.CellPadding - lw,
                    _ => x + style.CellPadding,
                };
                _page!.DrawText(WinAnsiText.Map(line), lx, yText, font, size, style.HeaderTextColor);
                yText += lineHeight;
            }

            if (style.BorderMode == TableBorderMode.Grid)
            {
                _page!.DrawRectangle(x, _y, colWidths[c], headerHeight,
                    fill: null, stroke: style.BorderColor, strokeWidth: style.BorderWidth);
            }
            x += colWidths[c];
        }

        if (style.BorderMode == TableBorderMode.Outline)
        {
            _page!.DrawRectangle(ContentLeft, _y, tableWidth, headerHeight,
                fill: null, stroke: style.BorderColor, strokeWidth: style.BorderWidth);
        }
        else if (style.BorderMode == TableBorderMode.HorizontalOnly)
        {
            _page!.DrawLine(ContentLeft, _y, ContentLeft + tableWidth, _y,
                style.BorderColor, style.BorderWidth);
            _page!.DrawLine(ContentLeft, _y + headerHeight, ContentLeft + tableWidth, _y + headerHeight,
                style.BorderColor, style.BorderWidth);
        }
        else if (style.BorderMode == TableBorderMode.HeaderUnderlineOnly)
        {
            _page!.DrawLine(ContentLeft, _y + headerHeight, ContentLeft + tableWidth, _y + headerHeight,
                style.BorderColor, style.BorderWidth);
        }

        _y += headerHeight;
    }

    private void DrawBodyRow(
        ReportTable table, List<PlacedCell> placed,
        double[] colWidths, double[] rowHeights, int rowIndex)
    {
        TableStyle style = table.Style;
        double tableWidth = 0;
        foreach (double cw in colWidths)
        {
            tableWidth += cw;
        }

        foreach (PlacedCell pc in placed)
        {
            if (pc.RowIndex != rowIndex)
            {
                continue;
            }

            double x = ContentLeft;
            for (int c = 0; c < pc.ColIndex; c++)
            {
                x += colWidths[c];
            }
            double w = 0;
            for (int c = pc.ColIndex; c < pc.ColIndex + pc.ColSpan; c++)
            {
                w += colWidths[c];
            }
            double h = 0;
            for (int r = pc.RowIndex; r < pc.RowIndex + pc.RowSpan; r++)
            {
                h += rowHeights[r];
            }
            // Spanning cells clamp to the page bottom on a degraded split.
            if (_y + h > Bottom)
            {
                h = Math.Max(rowHeights[rowIndex], Bottom - _y);
            }

            PaintCell(table, pc, x, _y, w, h);
        }

        // Border passes that depend on row boundaries.
        if (style.BorderMode == TableBorderMode.HorizontalOnly)
        {
            double yBottom = _y + rowHeights[rowIndex];
            DrawRowBoundary(table, placed, colWidths, rowIndex, yBottom);
        }
        else if (style.BorderMode == TableBorderMode.Outline)
        {
            _page!.DrawLine(ContentLeft, _y, ContentLeft, _y + rowHeights[rowIndex],
                style.BorderColor, style.BorderWidth);
            _page!.DrawLine(ContentLeft + tableWidth, _y, ContentLeft + tableWidth,
                _y + rowHeights[rowIndex], style.BorderColor, style.BorderWidth);
            if (rowIndex == table.Rows.Count - 1)
            {
                _page!.DrawLine(ContentLeft, _y + rowHeights[rowIndex],
                    ContentLeft + tableWidth, _y + rowHeights[rowIndex],
                    style.BorderColor, style.BorderWidth);
            }
        }
    }

    private void DrawRowBoundary(
        ReportTable table, List<PlacedCell> placed, double[] colWidths,
        int rowIndex, double y)
    {
        TableStyle style = table.Style;
        int cols = table.Columns.Count;
        bool lastRow = rowIndex == table.Rows.Count - 1;

        // Draw the boundary line, skipping columns where a span crosses it.
        bool[] crossed = new bool[cols];
        if (!lastRow)
        {
            foreach (PlacedCell pc in placed)
            {
                if (pc.RowIndex <= rowIndex && pc.RowIndex + pc.RowSpan - 1 > rowIndex)
                {
                    for (int c = pc.ColIndex; c < pc.ColIndex + pc.ColSpan; c++)
                    {
                        crossed[c] = true;
                    }
                }
            }
        }

        double x = ContentLeft;
        int segStartCol = -1;
        double segStartX = 0;
        for (int c = 0; c <= cols; c++)
        {
            bool draw = c < cols && !crossed[c];
            if (draw && segStartCol < 0)
            {
                segStartCol = c;
                segStartX = x;
            }
            else if (!draw && segStartCol >= 0)
            {
                _page!.DrawLine(segStartX, y, x, y, style.BorderColor, style.BorderWidth);
                segStartCol = -1;
            }
            if (c < cols)
            {
                x += colWidths[c];
            }
        }
    }

    private void PaintCell(
        ReportTable table, PlacedCell pc, double x, double y, double w, double h)
    {
        TableStyle style = table.Style;
        ReportCell cell = pc.Cell;

        Color? background = cell.Background ?? pc.Row.Background;
        if (background is null && style.AlternatingRowBackground is Color alt &&
            (pc.BodyRowIndex % 2) == 1)
        {
            background = alt;
        }
        if (background is Color bg)
        {
            _page!.DrawRectangle(x, y, w, h, fill: bg, stroke: null);
        }

        double pad = style.CellPadding;
        double inner = Math.Max(4, w - (pad * 2));
        double innerH = Math.Max(2, h - (pad * 2));

        if (cell.ImageFrame is not null || cell.ImageBytes is not null)
        {
            PaintCellImage(cell, x, y, h, pad, inner, innerH);
        }
        else if (cell.Text.Length > 0)
        {
            PaintCellText(table, pc, x, y, w, h, pad, inner, innerH);
        }

        if (style.BorderMode == TableBorderMode.Grid)
        {
            _page!.DrawRectangle(x, y, w, h,
                fill: null, stroke: style.BorderColor, strokeWidth: style.BorderWidth);
        }
    }

    private void PaintCellImage(
        ReportCell cell, double x, double y, double h,
        double pad, double inner, double innerH)
    {
        (double pxW, double pxH) = cell.ImageFrame is not null
            ? (cell.ImageFrame.Width, cell.ImageFrame.Height)
            : MeasurePixels(cell.ImageBytes!);
        double aspect = pxW / Math.Max(1, pxH);

        double drawW = inner;
        double drawH = drawW / aspect;
        if (drawH > innerH)
        {
            drawH = innerH;
            drawW = drawH * aspect;
        }

        double ix = x + pad + ((inner - drawW) / 2);
        double iy = cell.VerticalAlignment switch
        {
            VerticalAlignment.Middle => y + ((h - drawH) / 2),
            VerticalAlignment.Bottom => y + h - pad - drawH,
            _ => y + pad,
        };

        if (cell.ImageFrame is not null)
        {
            _page!.DrawImage(cell.ImageFrame, ix, iy, drawW, drawH);
        }
        else
        {
            _page!.DrawImage(cell.ImageBytes!, ix, iy, drawW, drawH);
        }
    }

    private void PaintCellText(
        ReportTable table, PlacedCell pc, double x, double y, double w, double h,
        double pad, double inner, double innerH)
    {
        TableStyle style = table.Style;
        ReportCell cell = pc.Cell;
        ReportColumn column = table.Columns[pc.ColIndex];

        string font = (cell.Font ?? style.Font).Resolve();
        double size = cell.FontSize ?? style.FontSize;
        Color color = cell.TextColor ?? style.TextColor;
        TextAlignment align = cell.Alignment ?? column.Alignment;
        CellOverflow overflow = cell.Overflow ?? column.Overflow;
        double lineHeight = size * style.LineSpacing;

        List<string> lines;
        if (overflow == CellOverflow.Wrap)
        {
            lines = WrapText(cell.Text, font, size, inner, inner);
            int maxLines = Math.Max(1, (int)Math.Floor((innerH + 0.01) / lineHeight));
            if (lines.Count > maxLines)
            {
                lines = lines.GetRange(0, maxLines);
            }
        }
        else
        {
            lines = new List<string>
            {
                FitSingleLine(cell.Text, font, size, inner, overflow == CellOverflow.Ellipsis),
            };
        }

        double blockHeight = lines.Count * lineHeight;
        double yText = cell.VerticalAlignment switch
        {
            VerticalAlignment.Middle => y + ((h - blockHeight) / 2),
            VerticalAlignment.Bottom => y + h - pad - blockHeight,
            _ => y + pad,
        };

        foreach (string line in lines)
        {
            double lw = Measure(line, font, size);
            double lx = align switch
            {
                TextAlignment.Center => x + ((w - lw) / 2),
                TextAlignment.Right => x + w - pad - lw,
                _ => x + pad,
            };
            _page!.DrawText(WinAnsiText.Map(line), lx, yText, font, size, color);
            yText += lineHeight;
        }
    }

    // ── Text wrapping ─────────────────────────────────────────────────────

    private static string FitSingleLine(
        string text, string font, double size, double maxWidth, bool ellipsis)
    {
        string oneLine = text.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ');
        if (Measure(oneLine, font, size) <= maxWidth)
        {
            return oneLine;
        }

        string suffix = ellipsis ? "\u2026" : string.Empty;
        double suffixWidth = ellipsis ? Measure(suffix, font, size) : 0;
        StringBuilder sb = new();
        double width = 0;
        foreach (char ch in oneLine)
        {
            double cw = Measure(ch.ToString(), font, size);
            if (width + cw + suffixWidth > maxWidth)
            {
                break;
            }
            sb.Append(ch);
            width += cw;
        }
        sb.Append(suffix);
        return sb.ToString();
    }

    /// <summary>
    /// Word-wraps text to the given widths (the first line may differ for
    /// first-line indents). Words wider than a whole line break by character.
    /// Embedded newlines force line breaks.
    /// </summary>
    internal static List<string> WrapText(
        string text, string font, double size, double firstLineWidth, double width)
    {
        List<string> lines = new();
        string[] paragraphs = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        foreach (string para in paragraphs)
        {
            if (para.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            string[] words = para.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            StringBuilder current = new();
            double lineWidth = lines.Count == 0 ? firstLineWidth : width;

            foreach (string rawWord in words)
            {
                string word = rawWord;

                // Break words wider than a whole line by character.
                while (Measure(word, font, size) > lineWidth)
                {
                    if (current.Length > 0)
                    {
                        lines.Add(current.ToString());
                        current.Clear();
                        lineWidth = width;
                    }
                    int take = 1;
                    while (take < word.Length &&
                           Measure(word[..(take + 1)], font, size) <= lineWidth)
                    {
                        take++;
                    }
                    lines.Add(word[..take]);
                    word = word[take..];
                    lineWidth = width;
                    if (word.Length == 0)
                    {
                        break;
                    }
                }
                if (word.Length == 0)
                {
                    continue;
                }

                string candidate = current.Length == 0
                    ? word
                    : current.ToString() + " " + word;
                if (Measure(candidate, font, size) <= lineWidth)
                {
                    if (current.Length > 0)
                    {
                        current.Append(' ');
                    }
                    current.Append(word);
                }
                else
                {
                    if (current.Length > 0)
                    {
                        lines.Add(current.ToString());
                        current.Clear();
                        lineWidth = width;
                    }
                    current.Append(word);
                }
            }

            if (current.Length > 0)
            {
                lines.Add(current.ToString());
            }
        }

        return lines;
    }
}
