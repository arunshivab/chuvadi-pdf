// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — what Print needs from the structured display list (Print
// items a to i), the O9 font-cache fix, the LiPi Bold faces (D-152 option B),
// deterministic SVG ids (#32) and the moved shared types (D-143).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Chuvadi.Fonts;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering.DisplayList;
using Chuvadi.Pdf.Svg;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class PrintSupport331Tests
{
    private const string HelvResources = "<</Font<</F1<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>>>>>";

    private static readonly string[] MovedTypes =
    {
        "Chuvadi.Pdf.Rendering.DisplayList.PdfBlendMode", "Chuvadi.Pdf.Rendering.DisplayList.BlendModes",
        "Chuvadi.Pdf.Rendering.DisplayList.Type3Font", "Chuvadi.Pdf.Rendering.DisplayList.Type3Glyph",
    };

    [Fact]
    public void A1_TheSharedTypes_LiveInWalking_AndAreForwardedFromDisplayList()
    {
        Assembly displayList = typeof(DisplayListBuilder).Assembly;
        foreach (string name in MovedTypes)
        {
            Type? t = displayList.GetType(name);
            t.Should().NotBeNull(name);
            t!.Assembly.GetName().Name.Should().Be("Chuvadi.Pdf.Rendering.Walking");
        }

        typeof(Chuvadi.Pdf.Rendering.Raster.PageDisplayList).Assembly.GetReferencedAssemblies().Select(a => a.Name)
            .Should().NotContain("Chuvadi.Pdf.Rendering.DisplayList");
    }

    [Fact]
    public void H_PageBoxes_AreOnTheDisplayList_ClippedAndDefaulted()
    {
        byte[] pdf = TestPdf.OnePage("", HelvResources, Array.Empty<string>(), extraPage: "/CropBox[10 10 190 190]/TrimBox[20 20 180 180]/BleedBox[0 0 300 300]");
        using PdfDocument doc = TestPdf.Open(pdf);

        PageDisplayList list = DisplayListBuilder.Build(doc, 0);
        list.MediaBox.Width.Should().Be(200);
        list.CropBox.X1.Should().Be(10);
        list.TrimBox.X1.Should().Be(20);
        list.BleedBox.X2.Should().Be(190, "the BleedBox is clipped to the CropBox");
        list.ArtBox.X1.Should().Be(10, "the ArtBox defaults to the CropBox");
    }

    [Fact]
    public void O9_TheSameFontNameInAPageAndAForm_GivesTwoFonts()
    {
        TestPdf pdf = new TestPdf();
        int cmap = pdf.AddStream("<<>>", "/CIDInit /ProcSet findresource begin 12 dict begin begincmap 1 begincodespacerange <00> <FF> endcodespacerange 1 beginbfchar <41> <0042> endbfchar endcmap end end");
        int form = pdf.AddStream(
            "<</Type/XObject/Subtype/Form/BBox[0 0 200 200]/Resources<</Font<</F1<</Type/Font/Subtype/Type1/BaseFont/Courier/ToUnicode " + cmap.ToString(CultureInfo.InvariantCulture) + " 0 R>>>>>>>>",
            "BT /F1 12 Tf 10 50 Td (A) Tj ET");
        byte[] bytes = OnePageWith(pdf, "BT /F1 12 Tf 10 150 Td (A) Tj ET /X1 Do",
            "<</Font<</F1<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>>>/XObject<</X1 " + form.ToString(CultureInfo.InvariantCulture) + " 0 R>>>>");
        using PdfDocument doc = TestPdf.Open(bytes);

        List<TextOp> texts = DisplayListBuilder.Build(doc, 0).OfType<TextOp>().ToList();
        texts.Should().HaveCount(2);
        texts[0].FontKey.Should().Be("F1");
        texts[0].BaseFont.Should().Be("Helvetica");
        texts[1].FontKey.Should().Be("F1#2");
        texts[1].BaseFont.Should().Be("Courier");
        texts[1].Glyphs[0].Unicode.Should().Be("B", "the form's own encoding decodes its text (before 3.31 it used the page's font)");
        texts[1].Font!.BaseFont.Should().Be("Courier");
    }

    [Fact]
    public void B_C_D_AnEmbeddedFont_GivesItsProgram_Glyphs_AndRights()
    {
        byte[] program = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        TestPdf pdf = new TestPdf();
        int file = pdf.AddStream("<</Length1 " + program.Length.ToString(CultureInfo.InvariantCulture) + ">>", program);
        int fd = pdf.Add("<</Type/FontDescriptor/FontName/ABCDEF+LiPiSans/Flags 32/FontBBox[0 0 1000 1000]/ItalicAngle 0/Ascent 900/Descent -200/CapHeight 700/StemV 80/FontFile2 " + file.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        string font = "<</Type/Font/Subtype/TrueType/BaseFont/ABCDEF+LiPiSans/FirstChar 65/LastChar 65/Widths[600]/Encoding/WinAnsiEncoding/FontDescriptor " + fd.ToString(CultureInfo.InvariantCulture) + " 0 R>>";
        byte[] bytes = OnePageWith(pdf, "BT /F1 12 Tf 10 150 Td (A) Tj ET", "<</Font<</F1 " + font + ">>>>");
        using PdfDocument doc = TestPdf.Open(bytes);

        PageDisplayList list = DisplayListBuilder.Build(doc, 0);
        DisplayListFont f = list.Fonts.Should().ContainSingle().Subject;
        f.IsEmbedded.Should().BeTrue();
        f.IsSubset.Should().BeTrue();
        f.ProgramKind.Should().Be(FontProgramKind.TrueType);
        f.GetProgram().Should().Equal(program);
        f.UnitsPerEm.Should().BeGreaterThan(0);
        f.GetWidth(65).Should().Be(600);
        int gid = f.GetGlyphId(65);
        gid.Should().BeGreaterThan(0);
        f.GetGlyphOutline(gid)!.Outline.IsEmpty.Should().BeFalse();
        f.EmbeddingRights.Permission.Should().NotBe(FontEmbeddingPermission.Unknown);
        list.OfType<TextOp>().Single().Glyphs[0].Code.Should().Be(65);
        list.OfType<TextOp>().Single().Glyphs[0].CodeLength.Should().Be(1);

        FontEmbeddingReport.Check(doc).Verdict.Should().Be(FontEmbeddingVerdict.AllEmbedded);
    }

    [Fact]
    public void B_C_D_AFontThatIsNotEmbedded_GivesItsSubstitute_AndFailsTheVerdict()
    {
        byte[] bytes = TestPdf.OnePage("BT /F1 12 Tf 10 150 Td (A) Tj ET", HelvResources, Array.Empty<string>());
        using PdfDocument doc = TestPdf.Open(bytes);

        DisplayListFont f = DisplayListBuilder.Build(doc, 0).Fonts.Single();
        f.IsEmbedded.Should().BeFalse();
        f.Substitute.Should().Be("Helvetica");
        f.GetGlyphId(65).Should().Be(-1);
        f.GetWidth(65).Should().Be(667);
        f.GetSubstituteGlyphOutline(65)!.IsEmpty.Should().BeFalse();

        FontEmbeddingReport report = FontEmbeddingReport.Check(doc);
        report.Verdict.Should().Be(FontEmbeddingVerdict.NotAllEmbedded);
        report.NotEmbedded.Should().ContainSingle(u => u.BaseFont == "Helvetica" && u.PageNumber == 1 && u.Location == "page");
    }

    [Fact]
    public void D_TheVerdict_FindsFontsInFormsAndAnnotations_AndGeneratedAppearances()
    {
        byte[] bytes = TestPdf.OnePage(
            string.Empty,
            "<<>>",
            new[] { "<</Type/Annot/Subtype/Widget/FT/Tx/T(f)/V(x)/Rect[20 20 120 40]/F 4/DA(/Helv 10 Tf 0 g)>>" },
            "<</Fields[FIELDS]/NeedAppearances true>>");
        using PdfDocument doc = TestPdf.Open(bytes);

        FontEmbeddingReport report = FontEmbeddingReport.Check(doc);
        report.NeedsGeneratedAppearances.Should().BeTrue();
        report.Verdict.Should().Be(FontEmbeddingVerdict.AllEmbedded, "the file itself names no font");
    }

    [Fact]
    public void G_AMeshShading_IsAnOpMarkedNotExpressible_AndSvgIgnoresIt()
    {
        TestPdf pdf = new TestPdf();
        int mesh = pdf.AddStream("<</ShadingType 4/ColorSpace/DeviceRGB/BitsPerCoordinate 8/BitsPerComponent 8/BitsPerFlag 8/Decode[0 200 0 200 0 1 0 1 0 1]>>", new byte[] { 0, 0, 0, 255, 0, 0, 0, 200, 0, 0, 255, 0, 0, 0, 200, 0, 0, 255 });
        byte[] withShading = OnePageWith(pdf, "/Sh1 sh", "<</Shading<</Sh1 " + mesh.ToString(CultureInfo.InvariantCulture) + " 0 R>>>>");
        using PdfDocument doc = TestPdf.Open(withShading);

        ShadingOp op = DisplayListBuilder.Build(doc, 0).OfType<ShadingOp>().Should().ContainSingle().Subject;
        op.IsExpressible.Should().BeFalse();
        op.ShadingType.Should().Be(4);
        op.GetShadingData().Should().HaveCount(18);

        using PdfDocument empty = TestPdf.Open(TestPdf.OnePage(string.Empty, "<<>>", Array.Empty<string>()));
        string a = new SvgRenderer().RenderPage(doc, 0);
        string b = new SvgRenderer().RenderPage(empty, 0);
        a.Should().Be(b, "SVG skips shadings it cannot express, as before 3.31");
    }

    [Fact]
    public void F_AJpeg_ReportsItsColourTransform_AndAStencilMaskItsColour()
    {
        // Marker segments only: SOI, APP14 "Adobe" with transform 2 (YCCK), SOF0 with 4 components, SOS.
        byte[] jpeg =
        {
            0xFF, 0xD8, 0xFF, 0xEE, 0x00, 0x0E, (byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e', 0, 100, 0, 0, 0, 0, 2,
            0xFF, 0xC0, 0x00, 0x14, 8, 0, 1, 0, 1, 4, 1, 0x11, 0, 2, 0x11, 0, 3, 0x11, 0, 4, 0x11, 0,
            0xFF, 0xDA, 0x00, 0x02, 0xFF, 0xD9,
        };
        TestPdf pdf = new TestPdf();
        int img = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 1/Height 1/BitsPerComponent 8/ColorSpace/DeviceCMYK/Filter/DCTDecode/Decode[1 0 1 0 1 0 1 0]>>", jpeg);
        int mask = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 8/Height 1/ImageMask true>>", new byte[] { 0xAA });
        byte[] bytes = OnePageWith(pdf, "q 50 0 0 50 10 10 cm /I1 Do Q q 0 0 1 rg 50 0 0 50 100 10 cm /M1 Do Q",
            "<</XObject<</I1 " + img.ToString(CultureInfo.InvariantCulture) + " 0 R/M1 " + mask.ToString(CultureInfo.InvariantCulture) + " 0 R>>>>");
        using PdfDocument doc = TestPdf.Open(bytes);

        List<ImageOp> images = DisplayListBuilder.Build(doc, 0).OfType<ImageOp>().ToList();
        ImageOp photo = images.Single(i => i.Format == ImageFormat.Jpeg);
        photo.PixelData.Should().Equal(jpeg, "the original JPEG bytes pass through");
        photo.JpegColorTransform.Should().Be(JpegColorTransform.Ycck);
        photo.IsCmykJpeg.Should().BeTrue();
        photo.Decode.Should().Equal(1, 0, 1, 0, 1, 0, 1, 0);
        ImageOp stencil = images.Single(i => i.IsStencilMask);
        stencil.StencilColor.ToSrgb().B.Should().BeApproximately(1, 0.001);
    }

    [Fact]
    public void F_AnIccBasedImage_GivesItsProfileBytes()
    {
        byte[] profile = Encoding.ASCII.GetBytes("not a real profile, only bytes to carry");
        TestPdf pdf = new TestPdf();
        int icc = pdf.AddStream("<</N 3>>", profile);
        int img = pdf.AddStream("<</Type/XObject/Subtype/Image/Width 1/Height 1/BitsPerComponent 8/ColorSpace[/ICCBased " + icc.ToString(CultureInfo.InvariantCulture) + " 0 R]>>", new byte[] { 10, 20, 30 });
        byte[] bytes = OnePageWith(pdf, "q 50 0 0 50 10 10 cm /I1 Do Q", "<</XObject<</I1 " + img.ToString(CultureInfo.InvariantCulture) + " 0 R>>>>");
        using PdfDocument doc = TestPdf.Open(bytes);

        DisplayListBuilder.Build(doc, 0).OfType<ImageOp>().Single().GetIccProfile().Should().Equal(profile);
    }

    [Fact]
    public void I_OpsInsideAKnockoutGroup_CarryItsId_AndTheTableHasItsFlags()
    {
        TestPdf pdf = new TestPdf();
        int form = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 200 200]/Group<</S/Transparency/I true/K true/CS/DeviceRGB>>>>", "0 0 1 rg 10 10 50 50 re f");
        byte[] bytes = OnePageWith(pdf, "1 0 0 rg 100 100 50 50 re f /X1 Do", "<</XObject<</X1 " + form.ToString(CultureInfo.InvariantCulture) + " 0 R>>>>");
        using PdfDocument doc = TestPdf.Open(bytes);

        PageDisplayList list = DisplayListBuilder.Build(doc, 0);
        List<PathOp> paths = list.OfType<PathOp>().ToList();
        paths[0].GroupIds.Should().BeEmpty();
        paths[0].GroupTransparency.Should().BeNull();
        paths[1].GroupIds.Should().Equal(1);
        TransparencyGroup g = list.TransparencyGroups.Should().ContainSingle().Subject;
        g.Id.Should().Be(1);
        g.ParentId.Should().BeNull();
        g.Depth.Should().Be(1);
        g.Isolated.Should().BeTrue();
        g.Knockout.Should().BeTrue();
        g.ColorSpace.Should().Be("DeviceRGB");
        list.HasTransparency.Should().BeTrue();
        GroupInvariants.Check(list);
    }

    [Fact]
    public void SvgIds_AreTheSameOnEveryRun_AndComeFromAFixedHash()
    {
        byte[] bytes = TestPdf.OnePage("q 10 10 100 100 re W n 0 0 1 rg 0 0 200 200 re f Q", "<<>>", Array.Empty<string>());
        using PdfDocument doc = TestPdf.Open(bytes);

        string svg = new SvgRenderer().RenderPage(doc, 0);
        svg.Should().Be(new SvgRenderer().RenderPage(doc, 0));
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(svg, "clipPath id=\"clip[0-9A-F]+_([0-9A-F]+)\"><path d=\"([^\"]+)\"");
        m.Success.Should().BeTrue();
        m.Groups[1].Value.Should().Be(Fnv1a(System.Net.WebUtility.HtmlDecode(m.Groups[2].Value)).ToString("X", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void OptionB_BoldIndicText_InAFontThatIsNotEmbedded_UsesTheBoldFace()
    {
        int regular = TamilInk("Latha");
        int bold = TamilInk("Latha,Bold");

        regular.Should().BeGreaterThan(50);
        bold.Should().BeGreaterThan((int)(regular * 1.15), "LiPi Sans Tamil Bold has heavier strokes than Regular");
    }

    // Ink of a non-embedded Type 0 font showing Tamil (CIDs mapped through ToUnicode).
    private static int TamilInk(string baseFont)
    {
        TestPdf pdf = new TestPdf();
        int toUnicode = pdf.AddStream("<<>>", "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /T def 1 begincodespacerange <0000> <FFFF> endcodespacerange 4 beginbfchar <0001> <0B85> <0002> <0BB0> <0003> <0BC1> <0004> <0BA3> endbfchar endcmap end end");
        string font = "<</Type/Font/Subtype/Type0/BaseFont/" + baseFont + "/Encoding/Identity-H/ToUnicode " + toUnicode.ToString(CultureInfo.InvariantCulture)
            + " 0 R/DescendantFonts[<</Type/Font/Subtype/CIDFontType2/BaseFont/" + baseFont + "/CIDSystemInfo<</Registry(Adobe)/Ordering(Identity)/Supplement 0>>/DW 1000"
            + "/FontDescriptor<</Type/FontDescriptor/FontName/" + baseFont + "/Flags 32/FontBBox[0 0 1000 1000]/ItalicAngle 0/Ascent 900/Descent -200/CapHeight 700/StemV 80>>>>]>>";
        byte[] bytes = OnePageWith(pdf, "BT /F1 40 Tf 10 100 Td <0001000200030004> Tj ET", "<</Font<</F1 " + font + ">>>>");
        using PdfDocument doc = TestPdf.Open(bytes);
        return TestPdf.Ink(doc, AnnotationMode.View, 0, 60, 200, 160);
    }

    private static uint Fnv1a(string text)
    {
        uint hash = 2166136261u;
        foreach (char c in text)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return hash;
    }

    private static byte[] OnePageWith(TestPdf pdf, string content, string resources)
    {
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int page = pdf.Reserve();
        int contents = pdf.AddStream("<<>>", content);
        pdf.Set(catalog, "<</Type/Catalog/Pages " + pages.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + page.ToString(CultureInfo.InvariantCulture) + " 0 R]/Count 1>>");
        pdf.Set(page, "<</Type/Page/Parent " + pages.ToString(CultureInfo.InvariantCulture) + " 0 R/MediaBox[0 0 200 200]/Resources " + resources
            + "/Contents " + contents.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        return pdf.Build(catalog);
    }
}
