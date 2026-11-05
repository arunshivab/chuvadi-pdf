// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 (D-169) — a character above U+FFFF in a PDF: the structured
//        display list and the raster renderer pick its glyph through the
//        format 12 cmap, the SVG font embedder maps it, and Authoring draws it.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Chuvadi.Fonts;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Rendering.DisplayList;
using Chuvadi.Pdf.Svg;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class CmapAboveBmpPdf331Tests
{
    // U+1F130 SQUARED LATIN CAPITAL LETTER A is glyph 1964 in LiPi Sans Latin
    // (the oracle's vector); its format 4 subtables cannot hold it.
    private const int SquaredA = 0x1F130;
    private const int SquaredAGlyph = 1964;

    private const string ToUnicodeSquaredA = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CIDSystemInfo<</Registry(Adobe)/Ordering(UCS)/Supplement 0>> def /CMapName/Adobe-Identity-UCS def /CMapType 2 def 1 begincodespacerange <00> <FF> endcodespacerange 1 beginbfchar <41> <D83CDD30> endbfchar endcmap CMapName currentdict /CMap defineresource pop end end";

    [Fact]
    public void ATrueTypeSimpleFont_TakesItsGlyphFromTheEncoding_NotFromToUnicode()
    {
        // D-175: code 65 is "A" by WinAnsiEncoding; ToUnicode says U+1F130.
        // The glyph is A's (PDF 32000-1:2008 §9.6.6.4, as MuPDF draws it);
        // the text is still U+1F130.
        byte[] program = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        int glyphA = FontFace.Load(program).GetGlyphIndex('A');
        using PdfDocument doc = TestPdf.Open(SimpleFontPdf(program, "(A) Tj"));

        DisplayListFont f = DisplayListBuilder.Build(doc, 0).Fonts.Should().ContainSingle().Subject;
        f.GetGlyphId(65).Should().Be(glyphA);

        using PdfDocument direct = TestPdf.Open(Type0Pdf(program, glyphA));
        int ink = TestPdf.Ink(doc, AnnotationMode.View, 0, 0, 200, 200);
        ink.Should().BeGreaterThan(20);
        ink.Should().Be(TestPdf.Ink(direct, AnnotationMode.View, 0, 0, 200, 200));

        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Should().Contain(char.ConvertFromUtf32(SquaredA));
    }

    [Fact]
    public void ACffSimpleFont_WhoseToUnicodeGivesASurrogatePair_GetsTheSfntCmapGlyph()
    {
        // CFF programs keep the ToUnicode-first order; the whole surrogate
        // pair is looked up (D-169), in the sfnt cmap first.
        byte[] otf = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "ChuvadiTest-Cff.otf"));
        int last = new FontRenderer(otf).NumGlyphs - 1;
        byte[] patched = WithCmap(otf, Format12Cmap(SquaredA, last));
        TestPdf pdf = new TestPdf();
        int file = pdf.AddStream("<</Subtype/OpenType>>", patched);
        int fd = pdf.Add("<</Type/FontDescriptor/FontName/ABCDEF+Test/Flags 32/FontBBox[0 0 1000 1000]/ItalicAngle 0/Ascent 900/Descent -200/CapHeight 700/StemV 80/FontFile3 " + file.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        int cmap = pdf.AddStream("<<>>", ToUnicodeSquaredA);
        string font = "<</Type/Font/Subtype/Type1/BaseFont/ABCDEF+Test/FirstChar 65/LastChar 65/Widths[800]/ToUnicode "
            + cmap.ToString(CultureInfo.InvariantCulture) + " 0 R/FontDescriptor " + fd.ToString(CultureInfo.InvariantCulture) + " 0 R>>";
        using PdfDocument doc = TestPdf.Open(OnePageWith(pdf, "BT /F1 60 Tf 20 80 Td (A) Tj ET", "<</Font<</F1 " + font + ">>>>"));

        DisplayListFont f = DisplayListBuilder.Build(doc, 0).Fonts.Should().ContainSingle().Subject;
        f.GetGlyphId(65).Should().Be(last);
    }

    [Fact]
    public void TheSvgFontEmbedder_MapsASurrogatePair_InAFontWithOnlyAMacCmap()
    {
        byte[] lipi = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        byte[] macOnly = WithCmap(lipi, MacFormat6(65, SquaredAGlyph));
        using PdfDocument doc = TestPdf.Open(SimpleFontPdf(macOnly, "(A) Tj"));

        string svg = new SvgRenderer().RenderPage(doc, 0);
        Match m = Regex.Match(svg, "base64,([A-Za-z0-9+/=]+)\\)");
        m.Success.Should().BeTrue();
        FontFace embedded = FontFace.Load(Convert.FromBase64String(m.Groups[1].Value));
        embedded.GetGlyphIndex(SquaredA).Should().Be(SquaredAGlyph, "the browser finds the glyph by the character the text shows");
    }

    [Fact]
    public void Authoring_DrawsACharacterAboveTheBmp_WithItsGlyph()
    {
        string text = "A" + char.ConvertFromUtf32(SquaredA) + "B";
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().UseLipiFonts();
        builder.AddPage(PageSize.A4).DrawText(text, 40, 100, "Lipi", 24, Colors.Black);
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(builder.ToByteArray()));

        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Should().Contain(text);
        PageDisplayList list = DisplayListBuilder.Build(doc, 0);
        List<DisplayListGlyph> glyphs = list.OfType<TextOp>().SelectMany(t => t.Glyphs).ToList();
        glyphs.Should().HaveCount(3);
        DisplayListFont font = list.Fonts.Should().ContainSingle().Subject;
        foreach (DisplayListGlyph g in glyphs)
        {
            int gid = font.GetGlyphId(g.Code);
            gid.Should().BeGreaterThan(0, "no character is drawn as .notdef");
            font.GetGlyphOutline(gid)!.Outline.IsEmpty.Should().BeFalse();
        }
    }

    private static byte[] SimpleFontPdf(byte[] program, string show)
    {
        TestPdf pdf = new TestPdf();
        int file = pdf.AddStream("<</Length1 " + program.Length.ToString(CultureInfo.InvariantCulture) + ">>", program);
        int fd = pdf.Add("<</Type/FontDescriptor/FontName/ABCDEF+LiPiSans/Flags 32/FontBBox[0 0 1000 1000]/ItalicAngle 0/Ascent 900/Descent -200/CapHeight 700/StemV 80/FontFile2 " + file.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        int cmap = pdf.AddStream("<<>>", ToUnicodeSquaredA);
        string font = "<</Type/Font/Subtype/TrueType/BaseFont/ABCDEF+LiPiSans/FirstChar 65/LastChar 65/Widths[800]/Encoding/WinAnsiEncoding/ToUnicode "
            + cmap.ToString(CultureInfo.InvariantCulture) + " 0 R/FontDescriptor " + fd.ToString(CultureInfo.InvariantCulture) + " 0 R>>";
        return OnePageWith(pdf, "BT /F1 60 Tf 20 80 Td " + show + " ET", "<</Font<</F1 " + font + ">>>>");
    }

    private static byte[] Type0Pdf(byte[] program, int gid)
    {
        TestPdf pdf = new TestPdf();
        int file = pdf.AddStream("<</Length1 " + program.Length.ToString(CultureInfo.InvariantCulture) + ">>", program);
        int fd = pdf.Add("<</Type/FontDescriptor/FontName/ABCDEF+LiPiSans/Flags 32/FontBBox[0 0 1000 1000]/ItalicAngle 0/Ascent 900/Descent -200/CapHeight 700/StemV 80/FontFile2 " + file.ToString(CultureInfo.InvariantCulture) + " 0 R>>");
        string font = "<</Type/Font/Subtype/Type0/BaseFont/ABCDEF+LiPiSans/Encoding/Identity-H/DescendantFonts[<</Type/Font/Subtype/CIDFontType2/BaseFont/ABCDEF+LiPiSans/CIDSystemInfo<</Registry(Adobe)/Ordering(Identity)/Supplement 0>>/DW 800/CIDToGIDMap/Identity/FontDescriptor "
            + fd.ToString(CultureInfo.InvariantCulture) + " 0 R>>]>>";
        return OnePageWith(pdf, "BT /F1 60 Tf 20 80 Td <" + gid.ToString("X4", CultureInfo.InvariantCulture) + "> Tj ET", "<</Font<</F1 " + font + ">>>>");
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

    // A cmap table with one Macintosh (1,0) format 6 subtable mapping one code.
    private static byte[] MacFormat6(int code, int gid)
    {
        byte[] t = new byte[4 + 8 + 12];
        Put16(t, 2, 1);
        Put16(t, 4, 1);
        Put16(t, 6, 0);
        Put16(t, 10, 12);
        Put16(t, 12, 6);
        Put16(t, 14, 12);
        Put16(t, 18, code);
        Put16(t, 20, 1);
        Put16(t, 22, gid);
        return t;
    }

    // A cmap table with one Windows (3,10) format 12 subtable mapping one code point.
    private static byte[] Format12Cmap(int codePoint, int gid)
    {
        byte[] t = new byte[4 + 8 + 28];
        Put16(t, 2, 1);
        Put16(t, 4, 3);
        Put16(t, 6, 10);
        Put32(t, 8, 12);
        Put16(t, 12, 12);
        Put32(t, 16, 28);
        Put32(t, 24, 1);
        Put32(t, 28, codePoint);
        Put32(t, 32, codePoint);
        Put32(t, 36, gid);
        return t;
    }

    // The font with its cmap table replaced (the loader does not check table checksums).
    private static byte[] WithCmap(byte[] font, byte[] cmap)
    {
        int numTables = (font[4] << 8) | font[5];
        List<(byte[] Record, byte[] Data)> tables = new List<(byte[], byte[])>();
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + (i * 16);
            bool isCmap = font[rec] == (byte)'c' && font[rec + 1] == (byte)'m' && font[rec + 2] == (byte)'a' && font[rec + 3] == (byte)'p';
            int off = Get32(font, rec + 8);
            int len = Get32(font, rec + 12);
            tables.Add((font.AsSpan(rec, 16).ToArray(), isCmap ? cmap : font.AsSpan(off, len).ToArray()));
        }

        using MemoryStream ms = new MemoryStream();
        ms.Write(font, 0, 12);
        int dataOffset = 12 + (numTables * 16);
        foreach ((byte[] record, byte[] data) in tables)
        {
            byte[] r = (byte[])record.Clone();
            Put32(r, 8, dataOffset);
            Put32(r, 12, data.Length);
            ms.Write(r);
            dataOffset += (data.Length + 3) & ~3;
        }

        foreach ((byte[] _, byte[] data) in tables)
        {
            ms.Write(data);
            ms.Write(new byte[((data.Length + 3) & ~3) - data.Length]);
        }

        return ms.ToArray();
    }

    private static int Get32(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

    private static void Put16(byte[] d, int o, int v)
    {
        d[o] = (byte)(v >> 8);
        d[o + 1] = (byte)v;
    }

    private static void Put32(byte[] d, int o, int v)
    {
        d[o] = (byte)(v >> 24);
        d[o + 1] = (byte)(v >> 16);
        d[o + 2] = (byte)(v >> 8);
        d[o + 3] = (byte)v;
    }
}
