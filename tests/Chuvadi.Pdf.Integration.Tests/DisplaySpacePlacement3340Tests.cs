// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.11.2 (page boundaries), §7.7.3.3 Table 30 (/Rotate)
// PHASE: 3.34.0 (D-335 item 5) — PlaceInDisplaySpace on WatermarkStamper and
//        HeaderFooter, and the real watermark text width.

using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Operations;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Watermark;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class DisplaySpacePlacement3340Tests
{
    private const string NoResources = "<<>>";

    [Theory]
    [InlineData("WWWW")]
    [InlineData("iiiiiiii")]
    [InlineData("Mixed width text")]
    public void TextWatermark_IsCentredByItsRealWidth(string text)
    {
        using PdfDocument source = Page("/MediaBox[0 0 400 200]");
        TextWatermarkOptions options = new TextWatermarkOptions(text)
        {
            FontSize = 40,
            Color = ColorF.Black,
            Opacity = 1f,
            RotationDegrees = 0,
        };

        Box ink = Ink(Stamp(source, options), rotate: false);

        ((ink.Left + ink.Right) / 2.0).Should().BeApproximately(200, 4, "the text is centred on the page");
        ((ink.Top + ink.Bottom) / 2.0).Should().BeApproximately(100, 12);
    }

    [Fact]
    public void TextWatermark_InDisplaySpace_IsUprightAndCentredOnTheShownPage()
    {
        // MediaBox 300 x 500, CropBox [50 100 250 400] (200 x 300), /Rotate 90:
        // the viewer shows a 300 x 200 landscape page.
        using PdfDocument source = Page("/MediaBox[0 0 300 500]/CropBox[50 100 250 400]/Rotate 90");
        TextWatermarkOptions options = new TextWatermarkOptions("DRAFT COPY")
        {
            FontSize = 30,
            Color = ColorF.Black,
            Opacity = 1f,
            RotationDegrees = 0,
            PlaceInDisplaySpace = true,
        };

        Box ink = Ink(Stamp(source, options), rotate: true);

        ink.Width.Should().BeGreaterThan(ink.Height * 3, "the text reads left to right on the shown page");
        ((ink.Left + ink.Right) / 2.0).Should().BeApproximately(150, 4);
        ((ink.Top + ink.Bottom) / 2.0).Should().BeApproximately(100, 10);
    }

    [Fact]
    public void TextWatermark_ByDefault_KeepsTheHistoricalUnrotatedPlacement()
    {
        using PdfDocument source = Page("/MediaBox[0 0 300 500]/Rotate 90");
        TextWatermarkOptions options = new TextWatermarkOptions("DRAFT COPY")
        {
            FontSize = 30,
            Color = ColorF.Black,
            Opacity = 1f,
            RotationDegrees = 0,
        };

        Box ink = Ink(Stamp(source, options), rotate: true);

        ink.Height.Should().BeGreaterThan(ink.Width * 3, "unchanged: the text runs along the MediaBox, shown sideways");
    }

    [Fact]
    public void Footer_InDisplaySpace_SitsAtTheBottomOfTheShownPage()
    {
        using PdfDocument source = Page("/MediaBox[0 0 300 500]/CropBox[50 100 250 400]/Rotate 270");
        HeaderFooterOptions options = new HeaderFooterOptions
        {
            Footer = new BandText(center: "Page {page} of {total}"),
            FontSize = 14,
            PlaceInDisplaySpace = true,
        };

        byte[] stamped;
        using (MemoryStream output = new MemoryStream())
        {
            HeaderFooter.Apply(output, source, options);
            stamped = output.ToArray();
        }

        Box ink = Ink(stamped, rotate: true);

        ink.Width.Should().BeGreaterThan(ink.Height * 3, "the footer reads left to right on the shown page");
        ink.Bottom.Should().BeGreaterThan(170, "it is in the bottom band of the 300 x 200 shown page");
        ((ink.Left + ink.Right) / 2.0).Should().BeApproximately(150, 4);
    }

    [Fact]
    public void ImageWatermark_IsDrawnAndCentredOnTheShownBox()
    {
        // A CropBox page (our rasterizer places images axis-aligned only, so
        // the /Rotate case is covered by the text tests). The image must also
        // draw at all: before 3.34.0 its XObject held PNG file bytes.
        using PdfDocument source = Page("/MediaBox[0 0 300 500]/CropBox[50 100 250 400]");
        PixelBuffer black = new PixelBuffer(4, 2);
        black.Clear(ColorF.Black);
        ImageFrame frame = new ImageFrame(black, ImageColorFormat.Rgb24);
        ImageWatermarkOptions options = new ImageWatermarkOptions
        {
            ScaleFraction = 0.5,
            Opacity = 1f,
            PlaceInDisplaySpace = true,
        };

        byte[] stamped;
        using (MemoryStream output = new MemoryStream())
        {
            WatermarkStamper.ApplyImage(output, source, frame, options);
            stamped = output.ToArray();
        }

        Box ink = Ink(stamped, rotate: true);

        ((ink.Left + ink.Right) / 2.0).Should().BeApproximately(100, 2, "centred on the 200 x 300 shown box");
        ((ink.Top + ink.Bottom) / 2.0).Should().BeApproximately(150, 2);
        ((double)ink.Width).Should().BeApproximately(100, 2, "half the shown width");
        ((double)ink.Height).Should().BeApproximately(50, 2);
    }

    private static byte[] Stamp(PdfDocument source, TextWatermarkOptions options)
    {
        using MemoryStream output = new MemoryStream();
        WatermarkStamper.ApplyText(output, source, options);
        return output.ToArray();
    }

    private static Box Ink(byte[] pdf, bool rotate)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf));
        PixelBuffer px = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72, ApplyPageRotation = rotate }).Rasterize(doc.Pages[0]);
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = -1;
        int bottom = -1;
        for (int y = 0; y < px.Height; y++)
        {
            for (int x = 0; x < px.Width; x++)
            {
                if (px.GetPixelBgra(x, y).B < 128)
                {
                    left = System.Math.Min(left, x);
                    right = System.Math.Max(right, x);
                    top = System.Math.Min(top, y);
                    bottom = System.Math.Max(bottom, y);
                }
            }
        }

        right.Should().BeGreaterThan(-1, "something was drawn");
        return new Box(left, top, right + 1, bottom + 1);
    }

    // TestPdf.OnePage writes a 200 x 200 MediaBox; these pages need their
    // own. A fixed-width run of spaces is reserved and overwritten in place,
    // so every xref offset stays right.
    private static PdfDocument Page(string extraPage)
    {
        const int Reserved = 120;
        const string FixtureMediaBox = "/MediaBox[0 0 200 200]";
        string reserve = new string(' ', Reserved);
        string text = System.Text.Encoding.Latin1.GetString(
            TestPdf.OnePage(string.Empty, NoResources, System.Array.Empty<string>(), extraPage: reserve));
        int media = text.IndexOf(FixtureMediaBox, System.StringComparison.Ordinal);
        text = text.Remove(media, FixtureMediaBox.Length).Insert(media, new string(' ', FixtureMediaBox.Length));
        int slot = text.IndexOf(reserve, System.StringComparison.Ordinal);
        text = text.Remove(slot, Reserved).Insert(slot, extraPage.PadRight(Reserved));
        return TestPdf.Open(System.Text.Encoding.Latin1.GetBytes(text));
    }

    private readonly record struct Box(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;

        public int Height => Bottom - Top;
    }
}
