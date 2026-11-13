// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.6 (character encoding), §9.10.3 (ToUnicode
//        is for text extraction)
// PHASE: 3.34.0 (#36, #37; D-353) — the glyph comes from the encoding, also
//        in SVG and for substitute fonts, when ToUnicode contradicts it.

using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Svg;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class GlyphChoice3340Tests
{
    // ToUnicode says code 0x41 is "B"; the encoding (WinAnsi) says "A".
    private const string ToUnicodeAtoB =
        "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /T def 1 begincodespacerange <00> <FF> endcodespacerange "
        + "1 beginbfchar <41> <0042> endbfchar endcmap CMapName currentdict /CMap defineresource pop end end";

    [Fact]
    public void Svg_EmbeddedTrueType_DrawsTheEncodingsGlyph()
    {
        byte[] program = File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "ChuvadiTest-Glyf.ttf"));
        using PdfDocument doc = TestPdf.Open(Document(program));

        string svg = new SvgRenderer().RenderPage(doc, 0);
        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Should().Contain("B", "extraction keeps ToUnicode");

        Match data = Regex.Match(svg, "font/truetype;base64,([A-Za-z0-9+/=]+)");
        data.Success.Should().BeTrue();
        FontRenderer embedded = new FontRenderer(Convert.FromBase64String(data.Groups[1].Value));
        FontRenderer original = new FontRenderer(program);
        embedded.GetGlyphIndexUnicode('B').Should().Be(original.GetGlyphIndexUnicode('A'),
            "the SVG text is 'B', and the browser must draw the glyph the raster renderer draws for the code: 'A'");
    }

    [Fact]
    public void Raster_SubstituteFont_DrawsTheEncodingsGlyph()
    {
        using PdfDocument withToUnicode = TestPdf.Open(Document(null));
        using PdfDocument plain = TestPdf.Open(Document(null, toUnicode: false));

        PixelBuffer a = Render(withToUnicode);
        PixelBuffer b = Render(plain);

        Ink(a).Should().BeGreaterThan(20);
        Same(a, b).Should().BeTrue("the substitute draws 'A' (the encoding), not 'B' (ToUnicode)");
    }

    private static PixelBuffer Render(PdfDocument doc) =>
        new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);

    private static int Ink(PixelBuffer px)
    {
        int n = 0;
        for (int y = 0; y < px.Height; y++)
        {
            for (int x = 0; x < px.Width; x++)
            {
                if (px.GetPixelBgra(x, y).R < 128)
                {
                    n++;
                }
            }
        }

        return n;
    }

    private static bool Same(PixelBuffer a, PixelBuffer b)
    {
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                if (a.GetPixelBgra(x, y) != b.GetPixelBgra(x, y))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // A page showing code 0x41 at 48 pt, in an embedded TrueType font
    // (program given) or in non-embedded Helvetica (program null).
    private static byte[] Document(byte[]? program, bool toUnicode = true)
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int cmap = pdf.AddStream("<<>>", ToUnicodeAtoB);
        string font;
        if (program is null)
        {
            font = "<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding" + (toUnicode ? "/ToUnicode " + N(cmap) + " 0 R" : string.Empty) + ">>";
        }
        else
        {
            int file = pdf.AddStream("<</Length1 " + N(program.Length) + ">>", program);
            int descriptor = pdf.Add("<</Type/FontDescriptor/FontName/ChuvadiTest/Flags 32/FontBBox[0 -200 1000 800]/ItalicAngle 0/Ascent 800/Descent -200/CapHeight 700/StemV 80/FontFile2 " + N(file) + " 0 R>>");
            font = "<</Type/Font/Subtype/TrueType/BaseFont/ChuvadiTest/FirstChar 65/LastChar 65/Widths[600]/Encoding/WinAnsiEncoding/FontDescriptor " + N(descriptor) + " 0 R"
                + (toUnicode ? "/ToUnicode " + N(cmap) + " 0 R" : string.Empty) + ">>";
        }

        int fontObj = pdf.Add(font);
        int contents = pdf.AddStream("<<>>", "BT /F1 48 Tf 50 80 Td (A) Tj ET");
        int page = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources<</Font<</F1 " + N(fontObj) + " 0 R>>>>/Contents " + N(contents) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        return pdf.Build(catalog);
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
