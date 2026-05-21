// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.10 — Extraction of text content
//        Unicode Bidirectional Algorithm (UAX #9) — strong-direction characters
// PHASE: v2.0.0 R3 — Text run extraction

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Text;

/// <summary>
/// Builds <see cref="TextRun"/> objects from the <see cref="TextFragment"/>
/// list produced by <see cref="TextExtractor.ExtractFragments(Chuvadi.Pdf.Documents.PdfPage)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each input fragment becomes exactly one output run. The builder
/// computes the run's bounding box from the X/Y position and an
/// estimated width derived from glyph count × font size × 0.6 (a stable
/// average across the Standard 14 fonts). Per-glyph positions are
/// allocated by laying out the run characters along the bounding-box
/// baseline.
/// </para>
/// <para>
/// Reading order is the input order: <see cref="Chuvadi.Pdf.Text.TextExtractor.ExtractFragments(Chuvadi.Pdf.Documents.PdfPage)"/>
/// returns fragments in content-stream order, which is the natural
/// reading order for most born-digital PDFs. Multi-column reading-order
/// reconstruction is on the v2.1 roadmap.
/// </para>
/// <para>
/// Directional inference: the builder scans each run for strong-direction
/// characters (Hebrew U+0590..U+05FF, U+FB1D..U+FB4F, Arabic
/// U+0600..U+06FF, U+0750..U+077F, U+08A0..U+08FF, U+FB50..U+FDFF,
/// U+FE70..U+FEFF). A run with more RTL than LTR strong characters is
/// classified <see cref="TextDirection.RightToLeft"/>; otherwise
/// <see cref="TextDirection.LeftToRight"/>. Neutrals do not contribute.
/// </para>
/// </remarks>
public static class TextRunBuilder
{
    /// <summary>Average glyph advance as a fraction of font size.</summary>
    private const double AverageAdvanceFraction = 0.6;

    /// <summary>
    /// Builds the text runs for a page given its fragments.
    /// </summary>
    /// <param name="fragments">
    /// The fragment list, typically from
    /// <see cref="TextExtractor.ExtractFragments(Chuvadi.Pdf.Documents.PdfPage)"/>.
    /// </param>
    /// <returns>
    /// One <see cref="TextRun"/> per fragment, in content-stream order.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fragments"/> is null.
    /// </exception>
    public static IReadOnlyList<TextRun> BuildFromFragments(
        IReadOnlyList<TextFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);

        if (fragments.Count == 0)
        {
            return Array.Empty<TextRun>();
        }

        List<TextRun> runs = new List<TextRun>(fragments.Count);

        for (int i = 0; i < fragments.Count; i++)
        {
            TextFragment frag = fragments[i];
            runs.Add(BuildOneRun(frag, i));
        }

        return runs;
    }

    private static TextRun BuildOneRun(TextFragment frag, int index)
    {
        string text = frag.Text ?? string.Empty;
        double avgAdvance = frag.FontSize * AverageAdvanceFraction;
        double width = text.Length * avgAdvance;
        double height = frag.FontSize;

        // PDF user space, Y up: the fragment's (X, Y) marks the baseline
        // origin of the leftmost glyph. The bounding box extends right
        // by `width` and vertically from `Y - 0.2 * fontSize` (descender)
        // to `Y + 0.8 * fontSize` (ascender). The 0.2/0.8 split tracks
        // the typical em-box partition of the Standard 14 fonts.
        double rectX = frag.X;
        double rectY = frag.Y - height * 0.2;
        RectangleF bbox = new RectangleF(
            (float)rectX,
            (float)rectY,
            (float)width,
            (float)height);

        TextDirection direction = InferDirection(text);
        IReadOnlyList<GlyphPosition> glyphs = LayOutGlyphs(text, frag, avgAdvance);

        return new TextRun(
            text,
            bbox,
            frag.FontSize,
            direction,
            glyphs,
            readingOrderIndex: index);
    }

    private static IReadOnlyList<GlyphPosition> LayOutGlyphs(
        string text, TextFragment frag, double avgAdvance)
    {
        if (text.Length == 0)
        {
            return Array.Empty<GlyphPosition>();
        }

        List<GlyphPosition> result = new List<GlyphPosition>(text.Length);
        double x = frag.X;
        double y = frag.Y;
        int idx = 0;

        while (idx < text.Length)
        {
            int cp;

            if (char.IsHighSurrogate(text[idx]) &&
                idx + 1 < text.Length &&
                char.IsLowSurrogate(text[idx + 1]))
            {
                cp = char.ConvertToUtf32(text[idx], text[idx + 1]);
                idx += 2;
            }
            else
            {
                cp = text[idx];
                idx++;
            }

            result.Add(new GlyphPosition(x, y, avgAdvance, cp));
            x += avgAdvance;
        }

        return result;
    }

    private static TextDirection InferDirection(string text)
    {
        int rtl = 0;
        int ltr = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // Hebrew: U+0590..U+05FF, U+FB1D..U+FB4F
            if ((c >= 0x0590 && c <= 0x05FF) ||
                (c >= 0xFB1D && c <= 0xFB4F))
            {
                rtl++;
                continue;
            }

            // Arabic blocks: U+0600..U+06FF, U+0750..U+077F, U+08A0..U+08FF,
            //                U+FB50..U+FDFF, U+FE70..U+FEFF
            if ((c >= 0x0600 && c <= 0x06FF) ||
                (c >= 0x0750 && c <= 0x077F) ||
                (c >= 0x08A0 && c <= 0x08FF) ||
                (c >= 0xFB50 && c <= 0xFDFF) ||
                (c >= 0xFE70 && c <= 0xFEFF))
            {
                rtl++;
                continue;
            }

            // Strong LTR scripts: Latin, Cyrillic, Greek, Armenian, Georgian,
            // Indic, Southeast Asian, CJK. A coarse but reliable proxy is
            // "is a letter that's not in the RTL ranges above".
            if (char.IsLetter(c))
            {
                ltr++;
            }
        }

        return rtl > ltr ? TextDirection.RightToLeft : TextDirection.LeftToRight;
    }
}
