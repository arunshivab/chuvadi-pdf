// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for v3.19 pattern completeness: pattern-coloured strokes in the
// raster pipeline, and pattern space inside form XObjects.

using System.IO;
using System.Linq;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Rendering.DisplayList;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class PatternCompletenessTests
{
    private const string Page = "/MediaBox[0 0 200 200]/Resources<</Pattern<</P0 5 0 R>>>>";

    [Fact]
    public void RasterStroke_WithColouredTilingPattern_PaintsTheStrokeArea()
    {
        // A solid red 10x10 cell; a 20pt-wide horizontal stroke at y=100.
        byte[] pdf = RawPdf.SinglePage(
            Page,
            "/Pattern CS /P0 SCN 20 w 20 100 m 180 100 l S",
            SolidCellPattern(paintType: 1, "1 0 0 rg 0 0 10 10 re f"));

        PixelBuffer pixels = Rasterize(pdf);

        IsRed(pixels, 100, 100).Should().BeTrue("the stroke centre shows the pattern");
        IsRed(pixels, 100, 93).Should().BeTrue("7pt off-axis is inside the 20pt stroke");
        IsWhite(pixels, 100, 85).Should().BeTrue("15pt off-axis is outside the stroke");
        IsWhite(pixels, 10, 100).Should().BeTrue("the stroke's butt cap ends at x=20");
    }

    [Fact]
    public void RasterStroke_WithUncolouredPattern_UsesTheScnColour()
    {
        byte[] pdf = RawPdf.SinglePage(
            "/MediaBox[0 0 200 200]/Resources<</Pattern<</P0 5 0 R>>/ColorSpace<</CS0[/Pattern/DeviceRGB]>>>>",
            "/CS0 CS 0 0 1 /P0 SCN 20 w 20 100 m 180 100 l S",
            SolidCellPattern(paintType: 2, "1 0 0 rg 0 0 10 10 re f"));

        PixelBuffer pixels = Rasterize(pdf);
        (byte b, byte g, byte r, byte _) = pixels.GetPixelBgra(100, 100);
        b.Should().BeGreaterThan(200, "the uncoloured cell paints in the SCN colour (blue)");
        r.Should().BeLessThan(60);
        g.Should().BeLessThan(60);
    }

    [Fact]
    public void RasterStroke_PatternThenFlatColour_StrokesFlat()
    {
        byte[] pdf = RawPdf.SinglePage(
            Page,
            "/Pattern CS /P0 SCN 0 1 0 RG 20 w 20 100 m 180 100 l S",
            SolidCellPattern(paintType: 1, "1 0 0 rg 0 0 10 10 re f"));

        (byte b, byte g, byte r, byte _) = Rasterize(pdf).GetPixelBgra(100, 100);
        g.Should().BeGreaterThan(200, "a later RG replaces the stroke pattern with flat green");
        r.Should().BeLessThan(60);
        b.Should().BeLessThan(60);
    }

    [Fact]
    public void RasterFillAndStroke_WithBothPatterns_PaintsBoth()
    {
        byte[] pdf = RawPdf.SinglePage(
            Page,
            "/Pattern cs /P0 scn /Pattern CS /P0 SCN 10 w 50 50 100 100 re B",
            SolidCellPattern(paintType: 1, "1 0 0 rg 0 0 10 10 re f"));

        PixelBuffer pixels = Rasterize(pdf);
        IsRed(pixels, 100, 100).Should().BeTrue("the interior is pattern-filled");
        IsRed(pixels, 100, 47).Should().BeTrue("the outline band outside the rectangle is pattern-stroked");
    }

    [Fact]
    public void TilingPatternInsideScaledForm_UsesTheFormSpace()
    {
        // The form scales by 2 and translates by (10,10); its pattern's tile
        // (0,0) must land at the form origin with the form scale.
        byte[] form = RawPdf.Stream(
            "/Type/XObject/Subtype/Form/BBox[0 0 100 100]/Matrix[2 0 0 2 10 10]/Resources<</Pattern<</P0 6 0 R>>>>",
            "/Pattern cs /P0 scn 0 0 40 40 re f");
        byte[] pdf = RawPdf.SinglePage(
            "/MediaBox[0 0 200 200]/Resources<</XObject<</Fm 5 0 R>>>>",
            "/Fm Do",
            form,
            SolidCellPattern(paintType: 1, "1 0 0 rg 0 0 10 10 re f"));

        using PdfDocument document = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        PageDisplayList list = document.BuildDisplayList(0);
        TilingPatternOp op = list.OfType<TilingPatternOp>().Single();

        op.Tiles.Should().Contain(new AffineMatrix(2, 0, 0, 2, 10, 10),
            "pattern space maps through the form's matrix (§8.7.2)");
        op.Tiles.Should().Contain(new AffineMatrix(2, 0, 0, 2, 50, 50),
            "tile (1,1) is one 20-unit step along both axes, scaled by 2");
    }

    private static byte[] SolidCellPattern(int paintType, string cell)
    {
        return RawPdf.Stream(
            "/Type/Pattern/PatternType 1/PaintType " + paintType
            + "/TilingType 1/BBox[0 0 10 10]/XStep 10/YStep 10/Resources<<>>",
            cell);
    }

    private static PixelBuffer Rasterize(byte[] pdf)
    {
        using PdfDocument document = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        PageRasterizer rasterizer = new PageRasterizer(document.Objects, new RenderOptions { Dpi = 72 });
        return rasterizer.Rasterize(document.Pages[0]);
    }

    private static bool IsRed(PixelBuffer p, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = p.GetPixelBgra(x, y);
        return r > 200 && g < 80 && b < 80;
    }

    private static bool IsWhite(PixelBuffer p, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = p.GetPixelBgra(x, y);
        return r > 240 && g > 240 && b > 240;
    }
}
