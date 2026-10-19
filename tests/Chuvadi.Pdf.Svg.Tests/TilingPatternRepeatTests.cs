// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.3 — Tiling patterns
// PHASE: 3.21 — fills needing more than 4,096 tiles are repeated (raster
//        bitmap tile, SVG <pattern>) instead of painting nothing; fills at
//        or below the threshold keep the exact per-tile path.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class TilingPatternRepeatTests
{
    [Fact]
    public void FineHatchOverTheWholePage_IsPainted_InRaster()
    {
        // A 4-point cell whose left half is black: 100 x 100 = 10,000 tiles.
        using PdfDocument doc = Open(PatternPdf("[0 0 4 4]", 4, 4, "0 0 2 4 re f", "0 0 400 400 re f"));

        PixelBuffer pixels = Rasterize(doc);

        Gray(pixels, 1, 200).Should().BeLessThan(60, "column 1 lies in a black stripe");
        Gray(pixels, 3, 200).Should().BeGreaterThan(200, "column 3 lies in a white stripe");
        Gray(pixels, 397, 5).Should().BeLessThan(60, "the stripes repeat to the far edge");
        Gray(pixels, 399, 5).Should().BeGreaterThan(200);
    }

    [Fact]
    public void FineHatchOverTheWholePage_IsPainted_InSvg()
    {
        using PdfDocument doc = Open(PatternPdf("[0 0 4 4]", 4, 4, "0 0 2 4 re f", "0 0 400 400 re f"));

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("<pattern id=\"tpat0\"").And.Contain("fill=\"url(#tpat0)\"");
        CountOf(svg, "<use ").Should().Be(9, "one cell reference per neighbouring offset inside the pattern");
    }

    [Fact]
    public void FillsAtOrBelowTheThreshold_KeepTheExactPerTilePath()
    {
        // 40-point cells over a 400-point page: 100 tiles.
        using PdfDocument doc = Open(PatternPdf("[0 0 40 40]", 40, 40, "0 0 20 40 re f", "0 0 400 400 re f"));

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().NotContain("<pattern");
        CountOf(svg, "<use ").Should().BeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public void RepeatedFill_StaysInsideTheFilledPath()
    {
        using PdfDocument doc = Open(PatternPdf("[0 0 1 1]", 1, 1, "0 0 0.5 1 re f", "100 100 100 100 re f"));

        PixelBuffer pixels = Rasterize(doc);

        Gray(pixels, 50, 50).Should().Be(255, "outside the filled square stays white");
        Gray(pixels, 300, 300).Should().Be(255);
    }

    [Fact]
    public void PeriodBelowAPixel_RendersAsTheCellsAverageTone()
    {
        // Half-black cell, 0.5-point period at 72 DPI: a mid-grey tone.
        using PdfDocument doc = Open(PatternPdf("[0 0 0.5 0.5]", 0.5, 0.5, "0 0 0.25 0.5 re f", "0 0 400 400 re f"));

        byte tone = Gray(Rasterize(doc), 200, 200);

        tone.Should().BeInRange(90, 210);
    }

    [Fact]
    public void AbsurdlyFineStep_DoesNotHang_AndPaints()
    {
        using PdfDocument doc = Open(PatternPdf("[0 0 0.000001 0.000001]", 0.000001, 0.000001,
            "0 0 0.000001 0.000001 re f", "0 0 400 400 re f"));

        Gray(Rasterize(doc), 200, 200).Should().BeLessThan(60, "a fully black cell averages to black");
        new SvgRenderer().RenderPage(doc, 0).Should().Contain("<pattern");
    }

    [Fact]
    public void RotatedPattern_RepeatsWithBothTones()
    {
        using PdfDocument doc = Open(PatternPdf("[0 0 4 4]", 4, 4, "0 0 2 4 re f", "0 0 400 400 re f",
            "/Matrix[0.7071 0.7071 -0.7071 0.7071 0 0]"));

        PixelBuffer pixels = Rasterize(doc);
        int dark = 0, light = 0;
        for (int x = 150; x < 170; x++)
        {
            byte v = Gray(pixels, x, 200);
            if (v < 80) { dark++; }
            if (v > 180) { light++; }
        }

        dark.Should().BeGreaterThan(0);
        light.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ThousandsOfRepeatedFills_StayWithinTheAreaBudget()
    {
        // 2,000 full-page fine-pattern fills: rendering stays bounded (fills
        // past the per-page budget are skipped) and the early fills paint.
        string fills = string.Concat(System.Linq.Enumerable.Repeat("0 0 400 400 re f ", 2000));
        using PdfDocument doc = Open(PatternPdf("[0 0 4 4]", 4, 4, "0 0 2 4 re f", fills));

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        PixelBuffer pixels = Rasterize(doc);
        watch.Stop();

        Gray(pixels, 1, 200).Should().BeLessThan(60);
        watch.Elapsed.TotalSeconds.Should().BeLessThan(30);
    }

    private static int CountOf(string text, string token)
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

    private static PixelBuffer Rasterize(PdfDocument doc) =>
        new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);

    // Green channel at device pixel (x, y), y measured from the top.
    private static byte Gray(PixelBuffer pixels, int x, int y) => pixels.GetPixelBgra(x, y).G;

    private static byte[] PatternPdf(
        string bbox, double xStep, double yStep, string cellContent, string fill, string matrix = "")
    {
        string step = "/XStep " + xStep.ToString("0.##########", CultureInfo.InvariantCulture)
            + "/YStep " + yStep.ToString("0.##########", CultureInfo.InvariantCulture);
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Pattern<</P0 5 0 R>>>>/Contents 4 0 R>>",
            Stream(string.Empty, "/Pattern cs /P0 scn " + fill),
            Stream("/PatternType 1/PaintType 1/TilingType 1/BBox" + bbox + step + matrix + "/Resources<<>>", cellContent),
        };

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer <</Size 6/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static string Stream(string entries, string content) =>
        "<<" + entries + "/Length " + Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture)
        + ">>\nstream\n" + content + "\nendstream";

    private static PdfDocument Open(byte[] pdf) => PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
}
