// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.4.4 (predictors), §7.5.4 (xref), §7.7.3
//        (page tree), §8.9.5 (image dictionaries, /Decode), §8.9.6 (masking),
//        §8.9.7 (inline images), §9.6.2 (Type 1 fonts)
// PHASE: 3.34.0 (Phase B test bench, D-365 / D-366: N1, N3 to N9, N11, N13)
//        — every file is built here; no PDF is committed.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Chuvadi.Fonts;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Svg;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class PhaseBRender3340Tests
{
    // ── N1: hinting at the size drawn, not the Tf size ─────────────────────

    [Fact]
    public void Hinting_UsesTheDrawnSize_WhenTheTextMatrixScales()
    {
        // The same text at 12 pt, once as 12 Tf and once as 1 Tf with a
        // text matrix that scales by 12. Hinted at the drawn size, both draw
        // the same pixels; hinted at the Tf size, the second was fitted to a
        // one-pixel grid.
        byte[] program = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        using PdfDocument byTf = TestPdf.Open(TrueTypePage(program, "BT /F1 12 Tf 20 100 Td (Hamburg 1946) Tj ET"));
        using PdfDocument byTm = TestPdf.Open(TrueTypePage(program, "BT /F1 1 Tf 12 0 0 12 20 100 Tm (Hamburg 1946) Tj ET"));

        PixelBuffer a = Raster(byTf, HintingMode.Full);
        PixelBuffer b = Raster(byTm, HintingMode.Full);

        Ink(a).Should().BeGreaterThan(100);
        Differing(a, b).Should().Be(0);
    }

    [Fact]
    public void Hinting_KeepsTheTfSize_WhenTheScaleIsModerate()
    {
        // 16 Tf under a 0.75 CTM draws at 12 pt. The scale is between 0.5
        // and 2, so the text is hinted at 16 as in 3.33.0 (D-377), and it
        // does not draw the same pixels as text hinted at 12.
        byte[] program = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        using PdfDocument byTf = TestPdf.Open(TrueTypePage(program, "BT /F1 12 Tf 20 100 Td (Hamburg 1946) Tj ET"));
        using PdfDocument byCtm = TestPdf.Open(TrueTypePage(program, "q 0.75 0 0 0.75 20 100 cm BT /F1 16 Tf (Hamburg 1946) Tj ET Q"));

        PixelBuffer a = Raster(byTf, HintingMode.Full);
        PixelBuffer b = Raster(byCtm, HintingMode.Full);

        Ink(b).Should().BeGreaterThan(100);
        Differing(a, b).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Hinting_IsSkipped_ForASkewedTextMatrix()
    {
        byte[] program = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        using PdfDocument skewed = TestPdf.Open(TrueTypePage(program, "BT /F1 1 Tf 12 0 4 12 20 100 Tm (Hamburg) Tj ET"));

        PixelBuffer hinted = Raster(skewed, HintingMode.Full);
        PixelBuffer plain = Raster(skewed, HintingMode.Off);

        Ink(hinted).Should().BeGreaterThan(100);
        Differing(hinted, plain).Should().Be(0);
    }

    [Fact]
    public void Hinting_IsKept_ForANearlyEvenTextMatrix()
    {
        // Word sets body text with matrices such as [0.9981 0 0 1]: a 0.2%
        // stretch still counts as even, so the text is hinted as in 3.33.0.
        byte[] program = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        using PdfDocument word = TestPdf.Open(TrueTypePage(program, "BT /F1 12 Tf 0.9981 0 0 1 20 100 Tm (Hamburg 1946) Tj ET"));

        PixelBuffer hinted = Raster(word, HintingMode.Full);
        PixelBuffer plain = Raster(word, HintingMode.Off);

        Ink(hinted).Should().BeGreaterThan(100);
        Differing(hinted, plain).Should().BeGreaterThan(0);
    }

    // ── N3: CMYK JPEG read through /Decode, not inverted ───────────────────

    [Fact]
    public void CmykJpeg_IsReadAsStored_WithoutDecode()
    {
        // MuPDF stores the channels uninverted (with an Adobe marker): cyan.
        using PdfDocument doc = TestPdf.Open(JpegPage(string.Empty));

        (byte r, byte g, byte b) = Pixel(Raster(doc, HintingMode.Off), 100, 100);

        r.Should().BeLessThan(40);
        g.Should().BeGreaterThan(200);
        b.Should().BeGreaterThan(200);
        new SvgRenderer().RenderPage(doc, 0).Should().Contain("data:image/png").And.NotContain("data:image/jpeg");
    }

    [Fact]
    public void CmykJpeg_WithAnInvertingDecode_IsInverted()
    {
        using PdfDocument doc = TestPdf.Open(JpegPage("/Decode[1 0 1 0 1 0 1 0]"));

        (byte r, byte g, byte b) = Pixel(Raster(doc, HintingMode.Off), 100, 100);

        (r + g + b).Should().BeLessThan(90);
    }

    [Fact]
    public void JpegBehindAnotherFilter_IsDecoded()
    {
        // [/FlateDecode /DCTDecode]: the Flate layer comes off first (found
        // by the pdf.js run, decodeACSuccessive.pdf).
        byte[] jpeg = File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "Images", "cmyk-cyan-mupdf.jpg"));
        TestPdf pdf = new TestPdf();
        int image = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 16/Height 8/ColorSpace/DeviceCMYK/BitsPerComponent 8/Filter[/FlateDecode/DCTDecode]>>", Zlib(jpeg));
        using PdfDocument doc = TestPdf.Open(Page(pdf, "q 160 0 0 160 20 20 cm /Im Do Q", "<</XObject<</Im " + N(image) + " 0 R>>>>"));

        (byte r, byte g, byte b) = Pixel(Raster(doc, HintingMode.Off), 100, 100);

        r.Should().BeLessThan(40);
        g.Should().BeGreaterThan(200);
        b.Should().BeGreaterThan(200);
    }

    [Fact]
    public void JpegThatCannotBeRead_IsSkipped_NotDrawnAsSamples()
    {
        // Four bytes for a large image (pdf.js issue18042.pdf): reported and
        // skipped, never padded into a black box.
        List<PdfWarning> warnings = new List<PdfWarning>();
        TestPdf pdf = new TestPdf();
        int image = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 730/Height 760/ColorSpace/DeviceRGB/BitsPerComponent 8/Filter/DCTDecode>>", new byte[] { 1, 2, 3, 4 });
        using PdfDocument doc = TestPdf.Open(Page(pdf, "q 160 0 0 160 20 20 cm /Im Do Q", "<</XObject<</Im " + N(image) + " 0 R>>>>"), warnings);

        Ink(Raster(doc, HintingMode.Off)).Should().Be(0);
        warnings.Should().Contain(w => w.Kind == PdfWarningKind.ImageSkipped);
    }

    [Fact]
    public void SoftMaskThatIsAJpeg_IsApplied()
    {
        // A red image whose /SMask is a gray JPEG: black (transparent) on the
        // left half, white (opaque) on the right (pdf.js issue18956.pdf).
        Chuvadi.Pdf.Images.ImageFrame mask = Chuvadi.Pdf.Images.ImageFrame.Create(16, 8, Chuvadi.Pdf.Images.ImageColorFormat.Gray8);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                byte v = x < 8 ? (byte)0 : (byte)255;
                mask.Pixels.SetPixelBgra(x, y, v, v, v, 255);
            }
        }

        using MemoryStream maskJpeg = new MemoryStream();
        Chuvadi.Pdf.Images.JpegEncoder.Encode(mask, maskJpeg, 100);
        byte[] red = new byte[16 * 8 * 3];
        for (int i = 0; i < red.Length; i += 3)
        {
            red[i] = 255;
        }

        TestPdf pdf = new TestPdf();
        int smask = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 16/Height 8/ColorSpace/DeviceGray/BitsPerComponent 8/Filter/DCTDecode>>", maskJpeg.ToArray());
        int image = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 16/Height 8/ColorSpace/DeviceRGB/BitsPerComponent 8/SMask " + N(smask) + " 0 R>>", red);
        using PdfDocument doc = TestPdf.Open(Page(pdf, "q 160 0 0 80 20 60 cm /Im Do Q", "<</XObject<</Im " + N(image) + " 0 R>>>>"));

        PixelBuffer page = Raster(doc, HintingMode.Off);

        Pixel(page, 50, 100).Should().Be(((byte)255, (byte)255, (byte)255));
        (byte r, byte g, byte b) = Pixel(page, 150, 100);
        r.Should().BeGreaterThan(240);
        (g + b).Should().BeLessThan(20);
    }

    // ── N4: inline images ──────────────────────────────────────────────────

    [Theory]
    [InlineData("unfiltered, with < ( ) in the data")]
    [InlineData("ASCIIHex")]
    [InlineData("Flate")]
    [InlineData("named colour space")]
    [InlineData("image mask")]
    [InlineData("CR LF after ID")]
    public void InlineImage_IsDrawn_AndTheContentAfterItToo(string kind)
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = TestPdf.Open(InlineImagePage(kind), warnings);

        PixelBuffer page = Raster(doc, HintingMode.Off);

        InkIn(page, 20, 20, 80, 80).Should().BeGreaterThan(3000, "the inline image is drawn");
        InkIn(page, 120, 20, 180, 80).Should().BeGreaterThan(3000, "the content after EI is drawn");
        warnings.Should().NotContain(w => w.Kind == PdfWarningKind.StreamDamaged);
        new SvgRenderer().RenderPage(doc, 0).Should().Contain("<image");
    }

    [Fact]
    public void InlineImage_WithNoEI_KeepsWhatCameBefore_AndWarns()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = TestPdf.Open(
            TestPdf.OnePage("0 0 1 rg 120 20 60 60 re f BI /W 2 /H 2 /CS /G /F /AHx ID 00ff00ff", "<<>>", Array.Empty<string>()),
            warnings);

        InkIn(Raster(doc, HintingMode.Off), 120, 20, 180, 80).Should().BeGreaterThan(3000);
        warnings.Should().Contain(w => w.Kind == PdfWarningKind.StreamDamaged && w.Reason.Contains("EI", StringComparison.Ordinal));
    }

    [Fact]
    public void StrayQ_AndAFontOperatorWithAStrayOperand_DoNotStopSvg()
    {
        // Found by fuzzing the inline-image reader: both threw in SVG.
        using PdfDocument doc = TestPdf.Open(TestPdf.OnePage(
            "Q 0 0 1 rg 120 20 60 60 re f BT 7 /F1 12 Tf 10 150 Td (Hello) Tj ET",
            "<</Font<</F1<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>>>>>",
            Array.Empty<string>()));

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("<svg");
        InkIn(Raster(doc, HintingMode.Off), 120, 20, 180, 80).Should().BeGreaterThan(3000);
        InkIn(Raster(doc, HintingMode.Off), 10, 145, 60, 165).Should().BeGreaterThan(20);
    }

    // ── N14: found with the test bench's real files ────────────────────────

    [Theory]
    [InlineData("0 0 1 rg 20 20 m 180 20 l 180 180 l 20 180 l f")]
    [InlineData("0 0 1 rg 20 180 m 180 180 l 180 20 l 20 20 l f*")]
    [InlineData("0 0 1 rg 20 20 m 180 20 l 100 180 l f")]
    public void FillOfAnOpenPath_ClosesIt(string content)
    {
        // m l l ... f with no h (safedocs-0176): the fill closes the path.
        using PdfDocument doc = TestPdf.Open(TestPdf.OnePage(content, "<<>>", Array.Empty<string>()));

        Pixel(Raster(doc, HintingMode.Off), 100, 120).Should().Be(((byte)0, (byte)0, (byte)255));
    }

    [Fact]
    public void TransparencyGroupForm_KeepsTheAlphaItWasDrawnWith()
    {
        // A group form drawn at /ca 0.5 whose content sets /ca 1
        // (safedocs-0206): the content is still drawn at half opacity.
        TestPdf pdf = new TestPdf();
        int form = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 200 200]/Group<</S/Transparency>>/Resources<</ExtGState<</Full<</ca 1>>>>>>>>",
            "/Full gs 0 0 0 rg 20 20 160 160 re f");
        string resources = "<</ExtGState<</Half<</ca 0.5>>>>/XObject<</Fm " + N(form) + " 0 R>>>>";
        using PdfDocument doc = TestPdf.Open(Page(pdf, "/Half gs /Fm Do", resources));

        (byte r, byte g, byte b) = Pixel(Raster(doc, HintingMode.Off), 100, 100);

        r.Should().BeInRange(110, 145);
        g.Should().BeInRange(110, 145);
        b.Should().BeInRange(110, 145);
    }

    // ── N5: an embedded Type 1 font with a standard-14 name ────────────────

    [Fact]
    public void EmbeddedType1_WithAStandard14Name_DrawsItsOwnGlyphs()
    {
        // The embedded program's "A" is a 100 x 100 square (at 1000 units).
        List<PdfWarning> warnings = new List<PdfWarning>();
        TestPdf pdf = new TestPdf();
        int file = pdf.AddStream("<<>>", Type1Program());
        int fd = pdf.Add("<</Type/FontDescriptor/FontName/Helvetica/Flags 32/FontBBox[0 0 1000 1000]/ItalicAngle 0/Ascent 900/Descent -200/CapHeight 700/StemV 80/FontFile "
            + N(file) + " 0 R>>");
        string font = "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/FirstChar 65/LastChar 65/Widths[500]/FontDescriptor " + N(fd) + " 0 R>>";
        using PdfDocument doc = TestPdf.Open(Page(pdf, "BT /F1 400 Tf 20 20 Td (A) Tj ET", "<</Font<</F1 " + font + ">>>>"), warnings);

        PixelBuffer page = Raster(doc, HintingMode.Off);

        // A 40 x 40 square at (20, 20); Helvetica's A would leave holes.
        InkIn(page, 22, 22, 58, 58).Should().Be(36 * 36);
        InkIn(page, 62, 62, 200, 200).Should().Be(0);
        warnings.Should().NotContain(w => w.Kind == PdfWarningKind.FontSubstituted);
    }

    // ── N6: indirect /DecodeParms ──────────────────────────────────────────

    [Fact]
    public void Image_WithIndirectDecodeParms_UsesThePredictor()
    {
        // 4 x 4 gray, PNG "Up" rows under a first row of 200s: every sample
        // is 200 only when the predictor is applied.
        byte[] rows = { 0, 200, 200, 200, 200, 2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 2, 0, 0, 0, 0 };
        TestPdf pdf = new TestPdf();
        int parms = pdf.Add("<</Predictor 15/Colors 1/BitsPerComponent 8/Columns 4>>");
        int image = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 4/Height 4/ColorSpace/DeviceGray/BitsPerComponent 8/Filter/FlateDecode/DecodeParms "
            + N(parms) + " 0 R>>", Zlib(rows));
        using PdfDocument doc = TestPdf.Open(Page(pdf, "q 100 0 0 100 50 50 cm /Im Do Q", "<</XObject<</Im " + N(image) + " 0 R>>>>"));

        (byte r, byte g, byte b) = Pixel(Raster(doc, HintingMode.Off), 100, 100);

        r.Should().BeInRange(197, 203);
        g.Should().BeInRange(197, 203);
        b.Should().BeInRange(197, 203);
    }

    // ── N7: images that cannot be drawn are reported; /Mask is applied ─────

    [Fact]
    public void JpxImage_IsReportedAsSkipped()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        TestPdf pdf = new TestPdf();
        int image = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 4/Height 4/ColorSpace/DeviceRGB/BitsPerComponent 8/Filter/JPXDecode>>", new byte[] { 0, 0, 0, 12, 0x6A, 0x50 });
        using PdfDocument doc = TestPdf.Open(Page(pdf, "q 100 0 0 100 50 50 cm /Im Do Q", "<</XObject<</Im " + N(image) + " 0 R>>>>"), warnings);

        Raster(doc, HintingMode.Off);

        warnings.Should().Contain(w => w.Kind == PdfWarningKind.ImageSkipped && w.Reason.Contains("JPXDecode", StringComparison.Ordinal));
    }

    [Fact]
    public void StencilMask_HidesTheMaskedSamples()
    {
        // Two red samples; the mask's first bit is 1, so the left one is hidden.
        TestPdf pdf = new TestPdf();
        int mask = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 2/Height 1/ImageMask true/BitsPerComponent 1>>", new byte[] { 0x80 });
        int image = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 2/Height 1/ColorSpace/DeviceRGB/BitsPerComponent 8/Mask " + N(mask) + " 0 R>>",
            new byte[] { 255, 0, 0, 255, 0, 0 });
        using PdfDocument doc = TestPdf.Open(Page(pdf, "q 160 0 0 80 20 60 cm /Im Do Q", "<</XObject<</Im " + N(image) + " 0 R>>>>"));

        PixelBuffer page = Raster(doc, HintingMode.Off);

        Pixel(page, 60, 100).Should().Be(((byte)255, (byte)255, (byte)255));
        Pixel(page, 140, 100).Should().Be(((byte)255, (byte)0, (byte)0));
    }

    [Fact]
    public void MaskThatIsAPlainGrayImage_ShowsWhereItIsWhite()
    {
        // A /Mask stream without /ImageMask (pdf.js issue6621.pdf): read as
        // Poppler and MuPDF read it, 1 (white) shows the image.
        TestPdf pdf = new TestPdf();
        int mask = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 2/Height 1/ColorSpace/DeviceGray/BitsPerComponent 1>>", new byte[] { 0x80 });
        int image = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 2/Height 1/ColorSpace/DeviceRGB/BitsPerComponent 8/Mask " + N(mask) + " 0 R>>",
            new byte[] { 255, 0, 0, 255, 0, 0 });
        using PdfDocument doc = TestPdf.Open(Page(pdf, "q 160 0 0 80 20 60 cm /Im Do Q", "<</XObject<</Im " + N(image) + " 0 R>>>>"));

        PixelBuffer page = Raster(doc, HintingMode.Off);

        Pixel(page, 60, 100).Should().Be(((byte)255, (byte)0, (byte)0));
        Pixel(page, 140, 100).Should().Be(((byte)255, (byte)255, (byte)255));
    }

    [Fact]
    public void SoftMaskGroup_PaintsOnlyInsideItsBBox()
    {
        // A luminosity mask whose group fills black everywhere but has a
        // /BBox over the left half only; outside it the backdrop (white,
        // opaque) applies (pdf.js issue7891_bc1.pdf). The masked blue square
        // shows on the right half only.
        TestPdf pdf = new TestPdf();
        int group = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 100 200]/Group<</S/Transparency/CS/DeviceRGB>>>>", "0 g 0 0 200 200 re f");
        string resources = "<</ExtGState<</M<</Type/ExtGState/SMask<</Type/Mask/S/Luminosity/BC[1 1 1]/G " + N(group) + " 0 R>>>>>>>>";
        using PdfDocument doc = TestPdf.Open(Page(pdf, "/M gs 0 0 1 rg 20 20 160 160 re f", resources));

        PixelBuffer page = Raster(doc, HintingMode.Off);

        Pixel(page, 50, 100).Should().Be(((byte)255, (byte)255, (byte)255));
        Pixel(page, 150, 100).Should().Be(((byte)0, (byte)0, (byte)255));
    }

    [Fact]
    public void ColourKeyMask_HidesTheSamplesInRange()
    {
        // Red is in the key range [255 255 0 0 0 0]; blue is not.
        TestPdf pdf = new TestPdf();
        int image = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 2/Height 1/ColorSpace/DeviceRGB/BitsPerComponent 8/Mask[255 255 0 0 0 0]>>",
            new byte[] { 255, 0, 0, 0, 0, 255 });
        using PdfDocument doc = TestPdf.Open(Page(pdf, "q 160 0 0 80 20 60 cm /Im Do Q", "<</XObject<</Im " + N(image) + " 0 R>>>>"));

        PixelBuffer page = Raster(doc, HintingMode.Off);

        Pixel(page, 60, 100).Should().Be(((byte)255, (byte)255, (byte)255));
        Pixel(page, 140, 100).Should().Be(((byte)0, (byte)0, (byte)255));
    }

    // ── N8, N11: cross-reference table layout ──────────────────────────────

    [Fact]
    public void StartxrefPointingAtWhiteSpaceBeforeXref_Opens()
    {
        string text = Encoding.Latin1.GetString(SquarePage());
        int xref = text.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        byte[] bytes = Encoding.Latin1.GetBytes(text[..xref] + "\r\n  " + text[xref..]);

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions());

        doc.PageCount.Should().Be(1);
        TestPdf.Ink(doc, AnnotationMode.View, 20, 20, 80, 80).Should().BeGreaterThan(3000);
        doc.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void XrefKeywordAndSubsectionOnOneLine_Opens()
    {
        string text = Encoding.Latin1.GetString(SquarePage());
        byte[] bytes = Encoding.Latin1.GetBytes(text.Replace("\nxref\n0 ", "\nxref 0 ", StringComparison.Ordinal));

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions());

        doc.PageCount.Should().Be(1);
        TestPdf.Ink(doc, AnnotationMode.View, 20, 20, 80, 80).Should().BeGreaterThan(3000);
        doc.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void StartxrefNearATableWhoseOffsetsAreWrong_StillNeedsRepair()
    {
        // Every offset in the table is off by 3: the table reached past white
        // space is not trusted, and repair rebuilds it, as before.
        string text = Encoding.Latin1.GetString(SquarePage());
        int xref = text.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        string table = text[xref..];
        string shifted = System.Text.RegularExpressions.Regex.Replace(table, "(\\d{10}) 00000 n", m =>
            (long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + 3).ToString("D10", CultureInfo.InvariantCulture) + " 00000 n");
        byte[] bytes = Encoding.Latin1.GetBytes(text[..xref] + "\n" + shifted);

        Action plain = () => PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions()).Dispose();
        plain.Should().Throw<PdfException>();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions { RepairIfDamaged = true });
        TestPdf.Ink(doc, AnnotationMode.View, 20, 20, 80, 80).Should().BeGreaterThan(3000);
    }

    [Fact]
    public void StreamWithAWrongLength_IsReadToEndstream()
    {
        // /Length 9 for a longer content stream (pdf.js issue6108.pdf).
        string text = Encoding.Latin1.GetString(SquarePage());
        int length = text.IndexOf("/Length 25", StringComparison.Ordinal);
        byte[] bytes = Encoding.Latin1.GetBytes(text[..length] + "/Length 09" + text[(length + 10)..]);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions());

        TestPdf.Ink(doc, AnnotationMode.View, 20, 20, 80, 80).Should().BeGreaterThan(3000);
        doc.Warnings.Should().Contain(w => w.Contains("does not end at 'endstream'", StringComparison.Ordinal));
    }

    // ── N9: a page-tree entry that is not a dictionary ─────────────────────

    [Fact]
    public void PageTreeKidThatIsNotADictionary_IsSkipped_WithAWarning()
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int junk = pdf.Add("42");
        int c1 = pdf.AddStream("<<>>", "1 0 0 rg 20 20 60 60 re f");
        int c2 = pdf.AddStream("<<>>", "0 0 1 rg 20 20 60 60 re f");
        int p1 = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Contents " + N(c1) + " 0 R>>");
        int p2 = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Contents " + N(c2) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(p1) + " 0 R " + N(junk) + " 0 R " + N(p2) + " 0 R]/Count 2>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf.Build(catalog)), new PdfOpenOptions());

        PixelBuffer second = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[1]);

        Pixel(second, 50, 150).Should().Be(((byte)0, (byte)0, (byte)255));
        doc.Warnings.Should().Contain(w => w.Contains("is not a dictionary", StringComparison.Ordinal));
    }

    // ── N13: image data that ends early ────────────────────────────────────

    [Fact]
    public void ImageDataThatEndsEarly_IsDrawn_WithTheRestAsZero()
    {
        // 4 x 4 RGB with only the first two rows (red) present.
        List<PdfWarning> warnings = new List<PdfWarning>();
        byte[] twoRows = new byte[24];
        for (int i = 0; i < twoRows.Length; i += 3)
        {
            twoRows[i] = 255;
        }

        TestPdf pdf = new TestPdf();
        int image = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 4/Height 4/ColorSpace/DeviceRGB/BitsPerComponent 8>>", twoRows);
        using PdfDocument doc = TestPdf.Open(Page(pdf, "q 160 0 0 160 20 20 cm /Im Do Q", "<</XObject<</Im " + N(image) + " 0 R>>>>"), warnings);

        PixelBuffer page = Raster(doc, HintingMode.Off);

        Pixel(page, 100, 40).Should().Be(((byte)255, (byte)0, (byte)0));
        Pixel(page, 100, 160).Should().Be(((byte)0, (byte)0, (byte)0));
        warnings.Should().Contain(w => w.Kind == PdfWarningKind.StreamDamaged && w.Reason.Contains("ends early", StringComparison.Ordinal));
    }

    // ── Builders ───────────────────────────────────────────────────────────

    private static byte[] InlineImagePage(string kind)
    {
        // Each image is 4 x 4 black (or an all-painting mask) drawn at
        // (20, 20) size 60; a blue square follows at (120, 20).
        string resources = "<<>>";
        byte[] head;
        byte[] data;
        switch (kind)
        {
            case "ASCIIHex":
                head = Latin1("BI /W 4 /H 4 /BPC 8 /CS /G /F /AHx ID ");
                data = Latin1(new string('0', 32) + ">");
                break;
            case "Flate":
                head = Latin1("BI /W 4 /H 4 /BPC 8 /CS /G /F /Fl ID ");
                data = Zlib(new byte[16]);
                break;
            case "named colour space":
                resources = "<</ColorSpace<</CS0/DeviceGray>>>>";
                head = Latin1("BI /W 4 /H 4 /BPC 8 /CS /CS0 ID ");
                data = new byte[16];
                break;
            case "image mask":
                head = Latin1("0 g BI /W 8 /H 8 /IM true ID ");
                data = new byte[8];
                break;
            case "CR LF after ID":
                head = Latin1("BI /W 4 /H 4 /BPC 8 /CS /G ID\r\n");
                data = new byte[16];
                break;
            default:
                head = Latin1("BI /W 4 /H 4 /BPC 8 /CS /G ID ");
                data = new byte[] { 0, 0x3C, 0, 0x28, 0, 0x29, 0, 0x5B, 0, 0x45, 0x49, 0, 0, 0, 0, 0 };
                break;
        }

        using MemoryStream content = new MemoryStream();
        content.Write(Latin1("q 60 0 0 60 20 20 cm "));
        content.Write(head);
        content.Write(data);
        content.Write(Latin1("\nEI Q 0 0 1 rg 120 20 60 60 re f"));

        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int contents = pdf.AddStream("<<>>", content.ToArray());
        int page = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources " + resources + "/Contents " + N(contents) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        return pdf.Build(catalog);
    }

    private static byte[] JpegPage(string decode)
    {
        byte[] jpeg = File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "Images", "cmyk-cyan-mupdf.jpg"));
        TestPdf pdf = new TestPdf();
        int image = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 16/Height 8/ColorSpace/DeviceCMYK/BitsPerComponent 8" + decode + "/Filter/DCTDecode>>", jpeg);
        return Page(pdf, "q 160 0 0 160 20 20 cm /Im Do Q", "<</XObject<</Im " + N(image) + " 0 R>>>>");
    }

    private static byte[] TrueTypePage(byte[] program, string content)
    {
        TestPdf pdf = new TestPdf();
        int file = pdf.AddStream("<</Length1 " + N(program.Length) + ">>", program);
        int fd = pdf.Add("<</Type/FontDescriptor/FontName/ABCDEF+LiPiSans/Flags 32/FontBBox[0 0 1000 1000]/ItalicAngle 0/Ascent 900/Descent -200/CapHeight 700/StemV 80/FontFile2 "
            + N(file) + " 0 R>>");
        string font = "<</Type/Font/Subtype/TrueType/BaseFont/ABCDEF+LiPiSans/Encoding/WinAnsiEncoding/FontDescriptor " + N(fd) + " 0 R>>";
        return Page(pdf, content, "<</Font<</F1 " + font + ">>>>");
    }

    private static byte[] SquarePage() =>
        TestPdf.OnePage("0 0 1 rg 20 20 60 60 re f", "<<>>", Array.Empty<string>());

    private static byte[] Page(TestPdf pdf, string content, string resources)
    {
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int contents = pdf.AddStream("<<>>", content);
        int page = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources " + resources + "/Contents " + N(contents) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        return pdf.Build(catalog);
    }

    // A minimal Type 1 program whose code 65 (/A) is a 100 x 100 square.
    private static byte[] Type1Program()
    {
        byte[] square = { 139, 248, 136, 13, 239, 139, 5, 139, 239, 5, 39, 139, 5, 9, 14 };
        string clear = "%!PS-AdobeFont-1.0: Helvetica\n/FontName /Helvetica def\n/FontMatrix [0.001 0 0 0.001 0 0] readonly def\n/Encoding 256 array\n"
            + "0 1 255 {1 index exch /.notdef put} for\ndup 65 /A put\nreadonly def\ncurrentfile eexec\n";
        byte[] cs = Encrypt(square, 4330);
        List<byte> priv = new List<byte>();
        priv.AddRange(Latin1("dup /Private 8 dict dup begin /lenIV 4 def\n/CharStrings 1 dict dup begin\n/A " + N(cs.Length) + " RD "));
        priv.AddRange(cs);
        priv.AddRange(Latin1(" ND\nend\nend\n"));
        List<byte> program = new List<byte>(Latin1(clear));
        program.AddRange(Encrypt(priv.ToArray(), 55665));
        return program.ToArray();
    }

    // Type 1 encryption with four leading bytes (lenIV 4).
    private static byte[] Encrypt(byte[] plain, ushort key)
    {
        byte[] input = new byte[plain.Length + 4];
        Array.Copy(plain, 0, input, 4, plain.Length);
        byte[] output = new byte[input.Length];
        ushort r = key;
        for (int i = 0; i < input.Length; i++)
        {
            byte c = (byte)(input[i] ^ (r >> 8));
            output[i] = c;
            r = (ushort)(((c + r) * 52845) + 22719);
        }

        return output;
    }

    private static byte[] Zlib(byte[] data)
    {
        using MemoryStream ms = new MemoryStream();
        using (ZLibStream z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(data);
        }

        return ms.ToArray();
    }

    private static PixelBuffer Raster(PdfDocument doc, HintingMode hinting) =>
        new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72, Hinting = hinting }).Rasterize(doc.Pages[0]);

    // The pixel at a device position (y down from the top of the page).
    private static (byte R, byte G, byte B) Pixel(PixelBuffer page, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = page.GetPixelBgra(x, y);
        return (r, g, b);
    }

    private static int Ink(PixelBuffer page) => InkIn(page, 0, 0, 200, 200);

    // Dark pixels inside a PDF-space rectangle of a 200 x 200 page.
    private static int InkIn(PixelBuffer page, int x0, int y0, int x1, int y1)
    {
        int count = 0;
        for (int y = 200 - y1; y < 200 - y0; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                (byte r, byte g, byte b) = Pixel(page, x, y);
                if ((r + g + b) / 3 < 200)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int Differing(PixelBuffer a, PixelBuffer b)
    {
        int count = 0;
        for (int y = 0; y < 200; y++)
        {
            for (int x = 0; x < 200; x++)
            {
                if (a.GetPixelBgra(x, y) != b.GetPixelBgra(x, y))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
