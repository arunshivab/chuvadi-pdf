// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.8 (streams), §7.8.2 (content streams),
//        §14.3 (metadata)
// PHASE: Phase 1 — Chuvadi.Pdf.Operations tests
//
// Covers the Phase 1 additions to PdfCompressor: byte-identical object
// deduplication, content-stream minification (with an inline-image bail), and
// opt-in metadata/document-info stripping. Each test compresses a synthetic
// PDF and asserts both the reported counters and the re-opened result.

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

public sealed class CompressorPhase1Tests
{
    private static readonly byte[] VerboseContent = Encoding.ASCII.GetBytes(
        "q\n\n\n   1   0   0   1   72   720   cm\n" +
        "% a comment that minification should strip\n" +
        "BT\n  /F1  12  Tf\n  (Hello   world) Tj\nET\nQ\n");

    private static readonly byte[] InlineImageContent = Encoding.ASCII.GetBytes(
        "q 100 0 0 100 0 0 cm\nBI /W 2 /H 2 /CS /RGB /BPC 8 ID \u0001\u0002\u0003\u0004\u0005\u0006 EI\nQ\n");

    private static readonly PdfName MetadataKey = PdfName.Intern("Metadata");
    private static readonly PdfName InfoKey = PdfName.Intern("Info");

    private static PdfDocument Open(byte[] pdf)
    {
        MemoryStream ms = new MemoryStream(pdf, writable: false);
        return PdfDocument.Open(ms, leaveOpen: false);
    }

    private static (CompressionResult Result, byte[] Output) Compress(
        byte[] source, CompressionOptions? options = null)
    {
        using PdfDocument document = Open(source);
        using MemoryStream output = new MemoryStream();
        CompressionResult result = PdfCompressor.Compress(document, output, options);
        return (result, output.ToArray());
    }

    // Single-page PDF whose page content is the supplied stream bytes.
    private static byte[] BuildSinglePage(byte[] content)
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId pageId = new(3, 0);
        PdfObjectId contentId = new(4, 0);

