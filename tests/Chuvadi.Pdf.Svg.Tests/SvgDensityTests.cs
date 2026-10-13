// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5 — Path construction and painting
// PHASE: Chuvadi.Pdf.Svg — density handling for dense vector pages.
//
// CAD exports and large plots can carry tens of thousands of path operations
// per page; emitted verbatim the SVG overwhelms viewers. Three levers:
// PathSimplifyTolerance (Ramer–Douglas–Peucker on straight-segment runs),
// MaxVectorOps (per-page vector-op budget), and DensityFallback (stay vector
// but simplify, or rasterize the whole page to a single <image>).

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class SvgDensityTests
{
    [Fact]
    public void PathSimplifyTolerance_CollapsesNearCollinearRuns()
    {
        byte[] pdf = BuildPdf(DensePolyline(pointCount: 60));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

        string plain = new SvgRenderer().RenderPage(doc, 0);
        string simplified = new SvgRenderer(new SvgExportOptions
        {
            PathSimplifyTolerance = 0.5,
        }).RenderPage(doc, 0);

        int plainLines = CountOccurrences(plain, "L ");
        int simplifiedLines = CountOccurrences(simplified, "L ");

        plainLines.Should().BeGreaterThanOrEqualTo(59, "every jittered point is emitted verbatim");
        simplifiedLines.Should().BeLessThan(5,
            "jitter below the tolerance collapses the run to its endpoints");
        simplified.Should().Contain("<path", "the output stays vector");
    }

    [Fact]
    public void MaxVectorOps_SimplifyFallback_ForcesAutoSimplification()
    {
        byte[] pdf = BuildPdf(ManyPolylines(pathCount: 20, pointsPerPath: 30));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

        string plain = new SvgRenderer().RenderPage(doc, 0);
        string budgeted = new SvgRenderer(new SvgExportOptions
        {
            MaxVectorOps = 5,
        }).RenderPage(doc, 0);

        CountOccurrences(budgeted, "L ").Should().BeLessThan(
            CountOccurrences(plain, "L "),
            "exceeding the budget under the Simplify fallback forces the automatic tolerance");
        budgeted.Should().Contain("<path", "the Simplify fallback stays vector");
    }

    [Fact]
    public void MaxVectorOps_RasterizeFallback_EmitsSingleImagePage()
    {
        byte[] pdf = BuildPdf(ManyPolylines(pathCount: 20, pointsPerPath: 10));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

        string svg = new SvgRenderer(new SvgExportOptions
        {
            MaxVectorOps = 5,
            DensityFallback = SvgDensityFallback.Rasterize,
            RasterFallbackDpi = 36,
        }).RenderPage(doc, 0);

        svg.Should().Contain("data:image/png;base64,",
            "the page rasterizes to a single embedded PNG");
        svg.Should().NotContain("<path",
            "no vector path elements survive the raster fallback");
    }

    [Fact]
    public void ConsecutiveSameStyleStrokes_MergeIntoOnePathElement()
    {
        // 20 stroked polylines sharing one style (CAD-export shape): batched
        // into a single <path> element so dense plots stay viewer-friendly.
        byte[] pdf = BuildPdf(ManyPolylines(pathCount: 20, pointsPerPath: 5));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        CountOccurrences(svg, "<path").Should().Be(1,
            "consecutive same-style strokes merge into one element");
        CountOccurrences(svg, "M ").Should().Be(20,
            "every polyline's geometry survives inside the merged element");
    }

    [Fact]
    public void DifferentStyleStrokes_DoNotMerge()
    {
        // Alternating stroke widths must stay separate elements, in order.
        string body = "1 w\n50 100 m\n400 100 l\nS\n2 w\n50 200 m\n400 200 l\nS\n";
        byte[] pdf = BuildPdf(body);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        CountOccurrences(svg, "<path").Should().Be(2,
            "a style change flushes the batch");
    }

    [Fact]
    public void UnderBudget_RasterizeFallbackStaysVector()
    {
        byte[] pdf = BuildPdf(ManyPolylines(pathCount: 3, pointsPerPath: 5));
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);

        string svg = new SvgRenderer(new SvgExportOptions
        {
            MaxVectorOps = 100,
            DensityFallback = SvgDensityFallback.Rasterize,
        }).RenderPage(doc, 0);

        svg.Should().Contain("<path", "a page under the budget renders fully vector");
        svg.Should().NotContain("data:image/png;base64,");
    }

    // A single stroked polyline along y≈500 whose points jitter by ±0.05 —
    // visually a straight line, emitted as pointCount-1 LineTo segments.
    private static string DensePolyline(int pointCount)
    {
        StringBuilder sb = new StringBuilder("50 500 m\n");
        for (int i = 1; i < pointCount; i++)
        {
            double x = 50 + (i * 350.0 / pointCount);
            double y = 500 + ((i % 2 == 0) ? 0.05 : -0.05);
            sb.Append(x.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ')
                .Append(y.ToString("0.###", CultureInfo.InvariantCulture)).Append(" l\n");
        }

        sb.Append("S\n");
        return sb.ToString();
    }

    private static string ManyPolylines(int pathCount, int pointsPerPath)
    {
        StringBuilder sb = new StringBuilder();
        for (int p = 0; p < pathCount; p++)
        {
            double baseY = 100 + (p * 30);
            sb.Append("50 ").Append(baseY.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" m\n");
            for (int i = 1; i < pointsPerPath; i++)
            {
                double x = 50 + (i * 400.0 / pointsPerPath);
                double y = baseY + ((i % 2 == 0) ? 0.05 : -0.05);
                sb.Append(x.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(y.ToString("0.###", CultureInfo.InvariantCulture)).Append(" l\n");
            }

            sb.Append("S\n");
        }

        return sb.ToString();
    }

    private static int CountOccurrences(string text, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(token, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static byte[] BuildPdf(string body)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.Contents, new PdfReference(contentId));
        page.Set(PdfName.Intern("Resources"), new PdfDictionary());

        byte[] content = Encoding.ASCII.GetBytes(body);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
