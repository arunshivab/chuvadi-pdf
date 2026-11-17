// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.6.5.2 (soft masks), §9.6.5 (Type 3 fonts)
// PHASE: 3.35.0 (backlog 17, D-385) — two Scrub residuals closed: a soft
//        mask set under more than one placement, and Type 3 glyph procedures.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class ScrubResiduals3350Tests
{
    // Crop to the bottom-left 200x200 of a 400x400 page.
    private static readonly RectangleF Crop = new RectangleF(0, 0, 200, 200);
    private const string Media = "/MediaBox[0 0 400 400]";

    [Fact]
    public void SoftMaskUnderTwoPlacements_IsScrubbedForEach()
    {
        // The group's SECRETMASK text lies outside the box under both
        // placements (at 300,300 and at 400,300); KEEPMASK lies inside under both.
        byte[] pdf = RawPdf.SinglePage(
            Media + "/Resources<</ExtGState<</G0<</SMask<</S/Luminosity/G 5 0 R>>>>>>>>",
            "q /G0 gs 0 0 1 rg 0 0 100 100 re f Q q 1 0 0 1 100 0 cm /G0 gs 0 0 1 rg 0 0 50 50 re f Q",
            RawPdf.Stream(
                "/Type/XObject/Subtype/Form/BBox[0 0 400 400]/Group<</S/Transparency/CS/DeviceGray>>"
                    + "/Resources<</Font<</F1 6 0 R>>>>",
                "1 g BT /F1 12 Tf 20 20 Td (KEEPMASK) Tj ET BT /F1 12 Tf 300 300 Td (SECRETMASK) Tj ET"),
            RawPdf.Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"));

        string output = Scrub(pdf);

        output.Should().NotContain("SECRETMASK", "each placement's group is scrubbed for that placement");
        output.Should().Contain("KEEPMASK");
        output.Should().Contain("/G0_p1 gs", "the second placement sets the state under a name of its own");
    }

    [Fact]
    public void Type3Font_KeepsOnlyTheGlyphProceduresOfKeptText()
    {
        byte[] pdf = Type3Page("BT /T3 12 Tf 20 20 Td (a) Tj ET BT /T3 12 Tf 300 300 Td (s) Tj ET", "[0.001 0 0 0.001 0 0]", "500");

        string output = Scrub(pdf);

        output.Should().Contain("GLYPHA");
        output.Should().NotContain("GLYPHSECRET", "the glyph is shown only outside the box");
        output.Should().NotContain("IMAGESECRET", "only the removed glyph's procedure drew the image");

        using PdfDocument scrubbed = PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(output)), leaveOpen: false);
        PixelBuffer px = new PageRasterizer(scrubbed.Objects, new RenderOptions { Dpi = 72 }).Rasterize(scrubbed.Pages[0]);
        px.GetPixelBgra(26, 200 - 26).R.Should().BeLessThan(100, "the kept glyph still draws");
    }

    [Fact]
    public void Type3Widths_FollowTheFontMatrix()
    {
        // Widths of 100 glyph units under a 0.01 matrix are a full em: at
        // size 100 from x 120, "a" is centred at 170 (inside) and "s" at 270
        // (outside). Read as thousandths, both were taken as inside.
        byte[] pdf = Type3Page("BT /T3 100 Tf 120 20 Td (as) Tj ET", "[0.01 0 0 0.01 0 0]", "100");

        string output = Scrub(pdf);

        output.Should().Contain("GLYPHA");
        output.Should().NotContain("GLYPHSECRET");
    }

    // A page with a Type 3 font /T3 whose glyph "a" (code 97) and "s" (code
    // 115) are marked GLYPHA and GLYPHSECRET; only "s" draws the image /ImS.
    private static byte[] Type3Page(string content, string fontMatrix, string width)
    {
        string widths = width;
        for (int i = 98; i <= 115; i++)
        {
            widths += " " + width;
        }

        return RawPdf.SinglePage(
            Media + "/Resources<</Font<</T3 5 0 R>>>>",
            content,
            RawPdf.Obj("<</Type/Font/Subtype/Type3/FontBBox[0 0 1000 1000]/FontMatrix" + fontMatrix
                + "/CharProcs<</a 6 0 R/s 7 0 R>>/Encoding<</Differences[97/a 115/s]>>"
                + "/FirstChar 97/LastChar 115/Widths[" + widths + "]"
                + "/Resources<</XObject<</ImS 8 0 R>>>>>>"),
            RawPdf.Stream(string.Empty, "0 0 d0 0 0 1000 1000 re f % GLYPHA"),
            RawPdf.Stream(string.Empty, "0 0 d0 q 50 0 0 50 0 0 cm /ImS Do Q % GLYPHSECRET"),
            RawPdf.Stream("/Type/XObject/Subtype/Image/Width 11/Height 1/ColorSpace/DeviceGray/BitsPerComponent 8", "IMAGESECRET"));
    }

    private static string Scrub(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        PageCropper.Crop(output, doc, new List<PageCrop> { new PageCrop(0, Crop, PageCropMode.Scrub) });
        return Encoding.Latin1.GetString(output.ToArray());
    }
}
