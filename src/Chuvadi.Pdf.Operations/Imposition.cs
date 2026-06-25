// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10 (form XObjects); §8.4.4 (clipping). LA-11.
// Imposition primitives built on PageComposer.PlacePage + DestinationClip.

using System;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Composes the pages of a source document onto larger sheets: N-up grids and
/// 2-up saddle-stitch booklets. Each source page is scaled to fit its cell
/// (aspect preserved), centered, and clipped to the cell so nothing overflows.
/// </summary>
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
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Rows < 1 || options.Columns < 1)
        {
            throw new ArgumentException("Rows and Columns must be at least 1.", nameof(options));
        }

        double sheetW = options.SheetSize.Width;
        double sheetH = options.SheetSize.Height;
        int perSheet = options.Rows * options.Columns;
        double cellW = (sheetW - (2 * options.Margin) - ((options.Columns - 1) * options.Gutter)) / options.Columns;
        double cellH = (sheetH - (2 * options.Margin) - ((options.Rows - 1) * options.Gutter)) / options.Rows;

        PageComposer composer = new PageComposer();
        int total = source.PageCount;

        for (int start = 0; start < total; start += perSheet)
        {
            composer.AddPage(sheetW, sheetH);
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
                PlaceInCell(composer, source, sourceIndex, cellX, cellY, cellW, cellH);
            }
        }

        composer.Write(output, encryption);
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
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        double slotW = options.PageSize.Width;
        double slotH = options.PageSize.Height;
        double sheetW = 2 * slotW;

        int pages = source.PageCount;
        int padded = ((pages + 3) / 4) * 4; // round up to a multiple of four
        int sheets = padded / 4;

        PageComposer composer = new PageComposer();

        for (int s = 0; s < sheets; s++)
        {
            // Saddle-stitch order (1-based page numbers).
            AddBookletSide(composer, source, options, padded - (2 * s), 1 + (2 * s), slotW, slotH, sheetW);
            AddBookletSide(composer, source, options, 2 + (2 * s), padded - 1 - (2 * s), slotW, slotH, sheetW);
        }

        composer.Write(output, encryption);
    }

    private static void AddBookletSide(
        PageComposer composer, PdfDocument source, BookletOptions options,
        int leftPageNumber, int rightPageNumber, double slotW, double slotH, double sheetW)
    {
        composer.AddPage(sheetW, slotH);
        PlaceBookletSlot(composer, source, options, leftPageNumber, 0, slotW, slotH);
        PlaceBookletSlot(composer, source, options, rightPageNumber, slotW, slotW, slotH);
    }

    private static void PlaceBookletSlot(
        PageComposer composer, PdfDocument source, BookletOptions options,
        int pageNumber, double cellX, double cellW, double cellH)
    {
        int index = pageNumber - 1;
        if (index < 0 || index >= source.PageCount)
        {
            return; // blank padding slot
        }

        double innerX = cellX + options.Margin;
        double innerY = options.Margin;
        double innerW = cellW - (2 * options.Margin);
        double innerH = cellH - (2 * options.Margin);
        PlaceFitted(composer, source, index, innerX, innerY, innerW, innerH, cellX, 0, cellW, cellH);
    }

    private static void PlaceInCell(
        PageComposer composer, PdfDocument source, int sourceIndex,
        double cellX, double cellY, double cellW, double cellH)
    {
        PlaceFitted(composer, source, sourceIndex, cellX, cellY, cellW, cellH, cellX, cellY, cellW, cellH);
    }

    private static void PlaceFitted(
        PageComposer composer, PdfDocument source, int sourceIndex,
        double fitX, double fitY, double fitW, double fitH,
        double clipX, double clipY, double clipW, double clipH)
    {
        PdfPage page = source.Pages[sourceIndex];
        double pageW = page.Width;
        double pageH = page.Height;
        if (pageW <= 0 || pageH <= 0 || fitW <= 0 || fitH <= 0)
        {
            return;
        }

        double scale = Math.Min(fitW / pageW, fitH / pageH);
        double placedW = pageW * scale;
        double placedH = pageH * scale;
        double offsetX = fitX + ((fitW - placedW) / 2);
        double offsetY = fitY + ((fitH - placedH) / 2);

        Transform transform = new Transform(scale, 0, 0, scale, offsetX, offsetY);
        PlacePageOptions placeOptions = new PlacePageOptions
        {
            DestinationClip = new RectangleF(clipX, clipY, clipW, clipH),
        };
        composer.PlacePage(source, sourceIndex, transform, placeOptions);
    }
}
