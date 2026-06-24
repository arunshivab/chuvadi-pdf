// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3 — Page tree
// Verifies PageOperations.Assemble builds output from an ordered (document,
// pageIndex) list that may repeat pages and interleave sources. Each source
// page carries a distinct MediaBox width so output order is checked directly.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class PageAssemblyTests
{
    [Fact]
    public void Assemble_NullOutput_Throws()
    {
        using MemoryStream raw = BuildTaggedPdf(10);
        using PdfDocument doc = OpenPdf(raw);
        List<PageSelector> pages = new List<PageSelector> { new PageSelector(doc, 0) };

        Action act = () => PageOperations.Assemble(null!, pages);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Assemble_NullPages_Throws()
    {
        using MemoryStream output = new MemoryStream();

        Action act = () => PageOperations.Assemble(output, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Assemble_EmptyPages_Throws()
    {
        using MemoryStream output = new MemoryStream();

        Action act = () => PageOperations.Assemble(output, new List<PageSelector>());

        act.Should().Throw<OperationsException>();
    }

    [Fact]
    public void Assemble_PageIndexTooLarge_Throws()
    {
        using MemoryStream raw = BuildTaggedPdf(10, 20);
        using PdfDocument doc = OpenPdf(raw);
        using MemoryStream output = new MemoryStream();
        List<PageSelector> pages = new List<PageSelector> { new PageSelector(doc, 5) };

        Action act = () => PageOperations.Assemble(output, pages);

        act.Should().Throw<OperationsException>();
    }

    [Fact]
    public void Assemble_NegativePageIndex_Throws()
    {
        using MemoryStream raw = BuildTaggedPdf(10, 20);
        using PdfDocument doc = OpenPdf(raw);
        using MemoryStream output = new MemoryStream();
        List<PageSelector> pages = new List<PageSelector> { new PageSelector(doc, -1) };

        Action act = () => PageOperations.Assemble(output, pages);

        act.Should().Throw<OperationsException>();
    }

    [Fact]
    public void Assemble_DefaultSelector_Throws()
    {
        using MemoryStream output = new MemoryStream();
        List<PageSelector> pages = new List<PageSelector> { default };

        Action act = () => PageOperations.Assemble(output, pages);

        act.Should().Throw<OperationsException>();
    }

    [Fact]
    public void Assemble_RepeatsSamePage()
    {
        using MemoryStream raw = BuildTaggedPdf(11, 22);
        using PdfDocument doc = OpenPdf(raw);
        using MemoryStream output = new MemoryStream();
        List<PageSelector> pages = new List<PageSelector>
        {
            new PageSelector(doc, 0),
            new PageSelector(doc, 0),
            new PageSelector(doc, 0),
        };

        PageOperations.Assemble(output, pages);

        using PdfDocument merged = OpenPdf(output);
        merged.PageCount.Should().Be(3);
        WidthOf(merged, 0).Should().Be(11);
        WidthOf(merged, 1).Should().Be(11);
        WidthOf(merged, 2).Should().Be(11);
    }

    [Fact]
    public void Assemble_InterleavesAcrossSources()
    {
        using MemoryStream rawA = BuildTaggedPdf(10, 20);
        using MemoryStream rawB = BuildTaggedPdf(30, 40);
        using PdfDocument docA = OpenPdf(rawA);
        using PdfDocument docB = OpenPdf(rawB);
        using MemoryStream output = new MemoryStream();

        // Output order: A[1], B[0], A[0], B[1] -> widths 20, 30, 10, 40.
        List<PageSelector> pages = new List<PageSelector>
        {
            new PageSelector(docA, 1),
            new PageSelector(docB, 0),
            new PageSelector(docA, 0),
            new PageSelector(docB, 1),
        };

        PageOperations.Assemble(output, pages);

        using PdfDocument merged = OpenPdf(output);
        merged.PageCount.Should().Be(4);
        WidthOf(merged, 0).Should().Be(20);
        WidthOf(merged, 1).Should().Be(30);
        WidthOf(merged, 2).Should().Be(10);
        WidthOf(merged, 3).Should().Be(40);
    }

    [Fact]
    public void Assemble_ReorderWithRepeatFromSingleSource()
    {
        using MemoryStream raw = BuildTaggedPdf(11, 22, 33);
        using PdfDocument doc = OpenPdf(raw);
        using MemoryStream output = new MemoryStream();

        // Output order: 2, 0, 1, 0 -> widths 33, 11, 22, 11.
        List<PageSelector> pages = new List<PageSelector>
        {
            new PageSelector(doc, 2),
            new PageSelector(doc, 0),
            new PageSelector(doc, 1),
            new PageSelector(doc, 0),
        };

        PageOperations.Assemble(output, pages);

        using PdfDocument merged = OpenPdf(output);
        merged.PageCount.Should().Be(4);
        WidthOf(merged, 0).Should().Be(33);
        WidthOf(merged, 1).Should().Be(11);
        WidthOf(merged, 2).Should().Be(22);
        WidthOf(merged, 3).Should().Be(11);
    }

    private static int WidthOf(PdfDocument document, int pageIndex)
    {
        return (int)document.Pages[pageIndex].MediaBox.Width;
    }

    private static PdfDocument OpenPdf(MemoryStream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(stream, leaveOpen: true);
    }

    // Builds an in-memory PDF with one page per supplied width; page i gets
    // MediaBox [0 0 width 800], making each page individually identifiable in
    // assembled output.
    private static MemoryStream BuildTaggedPdf(params int[] widths)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        PdfArray kids = new PdfArray([]);

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, widths.Length);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
        };

        for (int i = 0; i < widths.Length; i++)
        {
            PdfObjectId pageId = new PdfObjectId(3 + i, 0);
            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            pageDict.Set(PdfName.MediaBox, new PdfArray([
                new PdfInteger(0),
                new PdfInteger(0),
                new PdfInteger(widths[i]),
                new PdfInteger(800),
            ]));
            objects.Add(new PdfIndirectObject(pageId, pageDict));
            kids.Add(new PdfReference(pageId));
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }
}
