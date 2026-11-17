// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7.5.2 (predefined CMaps), §9.7.4.2 (CIDToGIDMap)
// PHASE: 3.35.0 (E1, D-352) — a Type 0 font with a predefined CJK CMap
//        draws the glyph of the code's CID, in the raster renderer and the
//        structured display list.

using System.Globalization;
using System.Linq;
using Chuvadi.Fonts;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Rendering.DisplayList;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class PredefinedCMapRender3350Tests
{
    // In 90ms-RKSJ-H the Shift-JIS code 82A0 (hiragana A) is CID 843.
    private const int HiraganaACid = 843;

    [Fact]
    public void AShiftJisCode_DrawsTheGlyphOfItsCid()
    {
        byte[] program = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        int glyphA = FontFace.Load(program).GetGlyphIndex('A');
        using PdfDocument viaCMap = TestPdf.Open(Pdf(program, "90ms-RKSJ-H", "<82A0>", glyphA));
        using PdfDocument identity = TestPdf.Open(Pdf(program, "Identity-H", "<" + HiraganaACid.ToString("X4", CultureInfo.InvariantCulture) + ">", glyphA));

        int ink = TestPdf.Ink(viaCMap, AnnotationMode.View, 0, 0, 200, 200);
        ink.Should().BeGreaterThan(20, "code 82A0 selects CID 843, which the map sends to A");
        ink.Should().Be(TestPdf.Ink(identity, AnnotationMode.View, 0, 0, 200, 200));

        DisplayListGlyph glyph = DisplayListBuilder.Build(viaCMap, 0).OfType<TextOp>().SelectMany(t => t.Glyphs).Should().ContainSingle().Subject;
        glyph.GlyphId.Should().Be(HiraganaACid);
        glyph.CodeLength.Should().Be(2);
        glyph.Code.Should().Be(0x82A0);
    }

    // A Type 0 font over the program whose CIDToGIDMap sends CID 843 to gid.
    private static byte[] Pdf(byte[] program, string encoding, string show, int gid)
    {
        TestPdf pdf = new TestPdf();
        int file = pdf.AddStream("<</Length1 " + program.Length.ToString(CultureInfo.InvariantCulture) + ">>", program);
        byte[] map = new byte[(HiraganaACid + 1) * 2];
        map[HiraganaACid * 2] = (byte)(gid >> 8);
        map[(HiraganaACid * 2) + 1] = (byte)gid;
        int cidToGid = pdf.AddStream("<<>>", map);
        int fd = pdf.Add("<</Type/FontDescriptor/FontName/ABCDEF+LiPiSans/Flags 4/FontBBox[0 0 1000 1000]/ItalicAngle 0/Ascent 900/Descent -200/CapHeight 700/StemV 80/FontFile2 " + file.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        string font = "<</Type/Font/Subtype/Type0/BaseFont/ABCDEF+LiPiSans/Encoding/" + encoding
            + "/DescendantFonts[<</Type/Font/Subtype/CIDFontType2/BaseFont/ABCDEF+LiPiSans/CIDSystemInfo<</Registry(Adobe)/Ordering(Japan1)/Supplement 6>>/DW 800/CIDToGIDMap "
            + cidToGid.ToString(CultureInfo.InvariantCulture) + " 0 R/FontDescriptor " + fd.ToString(CultureInfo.InvariantCulture) + " 0 R>>]>>";
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int page = pdf.Reserve();
        int contents = pdf.AddStream("<<>>", "BT /F1 60 Tf 20 80 Td " + show + " Tj ET");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + pages.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + page.ToString(CultureInfo.InvariantCulture) + " 0 R]/Count 1>>");
        pdf.Set(page, "<</Type/Page/Parent " + pages.ToString(CultureInfo.InvariantCulture) + " 0 R/MediaBox[0 0 200 200]/Resources <</Font<</F1 " + font + ">>>>"
            + "/Contents " + contents.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        return pdf.Build(catalog);
    }
}
