// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for v3.19 Scrub residuals: images and forms crossing the crop box are
// cropped/scrubbed at byte level, and replaced originals leave the file.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class ScrubResidualTests
{
    // Crop to the bottom-left 200x200 of a 400x400 page.
    private static readonly RectangleF Crop = new RectangleF(0, 0, 200, 200);
    private const string Media = "/MediaBox[0 0 400 400]";
    private const string Helv = "/Font<</F1 9 0 R>>";

    [Fact]
    public void OffBoxText_DoesNotSurviveInTheOriginalContentStream()
    {
        // Unfiltered content, so any surviving copy is visible as raw bytes.
        byte[] pdf = Page(
            "/Resources<<" + Helv + ">>",
            "BT /F1 12 Tf 20 20 Td (KEEPTEXT) Tj ET BT /F1 12 Tf 300 300 Td (SECRETOFFBOX) Tj ET");

        string output = ScrubToString(pdf);

        output.Should().NotContain("SECRETOFFBOX",
            "the original, unscrubbed content stream must not remain in the file");
        ExtractText(output).Should().Contain("KEEPTEXT");
    }

    [Fact]
    public void ImageXObjectCrossingTheBox_IsCroppedAndTheOriginalLeavesTheFile()
    {
        // 8x8 RGB image over (100,100)-(300,300): its bottom-left 4x4 quadrant
        // is in the box. In-box pixels are green; every off-box pixel carries
        // the marker bytes "QZX".
        byte[] pdf = Page(
            "/Resources<</XObject<</Im0 9 0 R>>>>",
            "q 200 0 0 200 100 100 cm /Im0 Do Q",
            Image(8, 8, "/ColorSpace/DeviceRGB/BitsPerComponent 8", QuadrantRgb(8)));

        string output = ScrubToString(pdf);

        output.Should().NotContain("QZX", "no off-box pixel may survive anywhere in the file");
        PixelBuffer pixels = Render(output);
        IsGreen(pixels, 150, 50 + 0).Should().BeTrue("the in-box quadrant still shows (raster rows top-down)");
    }

    [Fact]
    public void InlineImageCrossingTheBox_IsCroppedAtSampleLevel()
    {
        string data = Encoding.Latin1.GetString(QuadrantRgb(8));
        byte[] pdf = Page(
            string.Empty,
            "q 200 0 0 200 100 100 cm BI /W 8 /H 8 /CS /RGB /BPC 8 ID " + data + " EI Q");

        string output = ScrubToString(pdf);

        output.Should().NotContain("QZX");
        output.Should().Contain("/ScrubIm0 Do", "the in-box part is re-emitted as a cropped image");
        IsGreen(Render(output), 150, 50).Should().BeTrue();
    }

    [Fact]
    public void RotatedInlineImageCrossingTheBox_BlanksOffBoxPixels()
    {
        // Rotated 45 degrees about (150,150); a large part lies outside.
        string data = Encoding.Latin1.GetString(QuadrantRgb(8));
        byte[] pdf = Page(
            string.Empty,
            "q 141.42 141.42 -141.42 141.42 150 0 cm BI /W 8 /H 8 /CS /RGB /BPC 8 ID " + data + " EI Q");

        string output = ScrubToString(pdf);

        output.Should().NotContain("QZX", "off-box pixels of a rotated placement are blanked, not kept");
    }

    [Fact]
    public void OneBitStencilInlineImage_IsCroppedAndStillPaints()
    {
        // 16x16 1-bit stencil, all zero bits = paint everywhere (Decode [0 1]).
        string data = Encoding.Latin1.GetString(new byte[32]);
        byte[] pdf = Page(
            string.Empty,
            "1 0 0 rg q 200 0 0 200 100 100 cm BI /W 16 /H 16 /IM true ID " + data + " EI Q");

        string output = ScrubToString(pdf);
        PixelBuffer pixels = Render(output);

        (byte b, byte g, byte r, byte _) = pixels.GetPixelBgra(150, 50);
        r.Should().BeGreaterThan(200, "the stencil still paints the fill colour inside the box");
        g.Should().BeLessThan(80);
        b.Should().BeLessThan(80);
    }

    [Fact]
    public void FormXObjectCrossingTheBox_IsScrubbedRecursively()
    {
        byte[] form = RawPdf.Stream(
            "/Type/XObject/Subtype/Form/BBox[0 0 400 400]/Resources<</Font<</F1 10 0 R>>>>",
            "BT /F1 12 Tf 20 20 Td (FORMKEEP) Tj ET BT /F1 12 Tf 300 300 Td (FORMSECRET) Tj ET");
        byte[] pdf = Page("/Resources<</XObject<</Fm0 9 0 R>>>>", "/Fm0 Do", form);

        string output = ScrubToString(pdf);

        output.Should().NotContain("FORMSECRET", "the form's off-box text is scrubbed and the original form omitted");
        ExtractText(output).Should().Contain("FORMKEEP");
    }

    [Fact]
    public void ImageSharedWithAnUncroppedPage_IsRetained()
    {
        byte[] image = Image(8, 8, "/ColorSpace/DeviceRGB/BitsPerComponent 8", QuadrantRgb(8));
        List<byte[]> bodies = new List<byte[]>
        {
            RawPdf.Obj("<</Type/Catalog/Pages 2 0 R>>"),
            RawPdf.Obj("<</Type/Pages/Kids[3 0 R 5 0 R]/Count 2>>"),
            RawPdf.Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 7 0 R>>>>/Contents 4 0 R>>"),
            RawPdf.Stream(string.Empty, "q 200 0 0 200 100 100 cm /Im0 Do Q"),
            RawPdf.Obj("<</Type/Page/Parent 2 0 R" + Media + "/Resources<</XObject<</Im0 7 0 R>>>>/Contents 6 0 R>>"),
            RawPdf.Stream(string.Empty, "q 200 0 0 200 100 100 cm /Im0 Do Q"),
            image,
        };

        string output = ScrubToString(RawPdf.Build(bodies), pageIndex: 0);

        output.Should().Contain("QZX", "the second, uncropped page still uses the full image");
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(output)), leaveOpen: false);
        doc.PageCount.Should().Be(2);
    }

    [Fact]
    public void UndecodableCrossingImage_IsRemoved()
    {
        // JPX is not decodable here: the crossing image cannot be cropped, so
        // it is removed rather than kept whole (over-redaction in doubt).
        byte[] jpx = Encoding.Latin1.GetBytes("JPXSECRETPAYLOAD");
        byte[] pdf = Page(
            "/Resources<</XObject<</Im0 9 0 R>>>>",
            "q 200 0 0 200 100 100 cm /Im0 Do Q",
            Image(8, 8, "/ColorSpace/DeviceRGB/BitsPerComponent 8/Filter/JPXDecode", jpx));

        string output = ScrubToString(pdf);

        output.Should().NotContain("JPXSECRETPAYLOAD");
        output.Should().NotContain("/Im0 Do");
    }

    [Fact]
    public void ImageEntirelyInsideTheBox_IsKeptByteForByte()
    {
        byte[] pdf = Page(
            "/Resources<</XObject<</Im0 9 0 R>>>>",
            "q 100 0 0 100 50 50 cm /Im0 Do Q",
            Image(8, 8, "/ColorSpace/DeviceRGB/BitsPerComponent 8", QuadrantRgb(8)));

        string output = ScrubToString(pdf);

        output.Should().Contain("/Im0 Do");
        output.Should().Contain("QZX", "an in-box image is untouched");
    }

    [Fact]
    public void InheritedResources_StillResolveAfterScrub()
    {
        List<byte[]> bodies = new List<byte[]>
        {
            RawPdf.Obj("<</Type/Catalog/Pages 2 0 R>>"),
            RawPdf.Obj("<</Type/Pages/Kids[3 0 R]/Count 1/Resources<<" + Helv + ">>>>"),
            RawPdf.Obj("<</Type/Page/Parent 2 0 R" + Media + "/Contents 4 0 R>>"),
            RawPdf.Stream(string.Empty, "BT /F1 12 Tf 20 20 Td (INHERITED) Tj ET"),
            RawPdf.Obj("<<>>"),
            RawPdf.Obj("<<>>"),
            RawPdf.Obj("<<>>"),
            RawPdf.Obj("<<>>"),
            Helvetica(),
        };

        string output = ScrubToString(RawPdf.Build(bodies));

        ExtractText(output).Should().Contain("INHERITED", "the page's inherited font must still resolve");
    }

    // ── helpers ───────────────────────────────────────────────────────────

    // Catalog 1, Pages 2, Page 3, Contents 4, padding 5-8, extra object 9,
    // Helvetica 10 (referenced as 9 0 R when no extra object is supplied).
    private static byte[] Page(string pageResources, string content, byte[]? extra = null)
    {
        string resources = pageResources.Length == 0 ? string.Empty : pageResources;
        if (extra is null)
        {
            resources = resources.Length == 0 ? "/Resources<<" + Helv + ">>" : resources;
        }
        else if (!resources.Contains("/Font"))
        {
            resources = resources.Replace("/Resources<<", "/Resources<</Font<</F1 10 0 R>>");
        }

        List<byte[]> bodies = new List<byte[]>
        {
            RawPdf.Obj("<</Type/Catalog/Pages 2 0 R>>"),
            RawPdf.Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            RawPdf.Obj("<</Type/Page/Parent 2 0 R" + Media + resources + "/Contents 4 0 R>>"),
            RawPdf.Stream(string.Empty, content),
            RawPdf.Obj("<<>>"),
            RawPdf.Obj("<<>>"),
            RawPdf.Obj("<<>>"),
            RawPdf.Obj("<<>>"),
            extra ?? Helvetica(),
            Helvetica(),
        };

        return RawPdf.Build(bodies);
    }

    private static byte[] Helvetica() => RawPdf.Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>");

    private static byte[] Image(int w, int h, string entries, byte[] data) =>
        RawPdf.Stream("/Type/XObject/Subtype/Image/Width " + w + "/Height " + h + entries, data);

    // Rows are top-down: the in-box quadrant (image space bottom-left) is the
    // bottom-left 4x4 block of pixels, filled green; the rest carry "QZX".
    private static byte[] QuadrantRgb(int size)
    {
        byte[] data = new byte[size * size * 3];
        int half = size / 2;
        int p = 0;
        for (int row = 0; row < size; row++)
        {
            for (int col = 0; col < size; col++)
            {
                bool inBox = col < half && row >= half;
                if (inBox)
                {
                    data[p++] = 0;
                    data[p++] = 200;
                    data[p++] = 0;
                }
                else
                {
                    data[p++] = (byte)'Q';
                    data[p++] = (byte)'Z';
                    data[p++] = (byte)'X';
                }
            }
        }

        return data;
    }

    private static string ScrubToString(byte[] pdf, int pageIndex = 0)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        PageCropper.Crop(output, doc, new List<PageCrop>
        {
            new PageCrop(pageIndex, Crop, PageCropMode.Scrub),
        });

        return Encoding.Latin1.GetString(output.ToArray());
    }

    private static string ExtractText(string pdf)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(pdf)), leaveOpen: false);
        TextExtractor extractor = new TextExtractor(doc.Objects, ExtractionStrategy.Layout);
        return extractor.ExtractText(doc.Pages[0]);
    }

    private static PixelBuffer Render(string pdf)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(pdf)), leaveOpen: false);
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);
    }

    private static bool IsGreen(PixelBuffer p, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = p.GetPixelBgra(x, y);
        return g > 150 && r < 80 && b < 80;
    }
}
