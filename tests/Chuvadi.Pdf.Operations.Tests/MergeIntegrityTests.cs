// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3 (page tree), §14.4 (file identifiers)
// Regression coverage for two Operations defects found via Chuvadi Reader:
//   1. Merge collapsed distinct content streams when inputs reused object
//      numbers across documents (remap keyed by bare object number).
//   2. Merge/ExtractPages wrote no trailer /ID, prompting Adobe to save on close.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class MergeIntegrityTests
{
    [Fact]
    public void Merge_CrossDocumentObjectNumberCollision_KeepsEachPageContent()
    {
        // Both documents number their content streams identically (objects 4,
        // 6, ...). The pre-fix remap keyed on the bare number, so document B's
        // pages collapsed onto document A's streams. Each page must keep its own.
        using MemoryStream aStream = BuildPdfWithContent("A", 2);
        using MemoryStream bStream = BuildPdfWithContent("B", 2);
        using PdfDocument a = OpenPdf(aStream);
        using PdfDocument b = OpenPdf(bStream);

        using MemoryStream mergedStream = new MemoryStream();
        PageOperations.Merge(mergedStream, a, b);

        using PdfDocument merged = OpenPdf(mergedStream);
        merged.PageCount.Should().Be(4);

        PageContent(merged, 0).Should().StartWith("A0");
        PageContent(merged, 1).Should().StartWith("A1");
        PageContent(merged, 2).Should().StartWith("B0");
        PageContent(merged, 3).Should().StartWith("B1");

        List<string> all = new List<string>
        {
            PageContent(merged, 0),
            PageContent(merged, 1),
            PageContent(merged, 2),
            PageContent(merged, 3),
        };
        all.Distinct().Should().HaveCount(4, "every page must keep a distinct content stream");
    }

    [Fact]
    public void Merge_WritesTrailerFileId()
    {
        using MemoryStream aStream = BuildPdfWithContent("A", 1);
        using PdfDocument a = OpenPdf(aStream);

        using MemoryStream mergedStream = new MemoryStream();
        PageOperations.Merge(mergedStream, a);

        using PdfDocument merged = OpenPdf(mergedStream);
        merged.Trailer.TryGetValue(PdfName.Intern("ID"), out PdfPrimitive? id).Should().BeTrue();
        (id as PdfArray).Should().NotBeNull();
    }

    [Fact]
    public void ExtractPages_WritesTrailerFileId()
    {
        using MemoryStream src = BuildPdfWithContent("A", 3);
        using PdfDocument doc = OpenPdf(src);

        using MemoryStream outStream = new MemoryStream();
        PageOperations.ExtractPages(outStream, doc, 1, 1);

        using PdfDocument extracted = OpenPdf(outStream);
        extracted.Trailer.TryGetValue(PdfName.Intern("ID"), out PdfPrimitive? id).Should().BeTrue();
    }

    [Fact]
    public void Write_FileId_IsDeterministicForIdenticalContent()
    {
        using MemoryStream first = BuildPdfWithContent("A", 2);
        using MemoryStream second = BuildPdfWithContent("A", 2);

        first.ToArray().Should().Equal(second.ToArray(),
            "identical content must produce a byte-identical file, including /ID");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // Builds a PDF whose pages each carry a distinct content stream. Content
    // streams land on the same object numbers across documents (catalog 1,
    // pages 2, then page/content pairs from 3), reproducing the cross-document
    // number collision when two such documents are merged.
    private static MemoryStream BuildPdfWithContent(string prefix, int pageCount)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        PdfArray kids = new PdfArray([]);
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, pageCount);
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
        for (int i = 0; i < pageCount; i++)
        {
            PdfObjectId pageId = new PdfObjectId(next++, 0);
            PdfObjectId contentId = new PdfObjectId(next++, 0);

            byte[] content = Encoding.ASCII.GetBytes($"{prefix}{i} BT (marker) Tj ET");
            PdfDictionary contentDict = new PdfDictionary();
            contentDict.Set(PdfName.Length, content.Length);
            PdfStream contentStream = new PdfStream(contentDict, content);

            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            pageDict.Set(PdfName.Contents, new PdfReference(contentId));

            objects.Add(new PdfIndirectObject(pageId, pageDict));
            objects.Add(new PdfIndirectObject(contentId, contentStream));
            kids.Add(new PdfReference(pageId));
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    private static PdfDocument OpenPdf(MemoryStream ms)
    {
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }

    private static string PageContent(PdfDocument doc, int index)
    {
        PdfPrimitive contents = doc.Pages[index].Contents!;
        PdfStream stream = (PdfStream)doc.Objects.Resolve(contents);
        return Encoding.ASCII.GetString(stream.RawBytes);
    }
}
