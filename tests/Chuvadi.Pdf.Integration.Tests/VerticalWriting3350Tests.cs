// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7.4.3 (vertical metrics, /W2 /DW2), §9.4.4
// PHASE: 3.35.0 (E2) — vertical writing in the raster renderer, the
//        structured display list and text extraction.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chuvadi.Fonts;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Rendering.DisplayList;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class VerticalWriting3350Tests
{
    // Two A glyphs at size 60 from (100, 180) with Identity-V and the default
    // /DW2 [880 -1000]: each is drawn 24 units left (half its width of 800)
    // and 52.8 down of its origin, and the second origin is 60 units lower.
    [Fact]
    public void Glyphs_StackDownThePage()
    {
        using PdfDocument doc = TestPdf.Open(Pdf("Identity-V", out string show));

        TestPdf.Ink(doc, AnnotationMode.View, 70, 127, 130, 172).Should().BeGreaterThan(20, "the first glyph");
        TestPdf.Ink(doc, AnnotationMode.View, 70, 67, 130, 112).Should().BeGreaterThan(20, "the second glyph, one em lower");
        TestPdf.Ink(doc, AnnotationMode.View, 135, 0, 200, 200).Should().Be(0, "nothing is laid out to the right");
    }

    [Fact]
    public void TheDisplayList_PlacesEachGlyph()
    {
        using PdfDocument doc = TestPdf.Open(Pdf("Identity-V", out _));

        List<TextOp> ops = DisplayListBuilder.Build(doc, 0).OfType<TextOp>().ToList();
        ops.Should().HaveCount(2);
        ops[0].Transform.E.Should().BeApproximately(76, 0.01);
        ops[0].Transform.F.Should().BeApproximately(127.2, 0.01);
        ops[1].Transform.F.Should().BeApproximately(67.2, 0.01);
    }

    [Fact]
    public void Extraction_MovesDown()
    {
        using PdfDocument doc = TestPdf.Open(Pdf("Identity-V", out _, twoShows: true));

        List<TextFragment> fragments = new TextExtractor(doc.Objects).ExtractFragments(doc.Pages[0]);

        fragments.Should().HaveCount(2);
        fragments[0].Y.Should().BeApproximately(180, 0.01);
        fragments[1].Y.Should().BeApproximately(60, 0.01, "two glyphs of one em each lie above it");
        fragments[1].X.Should().BeApproximately(100, 0.01);
    }

    private static byte[] Pdf(string encoding, out string show, bool twoShows = false)
    {
        byte[] program = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        string a = FontFace.Load(program).GetGlyphIndex('A').ToString("X4", CultureInfo.InvariantCulture);
        show = "<" + a + a + ">";
        TestPdf pdf = new TestPdf();
        int file = pdf.AddStream("<</Length1 " + program.Length.ToString(CultureInfo.InvariantCulture) + ">>", program);
        int fd = pdf.Add("<</Type/FontDescriptor/FontName/ABCDEF+LiPiSans/Flags 4/FontBBox[0 0 1000 1000]/ItalicAngle 0/Ascent 900/Descent -200/CapHeight 700/StemV 80/FontFile2 " + file.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        string font = "<</Type/Font/Subtype/Type0/BaseFont/ABCDEF+LiPiSans/Encoding/" + encoding
            + "/DescendantFonts[<</Type/Font/Subtype/CIDFontType2/BaseFont/ABCDEF+LiPiSans/CIDSystemInfo<</Registry(Adobe)/Ordering(Identity)/Supplement 0>>/DW 800/CIDToGIDMap/Identity/FontDescriptor "
            + fd.ToString(CultureInfo.InvariantCulture) + " 0 R>>]>>";
        string content = "BT /F1 60 Tf 100 180 Td " + show + " Tj" + (twoShows ? " <" + a + "> Tj" : string.Empty) + " ET";
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int page = pdf.Reserve();
        int contents = pdf.AddStream("<<>>", content);
        pdf.Set(catalog, "<</Type/Catalog/Pages " + pages.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + page.ToString(CultureInfo.InvariantCulture) + " 0 R]/Count 1>>");
        pdf.Set(page, "<</Type/Page/Parent " + pages.ToString(CultureInfo.InvariantCulture) + " 0 R/MediaBox[0 0 200 200]/Resources <</Font<</F1 " + font + ">>>>"
            + "/Contents " + contents.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        return pdf.Build(catalog);
    }
}
