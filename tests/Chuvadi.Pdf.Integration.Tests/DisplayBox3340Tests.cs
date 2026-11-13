// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.11.2 (page boundaries), §7.7.3.3 Table 30 (/Rotate)
// PHASE: 3.34.0 (D-334, D-335 item 5) — PdfPage.GetDisplayBox,
//        PdfPage.GetDisplayTransform, PageRasterizer.GetOutputSize.

using System.Collections.Generic;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class DisplayBox3340Tests
{
    private const string NoResources = "<<>>";

    [Fact]
    public void GetDisplayBox_CropIntersectedWithMedia()
    {
        using PdfDocument doc = Page("/CropBox[150 20 250 120]");

        PdfRectangle crop = doc.Pages[0].GetDisplayBox(PdfDisplayBox.CropBox);
        (crop.X1, crop.Y1, crop.X2, crop.Y2).Should().Be((150.0, 20.0, 200.0, 120.0));
        PdfRectangle media = doc.Pages[0].GetDisplayBox(PdfDisplayBox.MediaBox);
        (media.X1, media.Y1, media.X2, media.Y2).Should().Be((0.0, 0.0, 200.0, 200.0));
    }

    [Fact]
    public void GetDisplayBox_NoOverlap_IsTheMediaBox_AndReversedBoxesAreNormalised()
    {
        using PdfDocument apart = Page("/CropBox[300 300 400 400]");
        apart.Pages[0].GetDisplayBox(PdfDisplayBox.CropBox).X2.Should().Be(200);

        using PdfDocument reversed = Page("/CropBox[120 120 20 20]");
        PdfRectangle r = reversed.Pages[0].GetDisplayBox(PdfDisplayBox.CropBox);
        (r.X1, r.Y1, r.X2, r.Y2).Should().Be((20.0, 20.0, 120.0, 120.0));
    }

    public static IEnumerable<object[]> OutputSizeCases() => new List<object[]>
    {
        new object[] { string.Empty, new RenderOptions { Dpi = 72 } },
        new object[] { "/CropBox[10 20 110 70]", new RenderOptions { Dpi = 150 } },
        new object[] { "/CropBox[10 20 110 70]/Rotate 90", new RenderOptions { Dpi = 100, ApplyPageRotation = true } },
        new object[] { "/CropBox[10 20 110 70]/Rotate 90", new RenderOptions { Dpi = 100 } },
        new object[] { "/CropBox[10 20 110 70]", new RenderOptions { Dpi = 96, Rotation = 270, SuperSample = 2 } },
        new object[] { "/CropBox[10 20 110 70]", new RenderOptions { Dpi = 72, DisplayBox = PdfDisplayBox.MediaBox } },
    };

    [Theory]
    [MemberData(nameof(OutputSizeCases))]
    public void GetOutputSize_IsTheRenderedSize(string extraPage, RenderOptions options)
    {
        using PdfDocument doc = Page(extraPage);
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, options);

        PixelBuffer rendered = rasterizer.Rasterize(doc.Pages[0]);

        rasterizer.GetOutputSize(doc.Pages[0]).Should().Be((rendered.Width, rendered.Height));
    }

    [Theory]
    [InlineData(0, 10, 20, 110, 70)]
    [InlineData(90, 110, 20, 10, 70)]
    [InlineData(180, 110, 70, 10, 20)]
    [InlineData(270, 10, 70, 110, 20)]
    public void GetDisplayTransform_MapsTheShownCornersToUserSpace(int rotate, double llx, double lly, double urx, double ury)
    {
        // CropBox 10 20 110 70 (100 x 50). Display lower-left (0, 0) and upper
        // right (display width, height) land on these user-space corners.
        using PdfDocument doc = Page("/CropBox[10 20 110 70]/Rotate " + rotate);
        PdfPage page = doc.Pages[0];
        Transform t = page.GetDisplayTransform(PdfDisplayBox.CropBox);
        (double w, double h) = rotate is 90 or 270 ? (50.0, 100.0) : (100.0, 50.0);

        PointF lowerLeft = t.TransformPoint(new PointF(0, 0));
        PointF upperRight = t.TransformPoint(new PointF(w, h));

        ((double)lowerLeft.X, (double)lowerLeft.Y).Should().Be((llx, lly));
        ((double)upperRight.X, (double)upperRight.Y).Should().Be((urx, ury));
    }

    [Fact]
    public void GetDisplayTransform_TopOfTheShownPage_IsWhereARotatedViewShowsIt()
    {
        // On a /Rotate 90 page, a dark square placed at the display's top-left
        // corner shows at the top-left of the rendered, rotated page.
        using PdfDocument blank = Page("/Rotate 90");
        Transform t = blank.Pages[0].GetDisplayTransform(PdfDisplayBox.CropBox);
        PointF a = t.TransformPoint(new PointF(0, 180));
        PointF b = t.TransformPoint(new PointF(20, 200));
        string square = "0 g "
            + System.FormattableString.Invariant($"{System.Math.Min(a.X, b.X)} {System.Math.Min(a.Y, b.Y)} {System.Math.Abs(b.X - a.X)} {System.Math.Abs(b.Y - a.Y)}")
            + " re f";
        using PdfDocument doc = TestPdf.Open(TestPdf.OnePage(square, NoResources, System.Array.Empty<string>(), extraPage: "/Rotate 90"));

        PixelBuffer shown = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72, ApplyPageRotation = true }).Rasterize(doc.Pages[0]);
        shown.GetPixelBgra(5, 5).B.Should().BeLessThan(50, "top-left of the shown page is dark");
        shown.GetPixelBgra(195, 195).B.Should().BeGreaterThan(200);
    }

    private static PdfDocument Page(string extraPage) =>
        TestPdf.Open(TestPdf.OnePage(string.Empty, NoResources, System.Array.Empty<string>(), extraPage: extraPage));
}
