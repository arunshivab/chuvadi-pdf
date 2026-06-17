// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.4 (xref), §7.7.3 (page tree)
// Verifies that an operation performed on a document recovered from a corrupt
// cross-reference table emits a clean file that reopens without further repair.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class RecoveredResaveTests
{
    [Fact]
    public void Merge_OnRecoveredDocument_WritesCleanReopenableFile()
    {
        byte[] corrupt = BuildCorruptPdf();

        byte[] resaved;
        using (MemoryStream ms = new MemoryStream(corrupt, writable: false))
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            doc.IsRecovered.Should().BeTrue();

            using MemoryStream output = new MemoryStream();
            PageOperations.Merge(output, doc);
            resaved = output.ToArray();
        }

        using MemoryStream reopened = new MemoryStream(resaved, writable: false);
        using PdfDocument clean = PdfDocument.Open(reopened, leaveOpen: true);

        clean.IsRecovered.Should().BeFalse();
        clean.PageCount.Should().Be(2);
        clean.Pages[0].MediaBox.Width.Should().BeApproximately(612, 0.5);
        clean.Pages[1].MediaBox.Width.Should().BeApproximately(612, 0.5);
    }

    // Builds a valid two-page PDF and repoints object 3's xref entry at object
    // 5's existing offset (a content stream), reproducing the MRDDFF.pdf class of
    // corruption without changing any byte offsets.
    private static byte[] BuildCorruptPdf()
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

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        byte[] bytes;
        using (MemoryStream ms = new MemoryStream())
        {
            PdfWriter.Write(ms, objects, trailer);
            bytes = ms.ToArray();
        }

        int offset5 = XrefOffsetOf(bytes, 5);
        WriteXrefOffset(bytes, 3, offset5);
        return bytes;
    }

    private static int XrefEntryTableStart(byte[] bytes)
    {
        string text = Encoding.Latin1.GetString(bytes);
        int xrefPos = text.LastIndexOf("\nxref", StringComparison.Ordinal);
        int afterXref = text.IndexOf('\n', xrefPos + 1) + 1;
        int afterHeader = text.IndexOf('\n', afterXref) + 1;
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
