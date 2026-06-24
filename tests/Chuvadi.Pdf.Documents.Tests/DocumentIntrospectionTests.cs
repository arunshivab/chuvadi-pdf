// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.7.2 (StructTreeRoot), §14.8 (Tagged PDF),
//        §7.8.2 (page /Contents)
// Coverage for PdfDocument.IsTagged / HasStructTree / StructTreeRoot and
// PdfPage.HasContent — first-class introspection so consumers need not sniff
// catalog keys or render a page to discover these facts. HasStructTree resolves
// the reference (a dangling /StructTreeRoot reports false), which is more robust
// than testing for the catalog key alone.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class DocumentIntrospectionTests
{
    private enum StructTree
    {
        Absent,
        Resolvable,
        Dangling,
    }

    private enum PageContents
    {
        Absent,
        NonEmptyStream,
        EmptyStream,
        ArrayWithNonEmpty,
    }

    [Fact]
    public void IsTagged_True_WhenMarkInfoMarkedTrue()
    {
        using MemoryStream ms = BuildPdf(marked: true);
        using PdfDocument doc = OpenPdf(ms);

        doc.IsTagged.Should().BeTrue();
    }

    [Fact]
    public void IsTagged_False_WhenMarkInfoMarkedFalse()
    {
        using MemoryStream ms = BuildPdf(marked: false);
        using PdfDocument doc = OpenPdf(ms);

        doc.IsTagged.Should().BeFalse();
    }

    [Fact]
    public void IsTagged_False_WhenNoMarkInfo()
    {
        using MemoryStream ms = BuildPdf(marked: null);
        using PdfDocument doc = OpenPdf(ms);

        doc.IsTagged.Should().BeFalse();
    }

    [Fact]
    public void HasStructTree_True_AndExposesRoot_WhenResolvable()
    {
        using MemoryStream ms = BuildPdf(structTree: StructTree.Resolvable);
        using PdfDocument doc = OpenPdf(ms);

        doc.HasStructTree.Should().BeTrue();
        doc.StructTreeRoot.Should().NotBeNull();
    }

    [Fact]
    public void HasStructTree_False_WhenStructTreeRootDangling()
    {
        using MemoryStream ms = BuildPdf(structTree: StructTree.Dangling);
        using PdfDocument doc = OpenPdf(ms);

        doc.HasStructTree.Should().BeFalse();
        doc.StructTreeRoot.Should().BeNull();
    }

    [Fact]
    public void HasStructTree_False_WhenAbsent()
    {
        using MemoryStream ms = BuildPdf(structTree: StructTree.Absent);
        using PdfDocument doc = OpenPdf(ms);

        doc.HasStructTree.Should().BeFalse();
        doc.StructTreeRoot.Should().BeNull();
    }

    [Fact]
    public void HasContent_True_WhenContentsStreamNonEmpty()
    {
        using MemoryStream ms = BuildPdf(contents: PageContents.NonEmptyStream);
        using PdfDocument doc = OpenPdf(ms);

        doc.Pages[0].HasContent.Should().BeTrue();
    }

    [Fact]
    public void HasContent_False_WhenNoContents()
    {
        using MemoryStream ms = BuildPdf(contents: PageContents.Absent);
        using PdfDocument doc = OpenPdf(ms);

        doc.Pages[0].HasContent.Should().BeFalse();
    }

    [Fact]
    public void HasContent_False_WhenContentsStreamEmpty()
    {
        using MemoryStream ms = BuildPdf(contents: PageContents.EmptyStream);
        using PdfDocument doc = OpenPdf(ms);

        doc.Pages[0].HasContent.Should().BeFalse();
    }

    [Fact]
    public void HasContent_True_WhenContentsArrayHasNonEmptyStream()
    {
        using MemoryStream ms = BuildPdf(contents: PageContents.ArrayWithNonEmpty);
        using PdfDocument doc = OpenPdf(ms);

        doc.Pages[0].HasContent.Should().BeTrue();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // Builds a minimal one-page PDF. Object ids: 1 catalog, 2 pages, 3 page,
    // 5 struct-tree root (when resolvable), 6/7 content streams (when present).
    private static MemoryStream BuildPdf(
        bool? marked = null,
        StructTree structTree = StructTree.Absent,
        PageContents contents = PageContents.Absent)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId markInfoId = new PdfObjectId(4, 0);
        PdfObjectId structId = new PdfObjectId(5, 0);
        PdfObjectId contentAId = new PdfObjectId(6, 0);
        PdfObjectId contentBId = new PdfObjectId(7, 0);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfArray kids = new PdfArray([]);
        kids.Add(new PdfReference(pageId));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 1);
        pagesDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(200), new PdfInteger(200)
        ]));

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
        };

        if (marked.HasValue)
        {
            PdfDictionary markInfo = new PdfDictionary();
            markInfo.Set(PdfName.Intern("Marked"), marked.Value);
            objects.Add(new PdfIndirectObject(markInfoId, markInfo));
            catalogDict.Set(PdfName.Intern("MarkInfo"), new PdfReference(markInfoId));
        }

        if (structTree == StructTree.Resolvable)
        {
            PdfDictionary structRoot = new PdfDictionary();
            structRoot.Set(PdfName.Type, PdfName.Intern("StructTreeRoot"));
            objects.Add(new PdfIndirectObject(structId, structRoot));
            catalogDict.Set(PdfName.Intern("StructTreeRoot"), new PdfReference(structId));
        }
        else if (structTree == StructTree.Dangling)
        {
            // Reference an object that is never written — it resolves to null.
            catalogDict.Set(PdfName.Intern("StructTreeRoot"), new PdfReference(new PdfObjectId(99, 0)));
        }

        AddContents(objects, pageDict, contents, contentAId, contentBId);

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    private static void AddContents(
        List<PdfIndirectObject> objects,
        PdfDictionary pageDict,
        PageContents contents,
        PdfObjectId contentAId,
        PdfObjectId contentBId)
    {
        byte[] marks = Encoding.ASCII.GetBytes("0 0 0 rg 0 0 100 100 re f");
        byte[] empty = System.Array.Empty<byte>();

        if (contents == PageContents.NonEmptyStream)
        {
            objects.Add(new PdfIndirectObject(contentAId, MakeStream(marks)));
            pageDict.Set(PdfName.Contents, new PdfReference(contentAId));
        }
        else if (contents == PageContents.EmptyStream)
        {
            objects.Add(new PdfIndirectObject(contentAId, MakeStream(empty)));
            pageDict.Set(PdfName.Contents, new PdfReference(contentAId));
        }
        else if (contents == PageContents.ArrayWithNonEmpty)
        {
            objects.Add(new PdfIndirectObject(contentAId, MakeStream(empty)));
            objects.Add(new PdfIndirectObject(contentBId, MakeStream(marks)));
            pageDict.Set(PdfName.Contents, new PdfArray([
                new PdfReference(contentAId), new PdfReference(contentBId)
            ]));
        }
    }

    private static PdfStream MakeStream(byte[] bytes)
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Length, bytes.Length);
        return new PdfStream(dict, bytes);
    }

    private static PdfDocument OpenPdf(MemoryStream ms)
    {
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }
}
