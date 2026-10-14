// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10 (form XObjects); §8.4.4 (clipping);
//        §14.11.2 (page boundaries), Table 30 /Rotate. LA-11.
// Imposition primitives built on PageComposer.PlacePage + DestinationClip.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Composes the pages of a source document onto larger sheets: N-up grids and
/// 2-up saddle-stitch booklets. Each source page is scaled to fit its cell
/// (aspect preserved), centred, and clipped to the cell so nothing overflows.
/// Placement honours each source page's crop box (only the visible region is
/// fitted, wherever its origin lies) and its <c>/Rotate</c> (a rotated page is
/// placed upright, as a viewer displays it).
/// </summary>
/// <remarks>
/// <see cref="PlanNUp"/> and <see cref="PlanBooklet"/> return the exact sheet
/// geometry that <see cref="NUp(Stream, PdfDocument, NUpOptions)"/> and
/// <see cref="Booklet(Stream, PdfDocument, BookletOptions)"/> write, so a
/// preview drawn from a plan matches the output.
/// </remarks>
public static class Imposition
{
    /// <summary>Lays out the source pages as an N-up grid and writes the result.</summary>
    /// <param name="output">The stream to write to.</param>
    /// <param name="source">The source document.</param>
    /// <param name="options">The grid layout options.</param>
    public static void NUp(Stream output, PdfDocument source, NUpOptions options) => NUp(output, source, options, null);

    /// <summary>Lays out the source pages as an N-up grid and writes the result, optionally encrypted.</summary>
    /// <param name="output">The stream to write to.</param>
    /// <param name="source">The source document.</param>
    /// <param name="options">The grid layout options.</param>
    /// <param name="encryption">The encryption options, or null for no encryption.</param>
    public static void NUp(Stream output, PdfDocument source, NUpOptions options, EncryptionOptions? encryption)
    {
        ArgumentNullException.ThrowIfNull(output);
        IReadOnlyList<ImpositionSheet> plan = PlanNUp(source, options);
        Execute(output, source, plan, encryption);
    }

    /// <summary>Lays out the source pages as a 2-up saddle-stitch booklet and writes the result.</summary>
    /// <param name="output">The stream to write to.</param>
    /// <param name="source">The source document.</param>
    /// <param name="options">The booklet layout options.</param>
    public static void Booklet(Stream output, PdfDocument source, BookletOptions options) => Booklet(output, source, options, null);

    /// <summary>Lays out the source pages as a 2-up saddle-stitch booklet and writes the result, optionally encrypted.</summary>
    /// <param name="output">The stream to write to.</param>
    /// <param name="source">The source document.</param>
    /// <param name="options">The booklet layout options.</param>
    /// <param name="encryption">The encryption options, or null for no encryption.</param>
    public static void Booklet(Stream output, PdfDocument source, BookletOptions options, EncryptionOptions? encryption)
    {
        ArgumentNullException.ThrowIfNull(output);
        IReadOnlyList<ImpositionSheet> plan = PlanBooklet(source, options);
        Execute(output, source, plan, encryption);
    }

    /// <summary>
    /// Computes the N-up sheet layout without writing anything: one
    /// <see cref="ImpositionSheet"/> per output sheet, each listing where every
    /// source page lands.
    /// </summary>
    /// <param name="source">The source document.</param>
    /// <param name="options">The grid layout options.</param>
    /// <returns>The sheets, in output order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the source has no pages, rows or columns are below 1, or the
    /// margin and gutter leave no room for a cell.
    /// </exception>
    public static IReadOnlyList<ImpositionSheet> PlanNUp(PdfDocument source, NUpOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        RequirePages(source);

        if (options.Rows < 1 || options.Columns < 1)
        {
            throw new ArgumentException("Rows and Columns must be at least 1.", nameof(options));
        }

        if (options.Margin < 0 || options.Gutter < 0)
        {
            throw new ArgumentException("Margin and Gutter must not be negative.", nameof(options));
        }

        double sheetW = options.SheetSize.Width;
        double sheetH = options.SheetSize.Height;
        int perSheet = options.Rows * options.Columns;
        double cellW = (sheetW - (2 * options.Margin) - ((options.Columns - 1) * options.Gutter)) / options.Columns;
        double cellH = (sheetH - (2 * options.Margin) - ((options.Rows - 1) * options.Gutter)) / options.Rows;

        if (cellW <= 0 || cellH <= 0)
        {
            throw new ArgumentException(
                "The margin and gutter leave no room for a cell on the sheet.", nameof(options));
        }

        List<ImpositionSheet> sheets = new List<ImpositionSheet>();
        int total = source.PageCount;

        for (int start = 0; start < total; start += perSheet)
        {
            List<ImpositionPlacement> placements = new List<ImpositionPlacement>();
            for (int slot = 0; slot < perSheet; slot++)
            {
                int sourceIndex = start + slot;
                if (sourceIndex >= total)
                {
                    break;
                }

                (int row, int col) = options.Order == NUpOrder.RowMajor
                    ? (slot / options.Columns, slot % options.Columns)
                    : (slot % options.Rows, slot / options.Rows);

                double cellX = options.Margin + (col * (cellW + options.Gutter));
                double cellY = sheetH - options.Margin - ((row + 1) * cellH) - (row * options.Gutter);
                RectangleF cell = new RectangleF(cellX, cellY, cellW, cellH);
                AddPlacement(placements, source, sourceIndex, cell, cell);
            }

            sheets.Add(new ImpositionSheet(sheetW, sheetH, placements));
        }

        return sheets;
    }