        PdfDictionary contentDict = new();
        PdfDictionary pageDict = new();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Intern("Contents"), new PdfReference(contentId));
        pageDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(595), new PdfInteger(842),
        }));

        PdfArray kids = new(new PdfPrimitive[] { new PdfReference(pageId) });
        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(catalogId, catalogDict),
        };

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }

    // Two pages that reference two byte-identical font dictionaries (objects 5 and 6).
    private static byte[] BuildWithDuplicateObjects()
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId page1Id = new(3, 0);
        PdfObjectId page2Id = new(4, 0);
        PdfObjectId font1Id = new(5, 0);
        PdfObjectId font2Id = new(6, 0);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        foreach (PdfObjectId fontId in new[] { font1Id, font2Id })
        {
            PdfDictionary fontDict = new();
            fontDict.Set(PdfName.Type, PdfName.Intern("Font"));
            fontDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
            fontDict.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Helvetica"));
            objects.Add(new PdfIndirectObject(fontId, fontDict));
        }

        AddPageWithFont(objects, page1Id, pagesId, font1Id);
        AddPageWithFont(objects, page2Id, pagesId, font2Id);

        PdfArray kids = new(new PdfPrimitive[] { new PdfReference(page1Id), new PdfReference(page2Id) });
        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 2);
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalogDict));

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }

    private static void AddPageWithFont(
        List<PdfIndirectObject> objects, PdfObjectId pageId, PdfObjectId pagesId, PdfObjectId fontId)
    {
        PdfDictionary fonts = new();
        fonts.Set(PdfName.Intern("F1"), new PdfReference(fontId));
        PdfDictionary resources = new();
        resources.Set(PdfName.Intern("Font"), fonts);

        PdfDictionary page = new();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.Intern("Resources"), resources);
        page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(595), new PdfInteger(842),
        }));
        objects.Add(new PdfIndirectObject(pageId, page));
    }

    // Single-page PDF whose catalog carries an XMP /Metadata stream and whose
    // trailer references a document /Info dictionary.
    private static byte[] BuildWithMetadataAndInfo()
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId pageId = new(3, 0);
        PdfObjectId metadataId = new(4, 0);
        PdfObjectId infoId = new(5, 0);

        byte[] xmp = Encoding.ASCII.GetBytes("<?xpacket?><x:xmpmeta>secret-xmp</x:xmpmeta>");
        PdfDictionary metaDict = new();
        metaDict.Set(PdfName.Type, MetadataKey);
        metaDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("XML"));

        PdfDictionary infoDict = new();
        infoDict.Set(PdfName.Intern("Producer"), new PdfString("SecretProducer"));

        PdfDictionary pageDict = new();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(595), new PdfInteger(842),
        }));

        PdfArray kids = new(new PdfPrimitive[] { new PdfReference(pageId) });
        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        catalogDict.Set(MetadataKey, new PdfReference(metadataId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(metadataId, new PdfStream(metaDict, xmp)),
            new PdfIndirectObject(infoId, infoDict),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(catalogId, catalogDict),
        };

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        trailer.Set(InfoKey, new PdfReference(infoId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }

    // ── Deduplication ─────────────────────────────────────────────────────

    [Fact]
    public void Compress_ByteIdenticalObjects_AreMerged()
    {
        byte[] source = BuildWithDuplicateObjects();

        (CompressionResult result, byte[] output) = Compress(source);

        result.DuplicatesRemoved.Should().BeGreaterThanOrEqualTo(1);
        using PdfDocument reopened = Open(output);
        reopened.PageCount.Should().Be(2);
    }

    // ── Content-stream minification ───────────────────────────────────────

    [Fact]
    public void Compress_VerboseContentStream_IsMinified()
    {
        byte[] source = BuildSinglePage(VerboseContent);

        (CompressionResult result, byte[] output) = Compress(source);

        result.ContentStreamsMinified.Should().BeGreaterThanOrEqualTo(1);
        using PdfDocument reopened = Open(output);
        reopened.PageCount.Should().Be(1);
    }

    [Fact]
    public void Compress_InlineImageContentStream_IsNotMinified()
    {
        // The minifier must bail on inline images (BI/ID/EI), whose binary payload
        // would otherwise be corrupted by tokenisation.
        byte[] source = BuildSinglePage(InlineImageContent);

        (CompressionResult result, byte[] output) = Compress(source);

        result.ContentStreamsMinified.Should().Be(0);
        using PdfDocument reopened = Open(output);
        reopened.PageCount.Should().Be(1);
    }

    // ── Metadata / document-info stripping ────────────────────────────────

    [Fact]
    public void Compress_Default_PreservesMetadataAndInfo()
    {
        byte[] source = BuildWithMetadataAndInfo();

        (_, byte[] output) = Compress(source);

        using PdfDocument reopened = Open(output);
        (reopened.Catalog is not null && reopened.Catalog.ContainsKey(MetadataKey)).Should().BeTrue();
        reopened.Trailer.ContainsKey(InfoKey).Should().BeTrue();
    }

    [Fact]
    public void Compress_RemoveMetadata_DropsCatalogMetadata()
    {
        byte[] source = BuildWithMetadataAndInfo();
        CompressionOptions options = new() { RemoveMetadata = true };

        (_, byte[] output) = Compress(source, options);

        using PdfDocument reopened = Open(output);
        bool hasMetadata = reopened.Catalog is not null && reopened.Catalog.ContainsKey(MetadataKey);
        hasMetadata.Should().BeFalse();
        Encoding.Latin1.GetString(output).Should().NotContain("secret-xmp");
    }

    [Fact]
    public void Compress_RemoveDocumentInfo_DropsTrailerInfo()
    {
        byte[] source = BuildWithMetadataAndInfo();
        CompressionOptions options = new() { RemoveDocumentInfo = true };

        (_, byte[] output) = Compress(source, options);

        using PdfDocument reopened = Open(output);
        reopened.Trailer.ContainsKey(InfoKey).Should().BeFalse();
        Encoding.Latin1.GetString(output).Should().NotContain("SecretProducer");
    }
}
