// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3 — Page tree, §7.8.3 — Resource dictionaries
// PHASE: Phase 2.9 — PageBuilder deep-copy regression
//
// Guards the fix for the shallow-copy / in-place-remap defect: page-tree
// operations must deep-copy so they never mutate the source document, and
// must correctly renumber references nested inside shared resource
// dictionaries and arrays. Earlier code shared the /Resources instance
// between the copy and the source and remapped it in place, which both
// corrupted the source (breaking any later operation on the same document)
// and scrambled references for multi-page documents that share resources.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class PageBuilderSharedResourceTests
{
    // Builds a PDF whose pages all share ONE font dictionary object and ONE
    // image XObject object, referenced from each page's /Resources.
    private static MemoryStream BuildSharedResourcePdf(int pageCount)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId fontId = new PdfObjectId(3, 0);
        PdfObjectId imageId = new PdfObjectId(4, 0);

        PdfDictionary fontDict = new PdfDictionary();
        fontDict.Set(PdfName.Type, PdfName.Intern("Font"));
        fontDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        fontDict.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Helvetica"));

        PdfDictionary imageDict = new PdfDictionary();
        imageDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        imageDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        imageDict.Set(PdfName.Intern("Width"), 2);
        imageDict.Set(PdfName.Intern("Height"), 2);
        imageDict.Set(PdfName.Intern("BitsPerComponent"), 8);
        imageDict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
        byte[] imageSamples = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255];
        PdfStream imageStream = new PdfStream(imageDict, imageSamples);

        PdfArray kidsArray = new PdfArray([]);
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kidsArray);
        pagesDict.Set(PdfName.Count, pageCount);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(fontId, fontDict),
            new PdfIndirectObject(imageId, imageStream),
        };

        for (int i = 0; i < pageCount; i++)
        {
            PdfObjectId pageId = new PdfObjectId(5 + i, 0);

            PdfDictionary fontResources = new PdfDictionary();
            fontResources.Set(PdfName.Intern("F1"), new PdfReference(fontId));

            PdfDictionary xobjResources = new PdfDictionary();
            xobjResources.Set(PdfName.Intern("Im0"), new PdfReference(imageId));

            PdfDictionary resources = new PdfDictionary();
            resources.Set(PdfName.Intern("Font"), fontResources);
            resources.Set(PdfName.Intern("XObject"), xobjResources);

            PdfArray mediaBox = new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(595), new PdfInteger(842)
            ]);

            PdfDictionary pageDict = new PdfDictionary();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            pageDict.Set(PdfName.Intern("Resources"), resources);
            pageDict.Set(PdfName.MediaBox, mediaBox);

            objects.Add(new PdfIndirectObject(pageId, pageDict));
            kidsArray.Add(new PdfReference(pageId));
        }

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }

    private static PdfDocument Open(MemoryStream ms)
    {
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }

    // Resolves page[index]/Resources/XObject/Im0 and returns it, or null.
    private static PdfStream? ResolveImage(PdfDocument doc, int pageIndex)
    {
        PdfDictionary page = doc.Pages[pageIndex].Dictionary;
        if (doc.Objects.Resolve(page[PdfName.Intern("Resources")]) is not PdfDictionary res)
        {
            return null;
        }
        if (doc.Objects.Resolve(res[PdfName.Intern("XObject")]) is not PdfDictionary xobj)
        {
            return null;
        }
        return doc.Objects.Resolve(xobj[PdfName.Intern("Im0")]) as PdfStream;
    }

    private static int FontRefNumber(PdfDocument doc, int pageIndex)
    {
        PdfDictionary page = doc.Pages[pageIndex].Dictionary;
        PdfDictionary res = (PdfDictionary)doc.Objects.Resolve(page[PdfName.Intern("Resources")]);
        PdfDictionary fonts = (PdfDictionary)doc.Objects.Resolve(res[PdfName.Intern("Font")]);
        return ((PdfReference)fonts[PdfName.Intern("F1")]).ObjectId.ObjectNumber;
    }

    [Fact]
    public void SplitPages_DoesNotMutateSourceDocument()
    {
        using MemoryStream src = BuildSharedResourcePdf(3);
        using PdfDocument doc = Open(src);

        int before = FontRefNumber(doc, 0);
        List<MemoryStream> parts = PageOperations.SplitPages(doc);
        int after = FontRefNumber(doc, 0);

        after.Should().Be(before, "splitting must not rewrite the source document's references");
        parts.Should().HaveCount(3);
    }

    [Fact]
    public void SplitPages_PreservesSharedFontAndImageOnEveryPage()
    {
        using MemoryStream src = BuildSharedResourcePdf(3);
        using PdfDocument doc = Open(src);

        List<MemoryStream> parts = PageOperations.SplitPages(doc);

        foreach (MemoryStream part in parts)
        {
            using PdfDocument outDoc = Open(part);
            outDoc.PageCount.Should().Be(1);

            PdfStream? image = ResolveImage(outDoc, 0);
            image.Should().NotBeNull("the shared image must survive the split");
            image!.RawBytes.Length.Should().Be(12);

            PdfDictionary page = outDoc.Pages[0].Dictionary;
            PdfDictionary res = (PdfDictionary)outDoc.Objects.Resolve(page[PdfName.Intern("Resources")]);
            PdfDictionary fonts = (PdfDictionary)outDoc.Objects.Resolve(res[PdfName.Intern("Font")]);
            PdfPrimitive resolvedFont = outDoc.Objects.Resolve(fonts[PdfName.Intern("F1")]);
            resolvedFont.Should().BeOfType<PdfDictionary>("the shared font must resolve to a real object");
        }
    }

    [Fact]
    public void Merge_PreservesSharedResourcesAcrossAllPages()
    {
        using MemoryStream src1 = BuildSharedResourcePdf(2);
        using MemoryStream src2 = BuildSharedResourcePdf(3);
        using PdfDocument a = Open(src1);
        using PdfDocument b = Open(src2);

        using MemoryStream merged = new MemoryStream();
        PageOperations.Merge(merged, a, b);

        using PdfDocument outDoc = Open(merged);
        outDoc.PageCount.Should().Be(5);

        for (int i = 0; i < outDoc.PageCount; i++)
        {
            PdfStream? image = ResolveImage(outDoc, i);
            image.Should().NotBeNull($"page {i} must keep its image after merge");
            image!.RawBytes.Length.Should().Be(12);
        }
    }

    [Fact]
    public void SplitThenSecondOperation_OnSameDocument_StillSeesResources()
    {
        // The corruption symptom: a second operation on a document that was
        // already split used to find scrambled references.
        using MemoryStream src = BuildSharedResourcePdf(2);
        using PdfDocument doc = Open(src);

        PageOperations.SplitPages(doc);

        // Second split must still produce pages with intact image resources.
        List<MemoryStream> second = PageOperations.SplitPages(doc);
        second.Should().HaveCount(2);
        using PdfDocument outDoc = Open(second[0]);
        ResolveImage(outDoc, 0).Should().NotBeNull("a repeated operation must still see the resources");
    }
}
