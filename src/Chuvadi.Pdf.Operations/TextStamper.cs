// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10.1, §9.4 (text), §11.6.4.4 (/ca)
// PHASE: Document operations — positioned text stamp (page numbers, footers).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Draws a single line of text at one of twelve anchor positions on selected
/// pages, with template-token substitution (page numbers in several styles,
/// file name/path, caller-supplied date/time, literal text). The stamp is an
/// overlay: existing content is not moved. For running headers/footers that
/// reserve space and reflow content, use <see cref="HeaderFooter"/>.
/// PDF 32000-1:2008 §9.4 — text; §8.10.1 — form XObjects.
/// </summary>
public static class TextStamper
{
    /// <summary>
    /// Stamps <paramref name="template"/> onto the requested pages.
    /// </summary>
    /// <param name="output">The stream to write the updated PDF to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="pageIndices">
    /// Zero-based page indices to stamp. Null stamps every page.
    /// </param>
    /// <param name="template">The text template (may contain tokens).</param>
    /// <param name="anchor">Where on the page to place the text.</param>
    /// <param name="marginX">Horizontal inset from the page edge, in points.</param>
    /// <param name="marginY">Vertical inset from the page edge, in points.</param>
    /// <param name="fontSize">Font size in points.</param>
    /// <param name="color">Text colour.</param>
    /// <param name="filePath">
    /// Source file path for the <c>{filename}</c>/<c>{filepath}</c> tokens, or null.
    /// </param>
    /// <param name="timestamp">
    /// Caller-supplied timestamp for date/time tokens, or null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="output"/>, <paramref name="document"/>, or
    /// <paramref name="template"/> is null.
    /// </exception>
    public static void Apply(
        Stream output,
        PdfDocument document,
        IEnumerable<int>? pageIndices,
        string template,
        StampAnchor anchor,
        double marginX,
        double marginY,
        double fontSize,
        ColorF color,
        string? filePath = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(template);

        HashSet<int> targets = ResolveTargets(pageIndices, document.PageCount);
        int total = document.PageCount;

        StampWriter writer = new StampWriter(document);

        for (int pageIndex = 0; pageIndex < total; pageIndex++)
        {
            if (!targets.Contains(pageIndex))
            {
                continue;
            }

            PdfPage page = document.Pages[pageIndex];
            StampContext ctx = new StampContext(pageIndex + 1, total, filePath, timestamp);
            string text = StampTokens.Resolve(template, ctx);

            if (text.Length == 0)
            {
                continue;
            }

            Transform placement = AnchorPlacement.ComputePlacement(
                anchor, page.MediaBox, StampText.MeasureWidth(text, fontSize),
                fontSize, marginX, marginY);

            string fragment = StampText.BuildShowText(text, placement, fontSize, color);
            byte[] streamBytes = Encoding.Latin1.GetBytes("q\n" + fragment + "Q\n");
            writer.AddOverlay(pageIndex, streamBytes);
        }

        writer.Write(output);
    }

    /// <summary>
    /// Stamps <paramref name="template"/> onto the requested pages, resolving the
    /// <c>{number}</c> token from a running <see cref="StampNumbering"/> sequence
    /// (Bates / styled page numbering) in a single pass.
    /// </summary>
    /// <param name="output">The stream to write the updated PDF to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="pageIndices">
    /// Zero-based page indices to stamp. Null stamps every page. Pages skipped by
    /// <see cref="StampNumbering.FirstPage"/> are never stamped even when selected.
    /// </param>
    /// <param name="template">The text template (may contain tokens, e.g. <c>{number}</c>).</param>
    /// <param name="anchor">Where on the page to place the text.</param>
    /// <param name="marginX">Horizontal inset from the page edge, in points.</param>
    /// <param name="marginY">Vertical inset from the page edge, in points.</param>
    /// <param name="fontSize">Font size in points.</param>
    /// <param name="color">Text colour.</param>
    /// <param name="numbering">The running numbering sequence used for <c>{number}</c>.</param>
    /// <param name="filePath">
    /// Source file path for the <c>{filename}</c>/<c>{filepath}</c> tokens, or null.
    /// </param>
    /// <param name="timestamp">
    /// Caller-supplied timestamp for date/time tokens, or null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="output"/>, <paramref name="document"/>,
    /// <paramref name="template"/>, or <paramref name="numbering"/> is null.
    /// </exception>
    public static void Apply(Stream output, PdfDocument document, IEnumerable<int>? pageIndices, string template, StampAnchor anchor, double marginX, double marginY, double fontSize, ColorF color, StampNumbering numbering, string? filePath = null, DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(numbering);

        HashSet<int> targets = ResolveTargets(pageIndices, document.PageCount);
        int total = document.PageCount;

        StampWriter writer = new StampWriter(document);

        for (int pageIndex = 0; pageIndex < total; pageIndex++)
        {
            int? sequenceValue = numbering.ResolveValue(pageIndex);
            if (sequenceValue is not int value)
            {
                continue;
            }

            if (!targets.Contains(pageIndex))
            {
                continue;
            }

            PdfPage page = document.Pages[pageIndex];
            string label = numbering.Format(value);
            StampContext ctx = new StampContext(pageIndex + 1, total, filePath, timestamp, label);
            string text = StampTokens.Resolve(template, ctx);

            if (text.Length == 0)
            {
                continue;
            }

            Transform placement = AnchorPlacement.ComputePlacement(
                anchor, page.MediaBox, StampText.MeasureWidth(text, fontSize),
                fontSize, marginX, marginY);

            string fragment = StampText.BuildShowText(text, placement, fontSize, color);
            byte[] streamBytes = Encoding.Latin1.GetBytes("q\n" + fragment + "Q\n");
            writer.AddOverlay(pageIndex, streamBytes);
        }

        writer.Write(output);
    }

    private static HashSet<int> ResolveTargets(IEnumerable<int>? pageIndices, int pageCount)
    {
        if (pageIndices is null)
        {
            HashSet<int> all = new HashSet<int>();
            for (int i = 0; i < pageCount; i++)
            {
                all.Add(i);
            }

            return all;
        }

        return new HashSet<int>(pageIndices);
    }
}
