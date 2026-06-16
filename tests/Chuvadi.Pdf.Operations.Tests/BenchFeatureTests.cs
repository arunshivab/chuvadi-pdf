// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 (outlines), §14.3.3 (info), §8.10.1 (forms),
//        §11.6.4.4 (opacity), §9.4 (text)
// Tests for the Bench feature set: DocumentInfo, OutlineWriter, PageOverlay,
// TextStamper, HeaderFooter, and the StampTokens engine.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class BenchFeatureTests
{
    // ── DocumentInfo ─────────────────────────────────────────────────────────

    [Fact]
    public void DocumentInfo_Apply_SetsAllFields()
    {
        using MemoryStream src = BuildTextPdf("A", "B");
        using PdfDocument doc = OpenPdf(src);

        using MemoryStream output = new MemoryStream();
        DocumentInfo.Apply(output, doc, "My Title", "My Author", "My Subject", "k1,k2");

        using PdfDocument result = OpenPdf(output);
        result.Title.Should().Be("My Title");
        result.Author.Should().Be("My Author");
        result.Subject.Should().Be("My Subject");
        result.Keywords.Should().Be("k1,k2");
    }

    [Fact]
    public void DocumentInfo_Apply_NullLeavesExistingUnchanged()
    {
        using MemoryStream src = BuildTextPdf("A");
        using PdfDocument doc = OpenPdf(src);

        using MemoryStream first = new MemoryStream();
        DocumentInfo.Apply(first, doc, title: "Original", author: "Orig Author");

        using PdfDocument firstDoc = OpenPdf(first);
        using MemoryStream second = new MemoryStream();
        DocumentInfo.Apply(second, firstDoc, subject: "Added Subject");

        using PdfDocument result = OpenPdf(second);
        result.Title.Should().Be("Original");
        result.Author.Should().Be("Orig Author");
        result.Subject.Should().Be("Added Subject");
    }

    [Fact]
    public void DocumentInfo_Apply_PreservesPages()
    {
        using MemoryStream src = BuildTextPdf("A", "B", "C");
        using PdfDocument doc = OpenPdf(src);

        using MemoryStream output = new MemoryStream();
        DocumentInfo.Apply(output, doc, title: "T");

        using PdfDocument result = OpenPdf(output);
        result.PageCount.Should().Be(3);
    }

    // ── OutlineWriter ────────────────────────────────────────────────────────

    [Fact]
    public void OutlineWriter_Apply_WritesOutlinesIntoCatalog()
    {
        using MemoryStream src = BuildTextPdf("A", "B", "C");
        using PdfDocument doc = OpenPdf(src);

        List<OutlineEntry> entries = new List<OutlineEntry>
        {
            new OutlineEntry("First", 0),
            new OutlineEntry("Third", 2),
        };

        using MemoryStream output = new MemoryStream();
        OutlineWriter.Apply(output, doc, entries);

        using PdfDocument result = OpenPdf(output);
        result.Catalog.ContainsKey(PdfName.Outlines).Should().BeTrue();
    }

    [Fact]
    public void OutlineWriter_Apply_NestedChildrenAreLinked()
    {
        using MemoryStream src = BuildTextPdf("A", "B", "C", "D");
        using PdfDocument doc = OpenPdf(src);

        List<OutlineEntry> entries = new List<OutlineEntry>
        {
            new OutlineEntry("Parent", 0, new List<OutlineEntry>
            {
                new OutlineEntry("Child", 2),
            }),
        };

        using MemoryStream output = new MemoryStream();
        OutlineWriter.Apply(output, doc, entries);

        using PdfDocument result = OpenPdf(output);
        PdfDictionary outlines = result.Objects.ResolveAs<PdfDictionary>(
            result.Catalog.GetAs<PdfPrimitive>(PdfName.Outlines) ?? PdfNull.Value)!;

        PdfDictionary firstItem = result.Objects.ResolveAs<PdfDictionary>(
            outlines.GetAs<PdfPrimitive>(PdfName.Intern("First")) ?? PdfNull.Value)!;

        firstItem.ContainsKey(PdfName.Intern("First")).Should().BeTrue();
        firstItem.ContainsKey(PdfName.Intern("Title")).Should().BeTrue();
    }

    [Fact]
    public void OutlineWriter_Apply_EmptyListRemovesOutline()
    {
        using MemoryStream src = BuildTextPdf("A");
        using PdfDocument doc = OpenPdf(src);

        using MemoryStream output = new MemoryStream();
        OutlineWriter.Apply(output, doc, Array.Empty<OutlineEntry>());

        using PdfDocument result = OpenPdf(output);
        result.Catalog.ContainsKey(PdfName.Outlines).Should().BeFalse();
    }

    [Fact]
    public void OutlineWriter_Apply_DestinationTargetsCorrectPage()
    {
        using MemoryStream src = BuildTextPdf("A", "B", "C");
        using PdfDocument doc = OpenPdf(src);

        List<OutlineEntry> entries = new List<OutlineEntry>
        {
            new OutlineEntry("Go to page 3", 2),
        };

        using MemoryStream output = new MemoryStream();
        OutlineWriter.Apply(output, doc, entries);

        using PdfDocument result = OpenPdf(output);
        PdfDictionary outlines = result.Objects.ResolveAs<PdfDictionary>(
            result.Catalog.GetAs<PdfPrimitive>(PdfName.Outlines) ?? PdfNull.Value)!;
        PdfDictionary item = result.Objects.ResolveAs<PdfDictionary>(
            outlines.GetAs<PdfPrimitive>(PdfName.Intern("First")) ?? PdfNull.Value)!;

        // The /Dest array's first element must resolve to a /Page dictionary.
        PdfArray dest = (PdfArray)item.GetAs<PdfPrimitive>(PdfName.Intern("Dest"))!;
        dest.Count.Should().BeGreaterThanOrEqualTo(2);

        PdfDictionary? destPage = result.Objects.ResolveAs<PdfDictionary>(dest[0]);
        destPage.Should().NotBeNull();
        ((PdfName)destPage!.GetAs<PdfPrimitive>(PdfName.Type)!).Value.Should().Be("Page");
    }

    // ── PageOverlay ──────────────────────────────────────────────────────────

    [Fact]
    public void PageOverlay_Apply_AddsExtGStateForOpacity()
    {
        using MemoryStream src = BuildTextPdf("A", "B");
        using PdfDocument doc = OpenPdf(src);

        using MemoryStream output = new MemoryStream();
        PageOverlay.Apply(output, doc, new[] { 0 }, background: null, contentOpacity: 0.4f);

        using PdfDocument result = OpenPdf(output);
        PdfDictionary resources = result.Pages[0].Resources!;
        resources.ContainsKey(PdfName.Intern("ExtGState")).Should().BeTrue();
    }

    [Fact]
    public void PageOverlay_Apply_PreservesPageCount()
    {
        using MemoryStream src = BuildTextPdf("A", "B", "C");
        using PdfDocument doc = OpenPdf(src);

        using MemoryStream output = new MemoryStream();
        PageOverlay.Apply(output, doc, null, ColorF.FromRgb(1f, 0f, 0f), 0.5f);

        using PdfDocument result = OpenPdf(output);
        result.PageCount.Should().Be(3);
    }

    [Fact]
    public void PageOverlay_Apply_RejectsOpacityOutOfRange()
    {
        using MemoryStream src = BuildTextPdf("A");
        using PdfDocument doc = OpenPdf(src);
        using MemoryStream output = new MemoryStream();

        Action act = () => PageOverlay.Apply(output, doc, null, null, 1.5f);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PageOverlay_Apply_WrapsContentAsForm()
    {
        using MemoryStream src = BuildTextPdf("A");
        using PdfDocument doc = OpenPdf(src);

        using MemoryStream output = new MemoryStream();
        PageOverlay.Apply(output, doc, new[] { 0 }, null, 0.5f);

        using PdfDocument result = OpenPdf(output);
        PdfDictionary resources = result.Pages[0].Resources!;
        PdfDictionary xobjects = result.Objects.ResolveAs<PdfDictionary>(
            resources.GetAs<PdfPrimitive>(PdfName.XObject) ?? PdfNull.Value)!;
        xobjects.ContainsKey(PdfName.Intern("CvContent")).Should().BeTrue();
    }

    // ── TextStamper ──────────────────────────────────────────────────────────

    [Fact]
    public void TextStamper_Apply_PreservesPageCountAndAddsFont()
    {
        using MemoryStream src = BuildTextPdf("A", "B");
        using PdfDocument doc = OpenPdf(src);

        using MemoryStream output = new MemoryStream();
        TextStamper.Apply(
            output, doc, null, "Page {page}", StampAnchor.BottomCenter,
            36, 24, 9, ColorF.Black);

        using PdfDocument result = OpenPdf(output);
        result.PageCount.Should().Be(2);
        PdfDictionary resources = result.Pages[0].Resources!;
        resources.ContainsKey(PdfName.Font).Should().BeTrue();
    }

    [Fact]
    public void TextStamper_Apply_OnlyStampsRequestedPages()
    {
        using MemoryStream src = BuildTextPdf("A", "B", "C");
        using PdfDocument doc = OpenPdf(src);

        using MemoryStream output = new MemoryStream();
        TextStamper.Apply(
            output, doc, new[] { 1 }, "X", StampAnchor.TopRight,
            10, 10, 8, ColorF.Black);

        using PdfDocument result = OpenPdf(output);

        // Page 0 was not stamped: no font resource was injected.
        PdfDictionary? p0res = result.Pages[0].Resources;
        bool p0HasFont = p0res is not null && p0res.ContainsKey(PdfName.Font);
        PdfDictionary p1res = result.Pages[1].Resources!;
        p1res.ContainsKey(PdfName.Font).Should().BeTrue();
        p0HasFont.Should().BeFalse();
    }

    // ── HeaderFooter ─────────────────────────────────────────────────────────

    [Fact]
    public void HeaderFooter_Overlay_PreservesPageCount()
    {
        using MemoryStream src = BuildTextPdf("A", "B");
        using PdfDocument doc = OpenPdf(src);

        HeaderFooterOptions options = new HeaderFooterOptions
        {
            Footer = new BandText(center: "Page {page} of {total}"),
            Fit = PageContentFit.Overlay,
        };

        using MemoryStream output = new MemoryStream();
        HeaderFooter.Apply(output, doc, options);

        using PdfDocument result = OpenPdf(output);
        result.PageCount.Should().Be(2);
    }

    [Fact]
    public void HeaderFooter_ReserveAndScale_WrapsContent()
    {
        using MemoryStream src = BuildTextPdf("A", "B");
        using PdfDocument doc = OpenPdf(src);

        HeaderFooterOptions options = new HeaderFooterOptions
        {
            Header = new BandText(center: "TITLE"),
            Footer = new BandText(center: "{page}"),
            Fit = PageContentFit.ReserveAndScale,
        };

        using MemoryStream output = new MemoryStream();
        HeaderFooter.Apply(output, doc, options);

        using PdfDocument result = OpenPdf(output);
        PdfDictionary resources = result.Pages[0].Resources!;
        PdfDictionary xobjects = result.Objects.ResolveAs<PdfDictionary>(
            resources.GetAs<PdfPrimitive>(PdfName.XObject) ?? PdfNull.Value)!;
        xobjects.ContainsKey(PdfName.Intern("CvContent")).Should().BeTrue();
    }

    // ── StampTokens ──────────────────────────────────────────────────────────

    [Fact]
    public void Tokens_PageArabicAndTotal()
    {
        StampContext ctx = new StampContext(3, 10, null, null);
        StampTokens.Resolve("{page}/{total}", ctx).Should().Be("3/10");
    }

    [Fact]
    public void Tokens_RomanLowerAndUpper()
    {
        StampContext ctx = new StampContext(4, 10, null, null);
        StampTokens.Resolve("{page:roman}", ctx).Should().Be("iv");
        StampTokens.Resolve("{page:ROMAN}", ctx).Should().Be("IV");
    }

    [Fact]
    public void Tokens_RomanLargeValue()
    {
        StampContext ctx = new StampContext(1487, 2000, null, null);
        StampTokens.Resolve("{page:ROMAN}", ctx).Should().Be("MCDLXXXVII");
    }

    [Fact]
    public void Tokens_AlphaBijectiveBaseTwentySix()
    {
        StampTokens.Resolve("{page:ALPHA}", new StampContext(1, 1, null, null)).Should().Be("A");
        StampTokens.Resolve("{page:ALPHA}", new StampContext(26, 1, null, null)).Should().Be("Z");
        StampTokens.Resolve("{page:ALPHA}", new StampContext(27, 1, null, null)).Should().Be("AA");
        StampTokens.Resolve("{page:ALPHA}", new StampContext(52, 1, null, null)).Should().Be("AZ");
        StampTokens.Resolve("{page:alpha}", new StampContext(28, 1, null, null)).Should().Be("ab");
    }

    [Fact]
    public void Tokens_FilenameVsFilepath()
    {
        StampContext ctx = new StampContext(1, 1, "/home/user/docs/report.pdf", null);
        StampTokens.Resolve("{filename}", ctx).Should().Be("report.pdf");
        StampTokens.Resolve("{filepath}", ctx).Should().Be("/home/user/docs/report.pdf");
    }

    [Fact]
    public void Tokens_DateTimeUsesSuppliedTimestampAndFormat()
    {
        DateTimeOffset ts = new DateTimeOffset(2026, 6, 17, 14, 30, 0, TimeSpan.Zero);
        StampContext ctx = new StampContext(1, 1, null, ts);
        StampTokens.Resolve("{date:yyyy-MM-dd}", ctx).Should().Be("2026-06-17");
        StampTokens.Resolve("{time:HH:mm}", ctx).Should().Be("14:30");
    }

    [Fact]
    public void Tokens_DateEmptyWhenNoTimestamp()
    {
        StampContext ctx = new StampContext(1, 1, null, null);
        StampTokens.Resolve("X{date:yyyy}Y", ctx).Should().Be("XY");
    }

    [Fact]
    public void Tokens_LiteralBracesAndUnknownTokensPreserved()
    {
        StampContext ctx = new StampContext(2, 5, null, null);
        StampTokens.Resolve("{{literal}} {page} {unknown}", ctx)
            .Should().Be("{literal} 2 {unknown}");
    }

    [Fact]
    public void Tokens_CustomTextAroundTokens()
    {
        StampContext ctx = new StampContext(7, 99, null, null);
        StampTokens.Resolve("Confidential - Page {page} of {total}", ctx)
            .Should().Be("Confidential - Page 7 of 99");
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
