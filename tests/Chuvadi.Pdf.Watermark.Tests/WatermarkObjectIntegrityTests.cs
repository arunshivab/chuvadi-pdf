// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 0 — watermark object-graph integrity (lazy-load fix)

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Watermark.Tests;

/// <summary>
/// Regression tests for the watermark rewrite's object-graph integrity. The
/// object store loads lazily, and <c>WatermarkDocument</c> previously numbered
/// new objects and copied originals from the partially-loaded store. On a
/// freshly opened document this (a) numbered watermark streams over existing
/// objects (collision) and (b) dropped objects reachable only from the catalog —
/// outlines, /Names, attachments, metadata, structure tree — because they were
/// never loaded. The constructor now force-loads the full trailer graph and
/// floors numbering at /Size.
/// </summary>
public sealed class WatermarkObjectIntegrityTests
{
    [Fact]
    public void ApplyText_FreshlyOpenedDoc_KeepsCatalogOnlyMetadataAndAvoidsIdCollisions()
    {
        byte[] pdf = BuildPdfWithCatalogOnlyMetadata(out int metadataNumber);

        using MemoryStream source = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);

        using MemoryStream output = new MemoryStream();
        WatermarkStamper.ApplyText(output, doc, new TextWatermarkOptions("DRAFT"));
        output.Position = 0;

        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);

        // Drop face: the catalog-only metadata stream (never reached by the page
        // walk) must survive intact. Pre-fix it was dropped -> resolves PdfNull.
        PdfPrimitive meta = result.Objects.ResolveById(new PdfObjectId(metadataNumber, 0));
        meta.Should().BeOfType<PdfStream>();
        Encoding.ASCII.GetString(((PdfStream)meta).RawBytes).Should().Contain("xmpmeta");

        // Collision face: the original page object must not be overwritten by a
        // watermark stream. Pre-fix, low next-id reuse collided onto it.
        PdfPrimitive page = result.Objects.ResolveById(new PdfObjectId(3, 0));
        page.Should().BeOfType<PdfDictionary>();
        ((PdfDictionary)page).GetAs<PdfName>(PdfName.Type)!.Value.Should().Be("Page");

        result.PageCount.Should().Be(1);
    }

    [Fact]
    public void ApplyText_PreservesDocumentInfoDictionary()
    {
        byte[] pdf = BuildPdfWithInfo();

        using MemoryStream source = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);

        using MemoryStream output = new MemoryStream();
        WatermarkStamper.ApplyText(output, doc, new TextWatermarkOptions("DRAFT"));
        output.Position = 0;

        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);

        // The original /Info must survive the rewrite. Pre-fix the trailer
        // dropped /Info and the writer synthesised a generic one, losing Title.
        result.Trailer.TryGetValue(PdfName.Intern("Info"), out PdfPrimitive? infoRef)
            .Should().BeTrue();
        PdfPrimitive info = result.Objects.Resolve(infoRef!);
        info.Should().BeOfType<PdfDictionary>();

        PdfPrimitive title = ((PdfDictionary)info).GetAs<PdfString>(PdfName.Intern("Title"))!;
        ((PdfString)title).ToTextString().Should().Be("Quarterly Report");
    }

    private static byte[] BuildPdfWithInfo()
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId infoId = new PdfObjectId(4, 0);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        PdfArray mediaBox = new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(595), new PdfInteger(842),
        ]);

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, mediaBox);
        objects.Add(new PdfIndirectObject(pageId, pageDict));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        PdfDictionary infoDict = new PdfDictionary();
        infoDict.Set(PdfName.Intern("Title"), new PdfString("Quarterly Report"));
        infoDict.Set(PdfName.Intern("Author"), new PdfString("Arun"));
        objects.Add(new PdfIndirectObject(infoId, infoDict));

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalogDict));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        trailer.Set(PdfName.Intern("Info"), new PdfReference(infoId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }

    private static byte[] BuildPdfWithCatalogOnlyMetadata(out int metadataNumber)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId metadataId = new PdfObjectId(4, 0);
        metadataNumber = metadataId.ObjectNumber;

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        PdfArray mediaBox = new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(595), new PdfInteger(842),
        ]);

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, mediaBox);
        objects.Add(new PdfIndirectObject(pageId, pageDict));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        // Metadata stream referenced only from the catalog — never reached by
        // the page walk, so the pre-fix lazy copy dropped it.
        byte[] xmp = Encoding.ASCII.GetBytes(
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"></x:xmpmeta>");
        PdfDictionary metaDict = new PdfDictionary();
        metaDict.Set(PdfName.Type, PdfName.Intern("Metadata"));
        metaDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("XML"));
        metaDict.Set(PdfName.Length, xmp.Length);
        objects.Add(new PdfIndirectObject(metadataId, new PdfStream(metaDict, xmp)));

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        catalogDict.Set(PdfName.Intern("Metadata"), new PdfReference(metadataId));
        objects.Add(new PdfIndirectObject(catalogId, catalogDict));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
