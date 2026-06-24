// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 (outline), §12.3.2 (destinations),
//        §7.9.2.2 (text-string / UTF-16BE)
// Verifies the outline-preserving Merge overload carries each input's bookmarks
// into the output with destination page indices re-based to the merged offsets,
// optionally nesting per document, and round-trips non-Latin1 titles via
// UTF-16BE. Merged outlines are read back with OutlineReader.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class OutlinePreservingMergeTests
{
    [Fact]
    public void Merge_PreserveOutlinesFalse_ProducesNoOutline()
    {
        using DocHandle a = BuildOutlined(3, new List<OutlineEntry> { new OutlineEntry("A-Sec", 2) });
        using DocHandle b = BuildOutlined(2, new List<OutlineEntry> { new OutlineEntry("B-Sec", 1) });
        using MemoryStream output = new MemoryStream();

        PageOperations.Merge(
            output,
            new List<PdfDocument> { a.Document, b.Document },
            new MergeOptions { PreserveOutlines = false });

        using PdfDocument merged = OpenPdf(output);
        merged.PageCount.Should().Be(5);
        OutlineReader.GetOutlines(merged).Should().BeEmpty();
    }

    [Fact]
    public void Merge_PreserveOutlines_RebasesDestinationIndices()
    {
        using DocHandle a = BuildOutlined(3, new List<OutlineEntry> { new OutlineEntry("A-Sec", 2) });
        using DocHandle b = BuildOutlined(2, new List<OutlineEntry> { new OutlineEntry("B-Sec", 1) });
        using MemoryStream output = new MemoryStream();

        PageOperations.Merge(
            output,
            new List<PdfDocument> { a.Document, b.Document },
            new MergeOptions { PreserveOutlines = true });

        using PdfDocument merged = OpenPdf(output);
        IReadOnlyList<OutlineItem> outlines = OutlineReader.GetOutlines(merged);

        outlines.Should().HaveCount(2);
        outlines[0].Title.Should().Be("A-Sec");
        outlines[0].DestinationPageIndex.Should().Be(2);
        outlines[1].Title.Should().Be("B-Sec");
        outlines[1].DestinationPageIndex.Should().Be(4); // 1 + offset 3
    }

    [Fact]
    public void Merge_WrapPerDocument_NestsUnderSuppliedTitles()
    {
        using DocHandle a = BuildOutlined(3, new List<OutlineEntry> { new OutlineEntry("A-Sec", 2) });
        using DocHandle b = BuildOutlined(2, new List<OutlineEntry> { new OutlineEntry("B-Sec", 1) });
        using MemoryStream output = new MemoryStream();

        PageOperations.Merge(
            output,
            new List<PdfDocument> { a.Document, b.Document },
            new MergeOptions
            {
                PreserveOutlines = true,
                WrapPerDocument = true,
                DocumentTitles = new List<string?> { "First", "Second" },
            });

        using PdfDocument merged = OpenPdf(output);
        IReadOnlyList<OutlineItem> outlines = OutlineReader.GetOutlines(merged);

        outlines.Should().HaveCount(2);

        outlines[0].Title.Should().Be("First");
        outlines[0].DestinationPageIndex.Should().Be(0);
        outlines[0].Children.Should().HaveCount(1);
        outlines[0].Children[0].Title.Should().Be("A-Sec");
        outlines[0].Children[0].DestinationPageIndex.Should().Be(2);

        outlines[1].Title.Should().Be("Second");
        outlines[1].DestinationPageIndex.Should().Be(3);
        outlines[1].Children.Should().HaveCount(1);
        outlines[1].Children[0].Title.Should().Be("B-Sec");
        outlines[1].Children[0].DestinationPageIndex.Should().Be(4);
    }

    [Fact]
    public void Merge_WrapPerDocument_FallsBackToDocumentN()
    {
        using DocHandle a = BuildOutlined(2, new List<OutlineEntry> { new OutlineEntry("A-Sec", 0) });
        using DocHandle b = BuildOutlined(2, new List<OutlineEntry> { new OutlineEntry("B-Sec", 1) });
        using MemoryStream output = new MemoryStream();

        PageOperations.Merge(
            output,
            new List<PdfDocument> { a.Document, b.Document },
            new MergeOptions { PreserveOutlines = true, WrapPerDocument = true });

        using PdfDocument merged = OpenPdf(output);
        IReadOnlyList<OutlineItem> outlines = OutlineReader.GetOutlines(merged);

        outlines.Should().HaveCount(2);
        outlines[0].Title.Should().Be("Document 1");
        outlines[1].Title.Should().Be("Document 2");
    }

    [Fact]
    public void Merge_PreservesNestedChildren_Rebased()
    {
        List<OutlineEntry> nested = new List<OutlineEntry>
        {
            new OutlineEntry("Parent", 0, new List<OutlineEntry> { new OutlineEntry("Child", 2) }),
        };
        using DocHandle a = BuildOutlined(3, nested);
        using DocHandle b = BuildOutlined(2, new List<OutlineEntry> { new OutlineEntry("B", 0) });
        using MemoryStream output = new MemoryStream();

        PageOperations.Merge(
            output,
            new List<PdfDocument> { a.Document, b.Document },
            new MergeOptions { PreserveOutlines = true });

        using PdfDocument merged = OpenPdf(output);
        IReadOnlyList<OutlineItem> outlines = OutlineReader.GetOutlines(merged);

        outlines.Should().HaveCount(2);
        outlines[0].Title.Should().Be("Parent");
        outlines[0].DestinationPageIndex.Should().Be(0);
        outlines[0].Children.Should().HaveCount(1);
        outlines[0].Children[0].Title.Should().Be("Child");
        outlines[0].Children[0].DestinationPageIndex.Should().Be(2);
        outlines[1].Title.Should().Be("B");
        outlines[1].DestinationPageIndex.Should().Be(3);
    }

    [Fact]
    public void Merge_NonLatin1Title_RoundTripsViaUtf16Be()
    {
        // Tamil "முன்னுரை" (Introduction) — all codepoints > 0xFF, so the merge
        // write path must emit FE FF UTF-16BE for the title to survive.
        const string Tamil = "முன்னுரை";
        using DocHandle a = BuildRawOutlinedDoc(Utf16BeWithBom(Tamil), destPageIndex: 1, pageCount: 2);
        using DocHandle b = BuildRawOutlinedDoc(Encoding.ASCII.GetBytes("Plain"), destPageIndex: 0, pageCount: 1);
        using MemoryStream output = new MemoryStream();

        PageOperations.Merge(
            output,
            new List<PdfDocument> { a.Document, b.Document },
            new MergeOptions { PreserveOutlines = true });

        using PdfDocument merged = OpenPdf(output);
        IReadOnlyList<OutlineItem> outlines = OutlineReader.GetOutlines(merged);

        outlines.Should().HaveCount(2);
        outlines[0].Title.Should().Be(Tamil);
        outlines[0].DestinationPageIndex.Should().Be(1);
        outlines[1].Title.Should().Be("Plain");
        outlines[1].DestinationPageIndex.Should().Be(2); // 0 + offset 2
    }

    [Fact]
    public void Merge_UnresolvedDestination_BecomesTitleOnly()
    {
        // Source bookmark with no /Dest and no /A -> read side reports -1.
        using DocHandle a = BuildRawOutlinedDoc(Encoding.ASCII.GetBytes("Orphan"), destPageIndex: -1, pageCount: 2);
        using MemoryStream output = new MemoryStream();

        PageOperations.Merge(
            output,
            new List<PdfDocument> { a.Document },
            new MergeOptions { PreserveOutlines = true });

        using PdfDocument merged = OpenPdf(output);
        IReadOnlyList<OutlineItem> outlines = OutlineReader.GetOutlines(merged);

        outlines.Should().HaveCount(1);
        outlines[0].Title.Should().Be("Orphan");
        outlines[0].DestinationPageIndex.Should().Be(-1);
    }

    [Fact]
    public void Merge_NullArguments_Throw()
    {
        using DocHandle a = BuildOutlined(1, new List<OutlineEntry> { new OutlineEntry("A", 0) });

        Action nullOutput = () => PageOperations.Merge(
            null!, new List<PdfDocument> { a.Document }, new MergeOptions());
        Action nullDocs = () => PageOperations.Merge(
            new MemoryStream(), null!, new MergeOptions());
        Action nullOptions = () => PageOperations.Merge(
            new MemoryStream(), new List<PdfDocument>(), null!);
        Action emptyDocs = () => PageOperations.Merge(
            new MemoryStream(), new List<PdfDocument>(), new MergeOptions());

        nullOutput.Should().Throw<ArgumentNullException>();
        nullDocs.Should().Throw<ArgumentNullException>();
        nullOptions.Should().Throw<ArgumentNullException>();
        emptyDocs.Should().Throw<OperationsException>();
    }

    [Fact]
    public void Merge_NullDocumentInList_Throws()
    {
        using MemoryStream output = new MemoryStream();
        List<PdfDocument> docs = new List<PdfDocument> { null! };

        Action act = () => PageOperations.Merge(output, docs, new MergeOptions { PreserveOutlines = true });

        act.Should().Throw<OperationsException>();
    }

    private static byte[] Utf16BeWithBom(string value)
    {
        byte[] body = Encoding.BigEndianUnicode.GetBytes(value);
        byte[] result = new byte[body.Length + 2];
        result[0] = 0xFE;
        result[1] = 0xFF;
        Array.Copy(body, 0, result, 2, body.Length);
        return result;
    }

    private static PdfDocument OpenPdf(MemoryStream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(stream, leaveOpen: true);
    }

    // Builds a plain N-page PDF, applies the given outline via OutlineWriter, and
    // reopens the result so OutlineReader sees explicit [pageRef /Fit] dests.
    private static DocHandle BuildOutlined(int pageCount, IReadOnlyList<OutlineEntry> entries)
    {
        using MemoryStream plain = BuildPlainPdf(pageCount);
        using PdfDocument source = PdfDocument.Open(plain, leaveOpen: true);

        MemoryStream outlined = new MemoryStream();
        OutlineWriter.Apply(outlined, source, entries);
        outlined.Seek(0, SeekOrigin.Begin);
        return new DocHandle(outlined, PdfDocument.Open(outlined, leaveOpen: true));
    }

    private static MemoryStream BuildPlainPdf(int pageCount)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        PdfArray kids = new PdfArray([]);

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, pageCount);
        pagesDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(600), new PdfInteger(800),
        ]));

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
        };

        for (int i = 0; i < pageCount; i++)
        {
            PdfObjectId pageId = new PdfObjectId(3 + i, 0);
            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            objects.Add(new PdfIndirectObject(pageId, pageDict));
            kids.Add(new PdfReference(pageId));
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    // Hand-builds a PDF with exactly one top-level outline item carrying the
    // given raw title bytes. When destPageIndex >= 0 the item gets an explicit
    // [pageRef /Fit] destination; otherwise it is title-only (read side -> -1).
    private static DocHandle BuildRawOutlinedDoc(byte[] titleBytes, int destPageIndex, int pageCount)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        PdfArray kids = new PdfArray([]);
        PdfObjectId[] pageIds = new PdfObjectId[pageCount];

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, pageCount);
        pagesDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(600), new PdfInteger(800),
        ]));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, new PdfDictionary()),
            new PdfIndirectObject(pagesId, pagesDict),
        };

        for (int i = 0; i < pageCount; i++)
        {
            pageIds[i] = new PdfObjectId(3 + i, 0);
            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            objects.Add(new PdfIndirectObject(pageIds[i], pageDict));
            kids.Add(new PdfReference(pageIds[i]));
        }

        PdfObjectId outlinesId = new PdfObjectId(3 + pageCount, 0);
        PdfObjectId itemId = new PdfObjectId(4 + pageCount, 0);

        PdfDictionary item = new PdfDictionary();
        item.Set(PdfName.Intern("Title"), new PdfString(titleBytes));
        item.Set(PdfName.Parent, new PdfReference(outlinesId));
        if (destPageIndex >= 0)
        {
            item.Set(PdfName.Intern("Dest"), new PdfArray([
                new PdfReference(pageIds[destPageIndex]), PdfName.Intern("Fit"),
            ]));
        }

        PdfDictionary outlines = new PdfDictionary();
        outlines.Set(PdfName.Type, PdfName.Outlines);
        outlines.Set(PdfName.Intern("First"), new PdfReference(itemId));
        outlines.Set(PdfName.Intern("Last"), new PdfReference(itemId));
        outlines.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        catalogDict.Set(PdfName.Outlines, new PdfReference(outlinesId));
        objects[0] = new PdfIndirectObject(catalogId, catalogDict);

        objects.Add(new PdfIndirectObject(outlinesId, outlines));
        objects.Add(new PdfIndirectObject(itemId, item));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return new DocHandle(ms, PdfDocument.Open(ms, leaveOpen: true));
    }

    // Holds an opened document together with its backing stream so the stream
    // stays alive for the document's lifetime.
    private sealed class DocHandle : IDisposable
    {
        private readonly MemoryStream _stream;

        internal DocHandle(MemoryStream stream, PdfDocument document)
        {
            _stream = stream;
            Document = document;
        }

        internal PdfDocument Document { get; }

        public void Dispose()
        {
            Document.Dispose();
            _stream.Dispose();
        }
    }
}
