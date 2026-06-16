// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10.1 (form XObjects), §9.4 (text), §8.3.3 (CTM)
// PHASE: Document operations — running headers and footers.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Adds running headers and/or footers to a document, with three strategies for
/// how the bands interact with existing content (see <see cref="PageContentFit"/>):
/// overlay in the margins, always reserve-and-scale, or scale only when content
/// intrudes. Header/footer text supports the same tokens as
/// <see cref="TextStamper"/> (page numbers in several styles, file name/path,
/// caller-supplied date/time, custom text), each with independent left/centre/
/// right segments.
/// </summary>
public static class HeaderFooter
{
    /// <summary>
    /// Applies a header and/or footer to <paramref name="document"/>.
    /// </summary>
    /// <param name="output">The stream to write the updated PDF to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="options">Header/footer content and layout options.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any argument is null.
    /// </exception>
    public static void Apply(Stream output, PdfDocument document, HeaderFooterOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        int total = document.PageCount;
        HashSet<int> targets = ResolveTargets(options.PageIndices, total);

        bool anyScaling = options.Fit != PageContentFit.Overlay;

        if (!anyScaling)
        {
            ApplyOverlay(output, document, options, targets, total);
            return;
        }

        ApplyWithReflow(output, document, options, targets, total);
    }

    // Overlay model: draw header/footer in margins via the stamp writer; no
    // content movement.
    private static void ApplyOverlay(
        Stream output,
        PdfDocument document,
        HeaderFooterOptions options,
        HashSet<int> targets,
        int total)
    {
        StampWriter writer = new StampWriter(document);

        for (int pageIndex = 0; pageIndex < total; pageIndex++)
        {
            if (!targets.Contains(pageIndex))
            {
                continue;
            }

            PdfPage page = document.Pages[pageIndex];
            string fragment = BuildBandFragments(options, page, pageIndex, total);

            if (fragment.Length == 0)
            {
                continue;
            }

            byte[] bytes = Encoding.Latin1.GetBytes("q\n" + fragment + "Q\n");
            writer.AddOverlay(pageIndex, bytes);
        }

        writer.Write(output);
    }

    // Reserve-and-scale (and scale-if-intruding): shrink content to free bands,
    // then draw header/footer in the freed space.
    private static void ApplyWithReflow(
        Stream output,
        PdfDocument document,
        HeaderFooterOptions options,
        HashSet<int> targets,
        int total)
    {
        PageContentEditor editor = new PageContentEditor(document);

        for (int pageIndex = 0; pageIndex < total; pageIndex++)
        {
            if (!targets.Contains(pageIndex))
            {
                continue;
            }

            PdfPage page = document.Pages[pageIndex];
            PdfRectangle mediaBox = page.MediaBox;
            double pageH = mediaBox.Height;

            double headerBand = options.Header is null ? 0.0 : options.HeaderHeight;
            double footerBand = options.Footer is null ? 0.0 : options.FooterHeight;

            bool scale = options.Fit == PageContentFit.ReserveAndScale
                || (options.Fit == PageContentFit.ScaleIfIntruding
                    && ContentIntrudes(document, page, headerBand, footerBand));

            Transform contentTransform = Transform.Identity;
            if (scale && (headerBand > 0 || footerBand > 0) && pageH > headerBand + footerBand)
            {
                double available = pageH - headerBand - footerBand;
                double factor = available / pageH;

                // Scale about the page origin, then lift content above the
                // footer band: x' = x*f, y' = y*f + footerBand.
                contentTransform = new Transform(
                    factor, 0, 0, factor,
                    mediaBox.X1 * (1 - factor),
                    (mediaBox.Y1 * factor) + footerBand);
            }

            string fragment = BuildBandFragments(options, page, pageIndex, total);

            List<byte[]> overlays = new List<byte[]>();
            if (fragment.Length > 0)
            {
                overlays.Add(Encoding.Latin1.GetBytes("q\n" + fragment + "Q\n"));
            }

            editor.TransformAndOverlay(pageIndex, contentTransform, options.Background, overlays);
            editor.AddOverlayFontResource(pageIndex, StampText.FontResourceName, StampText.BuildHelveticaFont());
        }

        editor.Write(output);
    }

