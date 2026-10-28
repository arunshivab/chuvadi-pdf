// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.9.4 — Replacement text (/ActualText)
// PHASE: 3.25 — text extraction uses a marked-content sequence's /ActualText
//        in place of the text its glyphs decode to.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

public sealed class ActualTextExtractionTests
{
    [Fact]
    public void InlineActualText_ReplacesTheGlyphText()
    {
        string text = Extract(
            "/Span <</ActualText (office)>> BDC BT /F1 12 Tf 50 300 Td (o\\002ce) Tj ET EMC "
            + "BT /F1 12 Tf 50 250 Td (PLAIN) Tj ET");

        text.Should().Contain("office").And.Contain("PLAIN");
        text.Should().NotContain("o\u0002ce");
    }

    [Fact]
    public void Utf16ActualText_IsDecoded()
    {
        // FEFF + "சொ" (U+0B9A U+0BCA) as hex, over two glyphs in two text objects.
        string text = Extract(
            "/Span <</ActualText <FEFF0B9A0BCA>>> BDC "
            + "BT /F1 12 Tf 50 300 Td (XY) Tj ET BT /F1 12 Tf 70 300 Td (Z) Tj ET EMC");

        text.Trim().Should().Be("சொ");
    }

    [Fact]
    public void NamedPropertyList_ActualText()
    {
        string text = Extract("/Span /P0 BDC BT /F1 12 Tf 50 300 Td (GLYPHS) Tj ET EMC", "/Properties<</P0<</ActualText(REAL)>>>>");

        text.Trim().Should().Be("REAL");
    }

    [Fact]
    public void EmptyActualText_RemovesTheText()
    {
        string text = Extract(
            "/Span <</ActualText ()>> BDC BT /F1 12 Tf 50 300 Td (SOFTHYPHEN) Tj ET EMC "
            + "BT /F1 12 Tf 50 250 Td (KEPT) Tj ET");

        text.Should().NotContain("SOFTHYPHEN").And.Contain("KEPT");
    }

    [Fact]
    public void NestedSpans_TheOuterActualTextWins_AndOtherMarkedContentIsUnchanged()
    {
        string text = Extract(
            "/Span <</ActualText (OUTER)>> BDC /Span <</ActualText (INNER)>> BDC "
            + "BT /F1 12 Tf 50 300 Td (A) Tj ET EMC BT /F1 12 Tf 60 300 Td (B) Tj ET EMC "
            + "/P <</MCID 0>> BDC BT /F1 12 Tf 50 250 Td (TAGGED) Tj ET EMC");

        text.Should().Contain("OUTER").And.Contain("TAGGED");
        text.Should().NotContain("INNER");
    }

    [Fact]
    public void InlineImageData_IsSkipped()
    {
        // Image bytes that look like "<<" or operators must not swallow or
        // run as content.
        string text = Extract(
            "q 10 0 0 10 50 50 cm BI /W 2 /H 2 /BPC 8 /CS /G ID \u0001<<(x) Tj\u0003 EI Q "
            + "BT /F1 12 Tf 50 300 Td (Hello) Tj ET");

        text.Trim().Should().Be("Hello");
    }

    [Fact]
    public void UnterminatedInlineDictionary_StopsAtTheNextOperator()
    {
        // The ">>" is missing: the dictionary ends at BDC, which still opens
        // its span, and the content after it is read as usual.
        string text = Extract(
            "/Span << /ActualText (X) BDC BT /F1 12 Tf 50 300 Td (Hello) Tj ET EMC "
            + "BT /F1 12 Tf 50 250 Td (After) Tj ET");

        text.Should().Contain("X").And.Contain("After");
    }

    [Fact]
    public void Fragments_PlaceTheActualTextAtTheFirstGlyph()
    {
        using PdfDocument doc = Open(
            "/Span <</ActualText (WHOLE)>> BDC BT /F1 12 Tf 80 300 Td (b) Tj ET BT /F1 12 Tf 90 300 Td (c) Tj ET EMC",
            string.Empty);

        List<TextFragment> fragments = new TextExtractor(doc.Objects).ExtractFragments(doc.Pages[0]);

        fragments.Should().ContainSingle();
        fragments[0].Text.Should().Be("WHOLE");
        fragments[0].X.Should().Be(80);
    }

    private static string Extract(string content, string extraResources = "")
    {
        using PdfDocument doc = Open(content, extraResources);
        return new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]);
    }

    private static PdfDocument Open(string content, string extraResources)
    {
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>" + extraResources + ">>/Contents 4 0 R>>",
            "<</Length " + Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture) + ">>\nstream\n" + content + "\nendstream",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
        };

        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture)).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer <</Size ").Append((bodies.Count + 1).ToString(CultureInfo.InvariantCulture))
            .Append("/Root 1 0 R>>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return PdfDocument.Open(new MemoryStream(Encoding.Latin1.GetBytes(sb.ToString())), leaveOpen: false);
    }
}
