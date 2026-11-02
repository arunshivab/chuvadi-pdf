// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.30 — text in a font the PDF does not embed is drawn with a
//        Liberation substitute at the PDF's widths, with a warning (D-105).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class NonEmbeddedFontRasterTests
{
    private const string Text = "BT /F1 24 Tf 20 100 Td (Hello World) Tj ET";

    [Theory]
    [InlineData("TrueType", "Arial", "Liberation Sans")]
    [InlineData("TrueType", "Arial,Bold", "Liberation Sans")]
    [InlineData("TrueType", "TimesNewRomanPSMT", "Liberation Serif")]
    [InlineData("Type1", "CourierNewPSMT", "Liberation Mono")]
    [InlineData("Type1", "NoSuchFontXYZ", "Liberation Sans")]
    public void ASimpleFontThatIsNotEmbedded_IsDrawnWithASubstitute(string subtype, string baseFont, string outlines)
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = Open("/Font<</F1 5 0 R>>", Text, SimpleFont(subtype, baseFont, 556), warnings.Add);

        Ink(Render(doc)).Should().BeGreaterThan(50);
        warnings.Should().ContainSingle(w => w.Kind == PdfWarningKind.FontSubstituted)
            .Which.Reason.Should().Contain(baseFont).And.Contain(outlines);
    }

    [Fact]
    public void APictureFont_IsNotDrawn_AndIsReported()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = Open("/Font<</F1 5 0 R>>", Text, SimpleFont("TrueType", "Wingdings-Regular", 800), warnings.Add);

        Ink(Render(doc)).Should().Be(0);
        warnings.Should().ContainSingle().Which.Reason.Should().Contain("no substitute");
    }

    [Fact]
    public void Characters_ArePlacedByThePdfWidths()
    {
        using PdfDocument narrow = Open("/Font<</F1 5 0 R>>", Text, SimpleFont("TrueType", "Arial", 400), null);
        using PdfDocument wide = Open("/Font<</F1 5 0 R>>", Text, SimpleFont("TrueType", "Arial", 900), null);

        InkRight(Render(wide)).Should().BeGreaterThan(InkRight(Render(narrow)) + 20);
    }

    [Fact]
    public void AType0FontThatIsNotEmbedded_IsDrawnThroughItsToUnicodeMap_AtItsWWidths()
    {
        string cmap =
            "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /T def " +
            "1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfrange <0020> <007E> <0020> endbfrange " +
            "endcmap CMapName currentdict /CMap defineresource pop end end";
        string hex = string.Concat("Hello".Select(c => ((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)));
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = Open(
            "/Font<</F1 5 0 R>>",
            "BT /F1 24 Tf 20 100 Td <" + hex + "> Tj ET",
            "<</Type/Font/Subtype/Type0/BaseFont/Arial/Encoding/Identity-H/DescendantFonts[6 0 R]/ToUnicode 7 0 R>>",
            warnings.Add,
            "<</Type/Font/Subtype/CIDFontType2/BaseFont/Arial/CIDSystemInfo<</Registry(Adobe)/Ordering(Identity)/Supplement 0>>/DW 1000/W[72 [722 556] 108 111 600]>>",
            "<</Length " + cmap.Length + ">>\nstream\n" + cmap + "\nendstream");

        Ink(Render(doc)).Should().BeGreaterThan(50);
        warnings.Should().Contain(w => w.Kind == PdfWarningKind.FontSubstituted);
    }

    [Theory]
    [InlineData("\u0928\u092E\u0938\u094D\u0924\u0947")]
    [InlineData("\u0BA4\u0BAE\u0BBF\u0BB4\u0BCD")]
    public void IndicTextInAType0FontThatIsNotEmbedded_IsDrawnWithTheBundledFace(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // One CID per character, mapped to it by ToUnicode (D-100).
        StringBuilder bf = new StringBuilder();
        StringBuilder hex = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            string cid = (i + 1).ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
            bf.Append('<').Append(cid).Append("> <").Append(((int)text[i]).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)).Append("> ");
            hex.Append(cid);
        }

        string cmap =
            "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /T def " +
            "1 begincodespacerange <0000> <FFFF> endcodespacerange " + text.Length + " beginbfchar " + bf + "endbfchar " +
            "endcmap CMapName currentdict /CMap defineresource pop end end";
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = Open(
            "/Font<</F1 5 0 R>>",
            "BT /F1 28 Tf 20 100 Td <" + hex + "> Tj ET",
            "<</Type/Font/Subtype/Type0/BaseFont/Mangal/Encoding/Identity-H/DescendantFonts[6 0 R]/ToUnicode 7 0 R>>",
            warnings.Add,
            "<</Type/Font/Subtype/CIDFontType2/BaseFont/Mangal/CIDSystemInfo<</Registry(Adobe)/Ordering(Identity)/Supplement 0>>/DW 600>>",
            "<</Length " + cmap.Length + ">>\nstream\n" + cmap + "\nendstream");

        Ink(Render(doc)).Should().BeGreaterThan(80);
        warnings.Should().NotContain(w => w.Kind == PdfWarningKind.GlyphSkipped);
    }

    [Fact]
    public void AnEscapedResourceName_FindsItsFont()
    {
        // "/F#20A" in the content stream is the key "F A" in /Resources.
        using PdfDocument doc = Open(
            "/Font<</F#20A 5 0 R>>",
            "BT /F#20A 24 Tf 20 100 Td (Hello) Tj ET",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
            null);

        Ink(Render(doc)).Should().BeGreaterThan(50);
    }

    private static string SimpleFont(string subtype, string baseFont, int width) =>
        "<</Type/Font/Subtype/" + subtype + "/BaseFont/" + baseFont + "/FirstChar 32/LastChar 126/Widths["
        + string.Join(" ", Enumerable.Repeat(width, 95)) + "]/Encoding/WinAnsiEncoding>>";

    private static PixelBuffer Render(PdfDocument doc) =>
        new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0], CancellationToken.None);

    private static int Ink(PixelBuffer buffer)
    {
        int ink = 0;
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                ink += buffer.GetPixelBgra(x, y).R < 128 ? 1 : 0;
            }
        }

        return ink;
    }

    private static int InkRight(PixelBuffer buffer)
    {
        int right = -1;
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                if (buffer.GetPixelBgra(x, y).R < 128)
                {
                    right = Math.Max(right, x);
                }
            }
        }

        return right;
    }

    private static PdfDocument Open(string resources, string content, string object5, Action<PdfWarning>? onWarning, params string[] more)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 200]/Resources<<" + resources + ">>/Contents 4 0 R>>",
            "<</Length " + content.Length + ">>\nstream\n" + content + "\nendstream",
            object5,
        };
        bodies.AddRange(more);
        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(i + 1).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append(bodies.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<</Size ").Append(bodies.Count + 1).Append("/Root 1 0 R>>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(sb.ToString())), new PdfOpenOptions { OnWarning = onWarning });
    }
}