    // Builds the text-show fragment for header and footer segments on one page.
    private static string BuildBandFragments(
        HeaderFooterOptions options,
        PdfPage page,
        int pageIndex,
        int total)
    {
        PdfRectangle mediaBox = page.MediaBox;
        StampContext ctx = new StampContext(
            pageIndex + 1, total, options.FilePath, options.Timestamp);

        StringBuilder sb = new StringBuilder();

        if (options.Header is BandText header)
        {
            double y = mediaBox.Y2 - options.HeaderHeight + options.HeaderBaselineOffset;
            AppendSegments(sb, header, ctx, mediaBox, y, options);
        }

        if (options.Footer is BandText footer)
        {
            double y = mediaBox.Y1 + options.FooterBaselineOffset;
            AppendSegments(sb, footer, ctx, mediaBox, y, options);
        }

        return sb.ToString();
    }

    private static void AppendSegments(
        StringBuilder sb,
        BandText band,
        StampContext ctx,
        PdfRectangle mediaBox,
        double baselineY,
        HeaderFooterOptions options)
    {
        AppendOne(sb, band.Left, ctx, mediaBox, baselineY, options, Align.Left);
        AppendOne(sb, band.Center, ctx, mediaBox, baselineY, options, Align.Center);
        AppendOne(sb, band.Right, ctx, mediaBox, baselineY, options, Align.Right);
    }

    private static void AppendOne(
        StringBuilder sb,
        string? template,
        StampContext ctx,
        PdfRectangle mediaBox,
        double baselineY,
        HeaderFooterOptions options,
        Align align)
    {
        if (template is null)
        {
            return;
        }

        string text = StampTokens.Resolve(template, ctx);
        if (text.Length == 0)
        {
            return;
        }

        double width = StampText.MeasureWidth(text, options.FontSize);
        double x = align switch
        {
            Align.Left => mediaBox.X1 + options.MarginX,
            Align.Center => mediaBox.X1 + (mediaBox.Width - width) / 2.0,
            _ => mediaBox.X2 - options.MarginX - width,
        };

        Transform placement = new Transform(1, 0, 0, 1, x, baselineY);
        sb.Append(StampText.BuildShowText(text, placement, options.FontSize, options.Color));
    }

    // Heuristic intrusion probe: scans the page's content-stream numbers paired
    // with y-affecting operators. Returns true if any positioning lands inside a
    // band. Deliberately conservative and dependency-free; ReserveAndScale is
    // the deterministic option for callers that need certainty.
    private static bool ContentIntrudes(
        PdfDocument document, PdfPage page, double headerBand, double footerBand)
    {
        double pageH = page.MediaBox.Height;
        double topThreshold = pageH - headerBand;
        double bottomThreshold = footerBand;

        byte[] content = ObjectImporter.ConcatenatePageContent(page, document.Objects);
        string text = Encoding.Latin1.GetString(content);

        // Token scan: collect numeric operands; when a y-bearing operator
        // (Td, TD, Tm, re, m, l, cm) appears, test the most recent y operand.
        string[] tokens = text.Split(
            new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        List<double> nums = new List<double>();
        foreach (string tok in tokens)
        {
            if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
            {
                nums.Add(n);
                if (nums.Count > 6)
                {
                    nums.RemoveAt(0);
                }

                continue;
            }

            double? y = tok switch
            {
                "Td" or "TD" or "l" or "m" => nums.Count >= 1 ? nums[^1] : null,
                "re" => nums.Count >= 3 ? nums[^3] : null,
                "Tm" or "cm" => nums.Count >= 1 ? nums[^1] : null,
                _ => null,
            };

            if (y is double yy && (yy > topThreshold || yy < bottomThreshold))
            {
                return true;
            }

            nums.Clear();
        }

        return false;
    }

    private static HashSet<int> ResolveTargets(IReadOnlyList<int>? pageIndices, int pageCount)
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

    private enum Align
    {
        Left,
        Center,
        Right,
    }
}
