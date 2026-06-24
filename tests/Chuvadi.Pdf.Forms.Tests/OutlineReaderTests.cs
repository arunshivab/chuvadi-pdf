// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 (outline), §12.3.2 (destinations),
//        §7.9.6 (name trees)
// Verifies OutlineReader decodes UTF-16BE titles and resolves a named
// destination (/GoTo /D via /Names /Dests) to a zero-based page index.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Forms.Tests;

public sealed partial class OutlineReaderTests
{
    [Fact]
    public void Utf16BeTitle_IsDecoded()
    {
        using MemoryStream pdf = BuildOutlinePdf();
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);

        IReadOnlyList<OutlineItem> outlines = OutlineReader.GetOutlines(doc);

        outlines.Should().ContainSingle();
        outlines[0].Title.Should().Be("Intro");
    }

    [Fact]
    public void NamedDestination_ResolvesToPageIndex()
    {
        using MemoryStream pdf = BuildOutlinePdf();
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);

        IReadOnlyList<OutlineItem> outlines = OutlineReader.GetOutlines(doc);

        // /A /GoTo /D (dest1) → /Names /Dests "dest1" → [page1 …] → index 1.
        outlines[0].DestinationPageIndex.Should().Be(1);
    }

    // Two-page PDF with one outline item: a FE-FF UTF-16BE title "Intro" and a
    // GoTo action to the named destination "dest1", which the /Names /Dests name
    // tree maps to page index 1.
    private static MemoryStream BuildOutlinePdf()
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId page0Id = new PdfObjectId(3, 0);
        PdfObjectId page1Id = new PdfObjectId(4, 0);
        PdfObjectId outlinesId = new PdfObjectId(5, 0);
        PdfObjectId itemId = new PdfObjectId(6, 0);
        PdfObjectId namesId = new PdfObjectId(7, 0);
        PdfObjectId destsTreeId = new PdfObjectId(8, 0);

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Intern("Pages"));
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[]
        {
            new PdfReference(page0Id), new PdfReference(page1Id),
        }));
        pages.Set(PdfName.Count, 2);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(600), new PdfInteger(800),
        }));

        PdfDictionary page0 = new PdfDictionary();
        page0.Set(PdfName.Type, PdfName.Intern("Page"));
        page0.Set(PdfName.Parent, new PdfReference(pagesId));

        PdfDictionary page1 = new PdfDictionary();
        page1.Set(PdfName.Type, PdfName.Intern("Page"));
        page1.Set(PdfName.Parent, new PdfReference(pagesId));

        // Named destination "dest1" → [page1 /XYZ 0 800 0].
        PdfArray destArray = new PdfArray(new PdfPrimitive[]
        {
            new PdfReference(page1Id), PdfName.Intern("XYZ"),
            new PdfInteger(0), new PdfInteger(800), new PdfInteger(0),
        });
        PdfDictionary destsTree = new PdfDictionary();
        destsTree.Set(PdfName.Intern("Names"), new PdfArray(new PdfPrimitive[]
        {
            new PdfString(Encoding.ASCII.GetBytes("dest1")), destArray,
        }));

        PdfDictionary names = new PdfDictionary();
        names.Set(PdfName.Intern("Dests"), new PdfReference(destsTreeId));

        // Outline item: UTF-16BE title + GoTo action to "dest1".
        byte[] title = { 0xFE, 0xFF, 0x00, (byte)'I', 0x00, (byte)'n', 0x00, (byte)'t', 0x00, (byte)'r', 0x00, (byte)'o' };
        PdfDictionary action = new PdfDictionary();
        action.Set(PdfName.Intern("S"), PdfName.Intern("GoTo"));
        action.Set(PdfName.Intern("D"), new PdfString(Encoding.ASCII.GetBytes("dest1")));

        PdfDictionary item = new PdfDictionary();
        item.Set(PdfName.Intern("Title"), new PdfString(title));
        item.Set(PdfName.Intern("Parent"), new PdfReference(outlinesId));
        item.Set(PdfName.Intern("A"), action);

        PdfDictionary outlines = new PdfDictionary();
        outlines.Set(PdfName.Type, PdfName.Intern("Outlines"));
        outlines.Set(PdfName.Intern("First"), new PdfReference(itemId));
        outlines.Set(PdfName.Intern("Last"), new PdfReference(itemId));
        outlines.Set(PdfName.Intern("Count"), 1);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        catalog.Set(PdfName.Outlines, new PdfReference(outlinesId));
        catalog.Set(PdfName.Intern("Names"), new PdfReference(namesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(page0Id, page0),
            new PdfIndirectObject(page1Id, page1),
            new PdfIndirectObject(outlinesId, outlines),
            new PdfIndirectObject(itemId, item),
            new PdfIndirectObject(namesId, names),
            new PdfIndirectObject(destsTreeId, destsTree),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }
}
