// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Rasterizer fixes found while building 3.25 (each was drawn wrongly before):
//   - dash patterns (d) were ignored: every dashed line was solid;
//   - line widths and dashes ignored the CTM scale (a scaled page drew lines
//     many times too thick);
//   - a closed right triangle used as a clip was taken for its bounding box;
//   - constant opacity (/ca) was composited in linear light, so 50% red over
//     white came out (255, 188, 188) instead of (255, 128, 128);
//   - a CIDFontType2's /CIDToGIDMap stream was ignored (CID used as GID).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class RasterConformance325Tests
{
    private static readonly double[] TenTen = { 10, 10 };
    private static readonly double[] Ten = { 10 };

    // ── Dashes ───────────────────────────────────────────────────────────

    [Fact]
    public void StrokeExpander_SplitsALineIntoItsDashes()
    {
        List<List<PointF>> line = new List<List<PointF>> { new List<PointF> { new PointF(0, 0), new PointF(100, 0) } };

        List<List<PointF>> dashes = new StrokeExpander().Expand(line, new StrokeStyle { Width = 2, DashPattern = TenTen });
        List<List<PointF>> phased = new StrokeExpander().Expand(line, new StrokeStyle { Width = 2, DashPattern = TenTen, DashOffset = 5 });
        List<List<PointF>> odd = new StrokeExpander().Expand(line, new StrokeStyle { Width = 2, DashPattern = Ten });
        List<List<PointF>> solid = new StrokeExpander().Expand(line, new StrokeStyle { Width = 2 });

        dashes.Should().HaveCount(5, "0-10, 20-30, 40-50, 60-70, 80-90");
        XSpan(dashes[1]).Should().Be((20, 30));
        phased.Should().HaveCount(6, "0-5, 15-25, 35-45, 55-65, 75-85, 95-100: the phase starts mid-dash");
        XSpan(phased[0]).Should().Be((0, 5));
        odd.Should().HaveCount(5, "a one-element pattern alternates on and off");
        solid.Should().ContainSingle();
    }

    [Fact]
    public void DashedLine_RendersWithGaps()
    {
        PixelBuffer px = Render("0 0 0 RG 4 w [10 10] 0 d 0 300 m 200 300 l S");

        Dark(px, 5, 100).Should().BeTrue("first dash");
        Dark(px, 15, 100).Should().BeFalse("first gap");
        Dark(px, 25, 100).Should().BeTrue("second dash");
    }

    [Fact]
    public void LineWidthAndDashes_ScaleWithTheCtm()
    {
        // Under "0.1 0 0 0.1 0 0 cm" a 40-unit line is 4 pt wide and 100-unit
        // dashes are 10 pt (Ghostscript writes pages this way). Both used to
        // be taken in page units: a 40 pt solid bar.
        PixelBuffer px = Render("0.1 0 0 0.1 0 0 cm 0 0 0 RG 40 w [100 100] 0 d 0 3000 m 2000 3000 l S");

        Dark(px, 5, 100).Should().BeTrue("inside the 4 pt line");
        Dark(px, 5, 96).Should().BeFalse("2 pt above the line's edge");
        Dark(px, 15, 100).Should().BeFalse("in the first 10 pt gap");
        Dark(px, 25, 100).Should().BeTrue("in the second dash");
    }

    [Theory]
    [InlineData(1, 234)]
    [InlineData(2, 312)]
    [InlineData(0, 0)]
    public void ZeroLengthDashes_DrawDotsWithRoundAndSquareCaps(int cap, int expectedDarkPixels)
    {
        // "[0 10]" with round or square caps is a dotted line (Office writes
        // dotted borders this way); butt caps draw nothing. The counts match
        // MuPDF's rendering of the same line.
        PixelBuffer px = Render("4 w " + cap.ToString(CultureInfo.InvariantCulture) + " J [0 10] 0 d 0 300 m 200 300 l S");

        CountDark(px).Should().BeCloseTo(expectedDarkPixels, 6);
    }

    [Theory]
    [InlineData(0, 2000)]
    [InlineData(1, 2304)]
    [InlineData(2, 2400)]
    public void LineCaps_MatchMuPdf(int cap, int expectedDarkPixels)
    {
        // A 100 pt line, 20 pt wide: butt 100 x 20; round adds two half
        // discs; square adds 10 pt at each end (the start used to be cut by
        // 10 pt instead).
        PixelBuffer px = Render("20 w " + cap.ToString(CultureInfo.InvariantCulture) + " J 50 200 m 150 200 l S");

        CountDark(px).Should().BeCloseTo(expectedDarkPixels, 12);
    }

    [Fact]
    public void TinyDashes_AreCappedForTheWholeStroke()
    {
        // 300 sub-paths of 0.001-unit dashes would be millions of polygons.
        List<List<PointF>> lines = new List<List<PointF>>();
        for (int i = 0; i < 300; i++)
        {
            lines.Add(new List<PointF> { new PointF(0, i), new PointF(1000, i) });
        }

        List<List<PointF>> outline = new StrokeExpander().Expand(lines, new StrokeStyle { Width = 1, DashPattern = new[] { 0.001, 0.001 } });

        outline.Count.Should().BeLessThan(110_000, "past the budget the remaining sub-paths are drawn solid");
    }

    // ── Clipping ─────────────────────────────────────────────────────────

    [Fact]
    public void ClosedRightTriangleClip_IsNotItsBoundingBox()
    {
        List<PointF> triangle = new List<PointF>
        {
            new PointF(0, 0), new PointF(100, 0), new PointF(0, 100), new PointF(0, 0),
        };

        ClipRegion region = ClipRegion.Build(
            new List<List<List<PointF>>> { new List<List<PointF>> { triangle } },
            new List<FillRule> { FillRule.NonZeroWinding })!;

        List<(double Start, double End)> row = region.AllowedIntervals(90);
        row.Should().ContainSingle();
        row[0].End.Should().BeApproximately(10, 0.5, "at y = 90 the triangle is 10 wide");
    }

    [Fact]
    public void TriangleClip_Renders()
    {
        PixelBuffer px = Render("100 300 m 200 300 l 100 200 l h W n 1 0 0 rg 0 0 400 400 re f");

        px.GetPixelBgra(110, 110).R.Should().Be(255);
        px.GetPixelBgra(180, 180).G.Should().Be(255, "outside the clip stays white");
    }

    [Fact]
    public void BowTieClip_IsNotARectangle()
    {
        // Four box corners in "Z" order enclose two triangles, not the box.
        List<PointF> bowTie = new List<PointF>
        {
            new PointF(0, 0), new PointF(100, 0), new PointF(0, 100), new PointF(100, 100), new PointF(0, 0),
        };

        ClipRegion region = ClipRegion.Build(
            new List<List<List<PointF>>> { new List<List<PointF>> { bowTie } },
            new List<FillRule> { FillRule.NonZeroWinding })!;

        List<(double Start, double End)> row = region.AllowedIntervals(50);
        double covered = 0;
        foreach ((double start, double end) in row)
        {
            covered += end - start;
        }

        covered.Should().BeLessThan(10, "at mid-height the bow-tie narrows to its crossing point");
    }

    // ── Constant opacity ─────────────────────────────────────────────────

    [Fact]
    public void HalfOpacity_CompositesInSrgb()
    {
        PixelBuffer px = Render("/G0 gs 1 0 0 rg 0 0 100 100 re f", "/ExtGState<</G0<</ca 0.5>>>>");

        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(50, 350);
        r.Should().Be(255);
        g.Should().BeInRange(126, 129);
        b.Should().BeInRange(126, 129);
    }

    // ── CIDToGIDMap ──────────────────────────────────────────────────────

    [Fact]
    public void CidFontType2_HonoursTheCidToGidMapStream()
    {
        // CID 1 -> glyph 4 ('H'), CID 2 -> glyph 2 ('A'). Showing CID 1 must
        // draw H: its right bar (x 158-168 at 100 pt from x = 110) is inked.
        byte[] font = File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "ChuvadiTest-Glyf.ttf"));
        string map = Encoding.Latin1.GetString(new byte[] { 0, 0, 0, 4, 0, 2 });
        PixelBuffer px = Render(
            "BT /F1 100 Tf 110 250 Td <0001> Tj ET",
            "/Font<</F1 5 0 R>>",
            "<</Type/Font/Subtype/Type0/BaseFont/T/Encoding/Identity-H/DescendantFonts[6 0 R]>>",
            "<</Type/Font/Subtype/CIDFontType2/BaseFont/T/CIDSystemInfo<</Registry(Adobe)/Ordering(Identity)/Supplement 0>>"
                + "/FontDescriptor 7 0 R/CIDToGIDMap 8 0 R/DW 1000>>",
            "<</Type/FontDescriptor/FontName/T/Flags 4/FontBBox[0 -250 1300 900]/ItalicAngle 0/Ascent 850/Descent -250"
                + "/CapHeight 700/StemV 80/FontFile2 9 0 R>>",
            Stream(map),
            Stream(Encoding.Latin1.GetString(font)));

        Dark(px, 163, 120).Should().BeTrue("CID 1 is H, whose right bar is here");
        Dark(px, 115, 120).Should().BeFalse("A's left bar would start at x 115; H's starts at 116");
    }

    private static (double, double) XSpan(List<PointF> outline)
    {
        double min = double.MaxValue;
        double max = double.MinValue;
        foreach (PointF p in outline)
        {
            min = Math.Min(min, p.X);
            max = Math.Max(max, p.X);
        }

        return (Math.Round(min, 6), Math.Round(max, 6));
    }

    private static int CountDark(PixelBuffer px)
    {
        int dark = 0;
        for (int y = 0; y < px.Height; y++)
        {
            for (int x = 0; x < px.Width; x++)
            {
                if (px.GetPixelBgra(x, y).R < 128)
                {
                    dark++;
                }
            }
        }

        return dark;
    }

    private static bool Dark(PixelBuffer px, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(x, y);
        return r < 100 && g < 100 && b < 100;
    }

    private static string Stream(string data) =>
        "<</Length " + data.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + data + "\nendstream";

    // A 400 x 400 pt page with the content and resources; extra objects
    // are numbered from 5.
    private static PixelBuffer Render(string content, string resources = "", params string[] extra)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<<" + resources + ">>/Contents 4 0 R>>",
            Stream(content),
        };
        bodies.AddRange(extra);

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture)).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer <</Size ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture))
            .Append("/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(sb.ToString())), leaveOpen: false);
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);
    }
}
