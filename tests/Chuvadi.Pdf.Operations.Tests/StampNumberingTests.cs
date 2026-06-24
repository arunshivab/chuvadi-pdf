// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 (text)
// Tests for LA-07: Bates / styled numbering on the stamp path —
// StampNumbering, the {number} token, and the TextStamper numbering overload.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class StampNumberingTests
{
    // ── StampNumbering.Format: styles ────────────────────────────────────────

    [Fact]
    public void Format_Arabic_NoPadding_IsPlainDigits()
    {
        StampNumbering n = new StampNumbering();
        n.Format(7).Should().Be("7");
    }

    [Fact]
    public void Format_RomanLower_And_Upper()
    {
        StampNumbering lower = new StampNumbering { Numbering = NumberingFormat.RomanLower, };
        StampNumbering upper = new StampNumbering { Numbering = NumberingFormat.RomanUpper, };
        lower.Format(4).Should().Be("iv");
        upper.Format(4).Should().Be("IV");
        upper.Format(1487).Should().Be("MCDLXXXVII");
    }

    [Fact]
    public void Format_LetterLower_And_Upper_AreBijectiveBase26()
    {
        StampNumbering lower = new StampNumbering { Numbering = NumberingFormat.LetterLower, };
        StampNumbering upper = new StampNumbering { Numbering = NumberingFormat.LetterUpper, };
        upper.Format(1).Should().Be("A");
        upper.Format(26).Should().Be("Z");
        upper.Format(27).Should().Be("AA");
        lower.Format(28).Should().Be("ab");
    }

    // ── StampNumbering.Format: padding ───────────────────────────────────────

    [Fact]
    public void Format_Arabic_ZeroPads_ToWidth()
    {
        StampNumbering n = new StampNumbering { PadWidth = 6, };
        n.Format(123).Should().Be("000123");
    }

    [Fact]
    public void Format_Arabic_WiderValue_IsNotTruncated()
    {
        StampNumbering n = new StampNumbering { PadWidth = 3, };
        n.Format(12345).Should().Be("12345");
    }

    [Fact]
    public void Format_PadWidth_IsIgnored_ForRomanAndLetter()
    {
        StampNumbering roman = new StampNumbering { Numbering = NumberingFormat.RomanUpper, PadWidth = 6, };
        StampNumbering letter = new StampNumbering { Numbering = NumberingFormat.LetterUpper, PadWidth = 6, };
        roman.Format(4).Should().Be("IV");
        letter.Format(1).Should().Be("A");
    }

    // ── StampNumbering.Format: prefix / suffix combinations ──────────────────

    [Fact]
    public void Format_PrefixOnly()
    {
        StampNumbering n = new StampNumbering { Prefix = "A-", };
        n.Format(5).Should().Be("A-5");
    }

    [Fact]
    public void Format_SuffixOnly()
    {
        StampNumbering n = new StampNumbering { Suffix = "-Z", };
        n.Format(5).Should().Be("5-Z");
    }

    [Fact]
    public void Format_PadOnly()
    {
        StampNumbering n = new StampNumbering { PadWidth = 4, };
        n.Format(5).Should().Be("0005");
    }

    [Fact]
    public void Format_Prefix_Suffix_And_Pad_AllCombine()
    {
        StampNumbering n = new StampNumbering { Prefix = "A-", Suffix = "-Z", PadWidth = 6, };
        n.Format(123).Should().Be("A-000123-Z");
    }

    [Fact]
    public void Format_BatesStyle()
    {
        StampNumbering n = new StampNumbering { Prefix = "BATES-", PadWidth = 6, };
        n.Format(1).Should().Be("BATES-000001");
    }

    [Fact]
    public void Format_None_IsPlainNumber()
    {
        StampNumbering n = new StampNumbering();
        n.Format(42).Should().Be("42");
    }

    // ── StampNumbering.ResolveValue: start offset ────────────────────────────

    [Fact]
    public void ResolveValue_HonoursStartValue()
    {
        StampNumbering n = new StampNumbering { StartValue = 100, };
        n.ResolveValue(0).Should().Be(100);
        n.ResolveValue(1).Should().Be(101);
        n.ResolveValue(4).Should().Be(104);
    }

    // ── StampNumbering.ResolveValue: first-page handling ─────────────────────

    [Fact]
    public void ResolveValue_Number_StampsAndCountsFirstPage()
    {
        StampNumbering n = new StampNumbering { FirstPage = StampFirstPageMode.Number, };
        n.ResolveValue(0).Should().Be(1);
        n.ResolveValue(1).Should().Be(2);
        n.ResolveValue(2).Should().Be(3);
    }

    [Fact]
    public void ResolveValue_SkipKeepCount_SkipsFirstButReservesItsSlot()
    {
        StampNumbering n = new StampNumbering { FirstPage = StampFirstPageMode.SkipKeepCount, };
        n.ResolveValue(0).Should().BeNull();
        n.ResolveValue(1).Should().Be(2);
        n.ResolveValue(2).Should().Be(3);
    }

    [Fact]
    public void ResolveValue_SkipRenumber_SkipsFirstAndDoesNotCountIt()
    {
        StampNumbering n = new StampNumbering { FirstPage = StampFirstPageMode.SkipRenumber, };
        n.ResolveValue(0).Should().BeNull();
        n.ResolveValue(1).Should().Be(1);
        n.ResolveValue(2).Should().Be(2);
    }

    // ── {number} token resolution ────────────────────────────────────────────

    [Fact]
    public void NumberToken_ResolvesFromContext()
    {
        StampContext ctx = new StampContext(1, 5, null, null, "BATES-000001");
        StampTokens.Resolve("{number}", ctx).Should().Be("BATES-000001");
        StampTokens.Resolve("Exhibit {number}", ctx).Should().Be("Exhibit BATES-000001");
    }

    [Fact]
    public void NumberToken_IsEmpty_WhenNoNumberSupplied()
    {
        StampContext ctx = new StampContext(1, 5, null, null);
        StampTokens.Resolve("X{number}Y", ctx).Should().Be("XY");
    }

    // ── TextStamper numbering overload (end-to-end) ──────────────────────────

    [Fact]
    public void Apply_Numbering_PreservesPageCount_AndStampsFirstPage()
    {
        using MemoryStream src = BuildTextPdf("A", "B", "C");
        using PdfDocument doc = OpenPdf(src);

        StampNumbering numbering = new StampNumbering { Prefix = "BATES-", PadWidth = 6, };
        using MemoryStream output = new MemoryStream();
        TextStamper.Apply(
            output, doc, null, "{number}", StampAnchor.BottomRight,
            36, 24, 9, ColorF.Black, numbering);

        using PdfDocument result = OpenPdf(output);
        result.PageCount.Should().Be(3);

        // Number mode: the first page is stamped, so it gains a font resource.
        PdfDictionary p0res = result.Pages[0].Resources!;
        p0res.ContainsKey(PdfName.Font).Should().BeTrue();
    }

    [Fact]
    public void Apply_Numbering_SkipRenumber_LeavesFirstPageUnstamped()
    {
        using MemoryStream src = BuildTextPdf("A", "B", "C");
        using PdfDocument doc = OpenPdf(src);

        StampNumbering numbering = new StampNumbering { FirstPage = StampFirstPageMode.SkipRenumber, };
        using MemoryStream output = new MemoryStream();
        TextStamper.Apply(
            output, doc, null, "{number}", StampAnchor.BottomRight,
            36, 24, 9, ColorF.Black, numbering);

        using PdfDocument result = OpenPdf(output);
        result.PageCount.Should().Be(3);

        // First page is skipped: no font injected. Later pages are stamped.
        PdfDictionary? p0res = result.Pages[0].Resources;
        bool p0HasFont = p0res is not null && p0res.ContainsKey(PdfName.Font);
        p0HasFont.Should().BeFalse();

        PdfDictionary p1res = result.Pages[1].Resources!;
        p1res.ContainsKey(PdfName.Font).Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PdfDocument OpenPdf(MemoryStream ms)
    {
        ms.Position = 0;
        return PdfDocument.Open(ms, leaveOpen: true);
    }

    private static MemoryStream BuildTextPdf(params string[] pageTexts)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        PdfArray kids = new PdfArray([]);
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, pageTexts.Length);
        pagesDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(595), new PdfInteger(842)
        ]));

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
        };

        int next = 3;
        foreach (string pageText in pageTexts)
        {
            PdfObjectId pageId = new PdfObjectId(next++, 0);
            PdfObjectId contentId = new PdfObjectId(next++, 0);

            byte[] content = Encoding.ASCII.GetBytes($"BT ({pageText}) Tj ET");
            PdfDictionary contentDict = new PdfDictionary();
            contentDict.Set(PdfName.Length, content.Length);

            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            pageDict.Set(PdfName.Contents, new PdfReference(contentId));

            objects.Add(new PdfIndirectObject(pageId, pageDict));
            objects.Add(new PdfIndirectObject(contentId, new PdfStream(contentDict, content)));
            kids.Add(new PdfReference(pageId));
        }

        MemoryStream ms = new MemoryStream();
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        Chuvadi.Pdf.IO.PdfWriter.Write(ms, objects, trailer);
        ms.Position = 0;
        return ms;
    }
}