    /// <summary>
    /// Computes the saddle-stitch booklet layout without writing anything: two
    /// sheet sides per physical sheet, in print order, padded with blank slots
    /// to a multiple of four pages.
    /// </summary>
    /// <param name="source">The source document.</param>
    /// <param name="options">The booklet layout options.</param>
    /// <returns>The sheet sides, in output order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the source has no pages or the margin leaves no room in a slot.
    /// </exception>
    public static IReadOnlyList<ImpositionSheet> PlanBooklet(PdfDocument source, BookletOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        RequirePages(source);

        double slotW = options.PageSize.Width;
        double slotH = options.PageSize.Height;
        if (options.Margin < 0
            || slotW - (2 * options.Margin) <= 0
            || slotH - (2 * options.Margin) <= 0)
        {
            throw new ArgumentException(
                "The margin must be non-negative and leave room in each page slot.", nameof(options));
        }

        double sheetW = 2 * slotW;
        int pages = source.PageCount;
        int padded = ((pages + 3) / 4) * 4; // round up to a multiple of four
        int sheetCount = padded / 4;

        List<ImpositionSheet> sheets = new List<ImpositionSheet>();
        for (int s = 0; s < sheetCount; s++)
        {
            // Saddle-stitch order (1-based page numbers): front, then back.
            sheets.Add(BookletSide(source, options, padded - (2 * s), 1 + (2 * s), slotW, slotH, sheetW));
            sheets.Add(BookletSide(source, options, 2 + (2 * s), padded - 1 - (2 * s), slotW, slotH, sheetW));
        }

        return sheets;
    }

    /// <summary>
    /// Returns the transform that fits a source page's visible area — its crop
    /// box, displayed upright per <c>/Rotate</c> — centred inside
    /// <paramref name="target"/> with its aspect ratio preserved. Concatenate
    /// it (<c>cm</c>) before drawing the page's content, as
    /// <see cref="PageComposer.PlacePage(PdfDocument, int, Transform)"/> does.
    /// </summary>
    /// <param name="page">The source page.</param>
    /// <param name="target">The rectangle to fit into, in target user space.</param>
    /// <returns>The placement transform.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the page's crop box or the target rectangle has no area.
    /// </exception>
    public static Transform FitTransform(PdfPage page, RectangleF target)
    {
        ArgumentNullException.ThrowIfNull(page);

        PdfRectangle crop = page.CropBox;
        double x1 = Math.Min(crop.X1, crop.X2);
        double y1 = Math.Min(crop.Y1, crop.Y2);
        double w = crop.Width;
        double h = crop.Height;
        if (w <= 0 || h <= 0 || target.Width <= 0 || target.Height <= 0)
        {
            throw new ArgumentException("The page crop box and the target must both have area.");
        }

        int rotation = ((page.Rotate % 360) + 360) % 360;

        // Page user space → upright display space (origin at the displayed
        // bottom-left corner). PDF /Rotate turns the page clockwise.
        (double a, double b, double c, double d, double e, double f, double dispW, double dispH) = rotation switch
        {
            90 => (0.0, -1.0, 1.0, 0.0, -y1, x1 + w, h, w),
            180 => (-1.0, 0.0, 0.0, -1.0, x1 + w, y1 + h, w, h),
            270 => (0.0, 1.0, -1.0, 0.0, y1 + h, -x1, h, w),
            _ => (1.0, 0.0, 0.0, 1.0, -x1, -y1, w, h),
        };

        double scale = Math.Min(target.Width / dispW, target.Height / dispH);
        double offsetX = target.X + ((target.Width - (dispW * scale)) / 2);
        double offsetY = target.Y + ((target.Height - (dispH * scale)) / 2);

        return new Transform(
            a * scale,
            b * scale,
            c * scale,
            d * scale,
            (e * scale) + offsetX,
            (f * scale) + offsetY);
    }

