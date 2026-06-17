// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.4 (xref), §7.7.3 (page tree)
// PHASE: Input robustness — page-tree recovery from a corrupt cross-reference.
//
// Reproduces the MRDDFF.pdf failure class: a classic xref entry for a page
// object whose byte offset points at a *different* definition of the same object
// number (a content stream), so /Kids[0] resolves to a stream instead of a
// /Page. Asserts the document opens, surfaces a warning, resolves the real page,
// and writes a clean repaired file; and that a healthy file is untouched.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class PageTreeRecoveryTests
{
    [Fact]
    public void Open_HealthyFile_IsNotRecoveredAndHasNoWarnings()
    {
        byte[] pdf = BuildHealthyPdf();

        using MemoryStream ms = new MemoryStream(pdf, writable: false);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        doc.IsRecovered.Should().BeFalse();
        doc.Warnings.Should().BeEmpty();
        doc.PageCount.Should().Be(2);
        doc.Pages[0].Should().NotBeNull();
    }

    [Fact]
    public void Open_CorruptPageXref_RecoversPageAndRaisesWarning()
    {
        byte[] pdf = BuildCorruptPdf();

        using MemoryStream ms = new MemoryStream(pdf, writable: false);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        doc.IsRecovered.Should().BeTrue();
        doc.Warnings.Should().ContainSingle();
        doc.Warnings[0].Should().Contain("Page-tree object");
    }

    [Fact]
    public void Open_CorruptPageXref_PageZeroResolvesToRealPage()
    {
        byte[] pdf = BuildCorruptPdf();

        using MemoryStream ms = new MemoryStream(pdf, writable: false);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        // Before recovery this threw "Page tree /Kids[0] is not a dictionary".
        PdfPage page0 = doc.Pages[0];
        page0.MediaBox.Width.Should().BeApproximately(612, 0.5);
        page0.MediaBox.Height.Should().BeApproximately(792, 0.5);
    }

    [Fact]
    public void Open_CorruptPageXref_AllPagesResolve()
    {
        byte[] pdf = BuildCorruptPdf();

        using MemoryStream ms = new MemoryStream(pdf, writable: false);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        doc.PageCount.Should().Be(2);
        doc.Pages[0].Should().NotBeNull();
        doc.Pages[1].Should().NotBeNull();
    }

    // ── Fixtures ──────────────────────────────────────────────────────────

    // A valid two-page PDF written by the library's own writer.
    private static byte[] BuildHealthyPdf()
    {
        List<PdfIndirectObject> objects = BuildObjects(out PdfDictionary trailer);
        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }

    // The same document, corrupted to mirror MRDDFF.pdf without changing any byte
    // offsets: the classic xref entry for object 3 (page 1) is repointed at the
    // existing offset of object 5 (a content stream). /Kids[0] = 3 0 R therefore
    // resolves to a stream, while the real "3 0 obj" /Page definition remains in
    // the file for recovery to find by scanning. No insertion, so every other
    // object's offset stays valid.
    private static byte[] BuildCorruptPdf()
    {
        byte[] bytes = BuildHealthyPdf();

        int offset3 = XrefOffsetOf(bytes, 3);
        int offset5 = XrefOffsetOf(bytes, 5);
        offset3.Should().BeGreaterThan(0);
        offset5.Should().BeGreaterThan(0);
        offset3.Should().NotBe(offset5);

        WriteXrefOffset(bytes, objectNumber: 3, offset: offset5);
        return bytes;
    }

    // Returns the byte position of the start of the classic xref subsection's
    // entry table (just past the "0 N" subsection header).
    private static int XrefEntryTableStart(byte[] bytes)
    {
        string text = Encoding.Latin1.GetString(bytes);
        int xrefPos = text.LastIndexOf("\nxref", StringComparison.Ordinal);
        xrefPos.Should().BeGreaterThan(0);
        int afterXref = text.IndexOf('\n', xrefPos + 1) + 1; // past "xref"
        int afterHeader = text.IndexOf('\n', afterXref) + 1; // past "0 N"
        return afterHeader;
    }

    private static int XrefOffsetOf(byte[] bytes, int objectNumber)
    {
        int entryStart = XrefEntryTableStart(bytes) + (objectNumber * 20);
        string field = Encoding.Latin1.GetString(bytes, entryStart, 10);
        return int.Parse(field, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void WriteXrefOffset(byte[] bytes, int objectNumber, int offset)
    {
        int entryStart = XrefEntryTableStart(bytes) + (objectNumber * 20);
        string field = offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < 10; i++)
        {
            bytes[entryStart + i] = (byte)field[i];
        }
    }

    private static List<PdfIndirectObject> BuildObjects(out PdfDictionary trailer)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId page1Id = new PdfObjectId(3, 0);
        PdfObjectId page2Id = new PdfObjectId(4, 0);
        PdfObjectId content1Id = new PdfObjectId(5, 0);
        PdfObjectId content2Id = new PdfObjectId(6, 0);

        byte[] content1 = Encoding.ASCII.GetBytes("BT (ONE) Tj ET");
        byte[] content2 = Encoding.ASCII.GetBytes("BT (TWO) Tj ET");

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        PdfDictionary cd1 = new PdfDictionary();
        cd1.Set(PdfName.Length, content1.Length);
        objects.Add(new PdfIndirectObject(content1Id, new PdfStream(cd1, content1)));

        PdfDictionary cd2 = new PdfDictionary();
        cd2.Set(PdfName.Length, content2.Length);
        objects.Add(new PdfIndirectObject(content2Id, new PdfStream(cd2, content2)));

        objects.Add(MakePage(page1Id, pagesId, content1Id));
        objects.Add(MakePage(page2Id, pagesId, content2Id));

        PdfArray kids = new PdfArray(new PdfPrimitive[]
        {
            new PdfReference(page1Id), new PdfReference(page2Id),
        });
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 2);
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalogDict));

        trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        return objects;
    }

    private static PdfIndirectObject MakePage(
        PdfObjectId pageId, PdfObjectId pagesId, PdfObjectId contentId)
    {
        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));
        page.Set(PdfName.Intern("Contents"), new PdfReference(contentId));
        return new PdfIndirectObject(pageId, page);
    }
}
