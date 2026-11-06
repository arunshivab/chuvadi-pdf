// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31.2 (D-219) — outline page indexes come from the page tree

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

public sealed class OutlinePageTree3312Tests
{
    [Fact]
    public void OutlineDestination_UsesThePageTreeOrder_NotTheObjectOrder()
    {
        // Pages are objects 3 and 4; the tree lists [4 3], so object 3 is page 1.
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageA = new PdfObjectId(3, 0);
        PdfObjectId pageB = new PdfObjectId(4, 0);
        PdfObjectId outlinesId = new PdfObjectId(5, 0);
        PdfObjectId itemId = new PdfObjectId(6, 0);

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageB), new PdfReference(pageA) }));
        pages.Set(PdfName.Count, 2);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(600), new PdfInteger(800),
        }));

        PdfDictionary item = new PdfDictionary();
        item.Set(PdfName.Intern("Title"), new PdfString(Encoding.ASCII.GetBytes("Second page")));
        item.Set(PdfName.Intern("Parent"), new PdfReference(outlinesId));
        item.Set(PdfName.Intern("Dest"), new PdfArray(new PdfPrimitive[] { new PdfReference(pageA), PdfName.Intern("Fit") }));

        PdfDictionary outlines = new PdfDictionary();
        outlines.Set(PdfName.Type, PdfName.Outlines);
        outlines.Set(PdfName.Intern("First"), new PdfReference(itemId));
        outlines.Set(PdfName.Intern("Last"), new PdfReference(itemId));

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        catalog.Set(PdfName.Outlines, new PdfReference(outlinesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageA, Page(pagesId)),
            new PdfIndirectObject(pageB, Page(pagesId)),
            new PdfIndirectObject(outlinesId, outlines),
            new PdfIndirectObject(itemId, item),
        };
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects.ToArray(), trailer);
        ms.Position = 0;

        // Pins the page-tree order. 3.31.1 also passes: it counted the /Page
        // objects in the order the store held them, which PdfDocument.Open
        // happens to fill in tree order.
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: false);
        IReadOnlyList<OutlineItem> items = OutlineReader.GetOutlines(doc);

        items.Should().HaveCount(1);
        items[0].DestinationPageIndex.Should().Be(1);
    }

    private static PdfDictionary Page(PdfObjectId parent)
    {
        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Intern("Parent"), new PdfReference(parent));
        return page;
    }
}