    private static ImpositionSheet BookletSide(
        PdfDocument source, BookletOptions options,
        int leftPageNumber, int rightPageNumber, double slotW, double slotH, double sheetW)
    {
        List<ImpositionPlacement> placements = new List<ImpositionPlacement>();
        AddBookletSlot(placements, source, options, leftPageNumber, 0, slotW, slotH);
        AddBookletSlot(placements, source, options, rightPageNumber, slotW, slotW, slotH);
        return new ImpositionSheet(sheetW, slotH, placements);
    }

    private static void AddBookletSlot(
        List<ImpositionPlacement> placements, PdfDocument source, BookletOptions options,
        int pageNumber, double cellX, double cellW, double cellH)
    {
        int index = pageNumber - 1;
        if (index < 0 || index >= source.PageCount)
        {
            return; // blank padding slot
        }

        RectangleF fit = new RectangleF(
            cellX + options.Margin,
            options.Margin,
            cellW - (2 * options.Margin),
            cellH - (2 * options.Margin));
        AddPlacement(placements, source, index, fit, new RectangleF(cellX, 0, cellW, cellH));
    }

    private static void AddPlacement(
        List<ImpositionPlacement> placements, PdfDocument source, int sourceIndex, RectangleF fit, RectangleF cell)
    {
        PdfPage page = source.Pages[sourceIndex];
        PdfRectangle crop = page.CropBox;
        if (crop.Width <= 0 || crop.Height <= 0)
        {
            return; // a page with no visible area places nothing
        }

        placements.Add(new ImpositionPlacement(sourceIndex, FitTransform(page, fit), cell));
    }

    private static void RequirePages(PdfDocument source)
    {
        if (source.PageCount == 0)
        {
            throw new ArgumentException("The source document has no pages to impose.", nameof(source));
        }
    }

    private static void Execute(
        Stream output, PdfDocument source, IReadOnlyList<ImpositionSheet> plan, EncryptionOptions? encryption)
    {
        PageComposer composer = new PageComposer();
        foreach (ImpositionSheet sheet in plan)
        {
            composer.AddPage(sheet.Width, sheet.Height);
            foreach (ImpositionPlacement placement in sheet.Placements)
            {
                PlacePageOptions placeOptions = new PlacePageOptions
                {
                    DestinationClip = placement.Cell,
                };
                composer.PlacePage(source, placement.SourcePageIndex, placement.Transform, placeOptions);
            }
        }

        composer.Write(output, encryption);
    }
}

/// <summary>One output sheet (or booklet sheet side) in an imposition plan.</summary>
public sealed class ImpositionSheet
{
    internal ImpositionSheet(double width, double height, IReadOnlyList<ImpositionPlacement> placements)
    {
        Width = width;
        Height = height;
        Placements = placements;
    }

    /// <summary>Gets the sheet width, in points.</summary>
    public double Width { get; }

    /// <summary>Gets the sheet height, in points.</summary>
    public double Height { get; }

    /// <summary>Gets the source pages placed on this sheet, in drawing order.</summary>
    public IReadOnlyList<ImpositionPlacement> Placements { get; }
}

/// <summary>Where one source page lands on an imposition sheet.</summary>
public sealed class ImpositionPlacement
{
    internal ImpositionPlacement(int sourcePageIndex, Transform transform, RectangleF cell)
    {
        SourcePageIndex = sourcePageIndex;
        Transform = transform;
        Cell = cell;
    }

    /// <summary>Gets the zero-based source page index.</summary>
    public int SourcePageIndex { get; }

    /// <summary>
    /// Gets the transform from source page user space to sheet space (crop box
    /// and <c>/Rotate</c> already folded in; see <see cref="Imposition.FitTransform"/>).
    /// </summary>
    public Transform Transform { get; }

    /// <summary>
    /// Gets the cell rectangle on the sheet (bottom-left origin, points). The
    /// placed page is clipped to it.
    /// </summary>
    public RectangleF Cell { get; }
}
