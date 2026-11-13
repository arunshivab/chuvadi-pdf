// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.4 (image space and the unit square)
// PHASE: 3.34.0 (#50, D-348) — images drawn through their full matrix.

using System;
using System.Globalization;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class ImagePlacement3340Tests
{
    // A 2 x 2 image: top row red, blue; bottom row green, black.
    private static readonly byte[] Samples = { 255, 0, 0, 0, 0, 255, 0, 255, 0, 0, 0, 0 };

    [Theory]
    // matrix (a b c d e f), then the expected colour at four device points
    // (x, y in a 200 x 200 page at 72 dpi, y down) for each image quadrant.
    // The expected colours are what Poppler (pdftoppm) draws for each matrix.
    [InlineData("100 0 0 100 50 50", "R@75,75 B@125,75 G@75,125 K@125,125")]   // upright (unchanged path)
    [InlineData("0 100 -100 0 150 50", "B@75,75 K@125,75 R@75,125 G@125,125")]  // rotated 90 degrees anticlockwise
    [InlineData("-100 0 0 -100 150 150", "K@75,75 G@125,75 B@75,125 R@125,125")] // rotated 180 degrees
    [InlineData("-100 0 0 100 150 50", "B@75,75 R@125,75 K@75,125 G@125,125")]  // mirrored left-right
    [InlineData("100 0 0 -100 50 150", "G@75,75 K@125,75 R@75,125 B@125,125")]  // flipped top-bottom
    public void Image_IsDrawnThroughItsMatrix(string matrix, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        using PdfDocument doc = Page("q " + matrix + " cm /Im1 Do Q");
        PixelBuffer px = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);

        foreach (string probe in expected.Split(' '))
        {
            string[] parts = probe.Split('@', ',');
            int x = int.Parse(parts[1], CultureInfo.InvariantCulture);
            int y = int.Parse(parts[2], CultureInfo.InvariantCulture);
            (byte b, byte g, byte r, byte _) = px.GetPixelBgra(x, y);
            Colour(r, g, b).Should().Be(parts[0], "at ({0}, {1}) for matrix {2}", x, y, matrix);
        }
    }

    [Fact]
    public void SkewedImage_FillsItsParallelogram_NotItsBoundingBox()
    {
        // Unit square to (50,50) (150,50) (200,150) (100,150): a skew in x.
        using PdfDocument doc = Page("q 100 0 50 100 50 50 cm /Im1 Do Q");
        PixelBuffer px = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);

        // Device y = 200 - page y. Inside near the bottom-left corner (page 60,55):
        Colour(px, 60, 145).Should().NotBe("W");
        // Outside the parallelogram but inside its bounding box (page 60,140):
        Colour(px, 60, 60).Should().Be("W");
        // Inside near the top-right corner (page 190,145):
        Colour(px, 190, 55).Should().NotBe("W");
    }

    private static string Colour(PixelBuffer px, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = px.GetPixelBgra(x, y);
        return Colour(r, g, b);
    }

    private static string Colour(byte r, byte g, byte b) =>
        (r > 200, g > 200, b > 200) switch
        {
            (true, false, false) => "R",
            (false, true, false) => "G",
            (false, false, true) => "B",
            (true, true, true) => "W",
            (false, false, false) => "K",
            _ => FormattableString.Invariant($"?{r},{g},{b}"),
        };

    private static PdfDocument Page(string content)
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int image = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 2/Height 2/ColorSpace/DeviceRGB/BitsPerComponent 8>>", Samples);
        int contents = pdf.AddStream("<<>>", content);
        int page = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources<</XObject<</Im1 " + N(image) + " 0 R>>>>/Contents " + N(contents) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        return TestPdf.Open(pdf.Build(catalog));
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
