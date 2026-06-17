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

    // ── Granular stripping fixtures (#10) ─────────────────────────────────

    private static readonly PdfName NamesKey = PdfName.Intern("Names");
    private static readonly PdfName JavaScriptKey = PdfName.Intern("JavaScript");
    private static readonly PdfName EmbeddedFilesKey = PdfName.Intern("EmbeddedFiles");
    private static readonly PdfName OpenActionKey = PdfName.Intern("OpenAction");
    private static readonly PdfName AaKey = PdfName.Intern("AA");
    private static readonly PdfName AfKey = PdfName.Intern("AF");
    private static readonly PdfName ThumbKey = PdfName.Intern("Thumb");
    private static readonly PdfName PieceInfoKey = PdfName.Intern("PieceInfo");
    private static readonly PdfName StructTreeRootKey = PdfName.Intern("StructTreeRoot");
    private static readonly PdfName MarkInfoKey = PdfName.Intern("MarkInfo");
    private static readonly PdfName AnnotsKey = PdfName.Intern("Annots");
    private static readonly PdfName SubtypeKey = PdfName.Intern("Subtype");
    private static readonly PdfName FileAttachmentName = PdfName.Intern("FileAttachment");

    // Single-page PDF whose catalog and page carry one of every strippable
    // category: document JavaScript (/Names /JavaScript, /OpenAction, /AA),
    // embedded attachments (/Names /EmbeddedFiles, /AF, a FileAttachment
    // annotation), a page /Thumb, /PieceInfo on catalog and page, a
    // /StructTreeRoot + /MarkInfo, and a link annotation kept alongside.
    private static byte[] BuildWithStrippables()
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId pageId = new(3, 0);
        PdfObjectId namesId = new(4, 0);
        PdfObjectId jsTreeId = new(5, 0);
        PdfObjectId jsActionId = new(6, 0);
        PdfObjectId efTreeId = new(7, 0);
        PdfObjectId filespecId = new(8, 0);
        PdfObjectId embeddedId = new(9, 0);
        PdfObjectId thumbId = new(10, 0);
        PdfObjectId structId = new(11, 0);
        PdfObjectId linkAnnotId = new(12, 0);
        PdfObjectId fileAnnotId = new(13, 0);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        // JavaScript: name tree leaf -> action with a /JS payload.
        PdfDictionary jsAction = new();
        jsAction.Set(PdfName.Intern("S"), JavaScriptKey);
        jsAction.Set(PdfName.Intern("JS"), new PdfString("app.alert('secret-js-code');"));
        objects.Add(new PdfIndirectObject(jsActionId, jsAction));

        PdfDictionary jsTree = new();
        jsTree.Set(NamesKey, new PdfArray(new PdfPrimitive[]
        {
            new PdfString("script-one"), new PdfReference(jsActionId),
        }));
        objects.Add(new PdfIndirectObject(jsTreeId, jsTree));

        // Attachment: name tree leaf -> filespec -> embedded file stream.
        PdfDictionary embeddedDict = new();
        embeddedDict.Set(PdfName.Type, PdfName.Intern("EmbeddedFile"));
        objects.Add(new PdfIndirectObject(
            embeddedId, new PdfStream(embeddedDict, Encoding.ASCII.GetBytes("embedded-file-secret-payload"))));

        PdfDictionary ef = new();
        ef.Set(PdfName.Intern("F"), new PdfReference(embeddedId));
        PdfDictionary filespec = new();
        filespec.Set(PdfName.Type, PdfName.Intern("Filespec"));
        filespec.Set(PdfName.Intern("F"), new PdfString("file-one.txt"));
        filespec.Set(PdfName.Intern("EF"), ef);
        objects.Add(new PdfIndirectObject(filespecId, filespec));

        PdfDictionary efTree = new();
        efTree.Set(NamesKey, new PdfArray(new PdfPrimitive[]
        {
            new PdfString("file-one.txt"), new PdfReference(filespecId),
        }));
        objects.Add(new PdfIndirectObject(efTreeId, efTree));

        PdfDictionary names = new();
        names.Set(JavaScriptKey, new PdfReference(jsTreeId));
        names.Set(EmbeddedFilesKey, new PdfReference(efTreeId));
        objects.Add(new PdfIndirectObject(namesId, names));

        // Page thumbnail image stream.
        PdfDictionary thumbDict = new();
        thumbDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
        thumbDict.Set(PdfName.Intern("Width"), 1);
        thumbDict.Set(PdfName.Intern("Height"), 1);
        objects.Add(new PdfIndirectObject(
            thumbId, new PdfStream(thumbDict, Encoding.ASCII.GetBytes("thumbnail-pixels-secret"))));

        // Structure tree root.
        PdfDictionary structRoot = new();
        structRoot.Set(PdfName.Type, StructTreeRootKey);
        structRoot.Set(PdfName.Intern("CvMark"), new PdfString("structtree-secret"));
        objects.Add(new PdfIndirectObject(structId, structRoot));

        // Annotations: a kept link and a stripped file-attachment.
        PdfDictionary linkAnnot = new();
        linkAnnot.Set(PdfName.Type, PdfName.Intern("Annot"));
        linkAnnot.Set(SubtypeKey, PdfName.Intern("Link"));
        objects.Add(new PdfIndirectObject(linkAnnotId, linkAnnot));

        PdfDictionary fileAnnot = new();
        fileAnnot.Set(PdfName.Type, PdfName.Intern("Annot"));
        fileAnnot.Set(SubtypeKey, FileAttachmentName);
        fileAnnot.Set(PdfName.Intern("FS"), new PdfReference(filespecId));
        objects.Add(new PdfIndirectObject(fileAnnotId, fileAnnot));

        // Page.
        PdfDictionary pagePiece = new();
        pagePiece.Set(PdfName.Intern("CvApp"), new PdfString("pieceinfo-page-secret"));
        PdfDictionary page = new();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(595), new PdfInteger(842),
        }));
        page.Set(ThumbKey, new PdfReference(thumbId));
        page.Set(PieceInfoKey, pagePiece);
        page.Set(AnnotsKey, new PdfArray(new PdfPrimitive[]
        {
            new PdfReference(linkAnnotId), new PdfReference(fileAnnotId),
        }));
        objects.Add(new PdfIndirectObject(pageId, page));

        // Pages node.
        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pagesDict.Set(PdfName.Count, 1);
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        // Catalog with inline JavaScript open-action / additional-actions and
        // a /PieceInfo, /MarkInfo, /AF.
        PdfDictionary openAction = new();
        openAction.Set(PdfName.Intern("S"), JavaScriptKey);
        openAction.Set(PdfName.Intern("JS"), new PdfString("openaction-js-secret();"));

        PdfDictionary willClose = new();
        willClose.Set(PdfName.Intern("S"), JavaScriptKey);
        willClose.Set(PdfName.Intern("JS"), new PdfString("aa-js-secret();"));
        PdfDictionary additionalActions = new();
        additionalActions.Set(PdfName.Intern("WC"), willClose);

        PdfDictionary catalogPiece = new();
        catalogPiece.Set(PdfName.Intern("CvApp"), new PdfString("pieceinfo-cat-secret"));

        PdfDictionary markInfo = new();
        markInfo.Set(PdfName.Intern("Marked"), true);

        PdfDictionary catalog = new();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        catalog.Set(NamesKey, new PdfReference(namesId));
        catalog.Set(OpenActionKey, openAction);
        catalog.Set(AaKey, additionalActions);
        catalog.Set(PieceInfoKey, catalogPiece);
        catalog.Set(StructTreeRootKey, new PdfReference(structId));
        catalog.Set(MarkInfoKey, markInfo);
        catalog.Set(AfKey, new PdfArray(new PdfPrimitive[] { new PdfReference(filespecId) }));
        objects.Add(new PdfIndirectObject(catalogId, catalog));

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }

    private static PdfDictionary FirstPage(PdfDocument doc)
    {
        PdfDictionary catalog = doc.Catalog;
        PdfDictionary pages = (PdfDictionary)doc.Objects.Resolve(catalog[PdfName.Pages]);
        PdfArray kids = (PdfArray)doc.Objects.Resolve(pages[PdfName.Kids]);
        return (PdfDictionary)doc.Objects.Resolve(kids[0]);
    }

    private static PdfDictionary CatalogNames(PdfDocument doc)
    {
        return (PdfDictionary)doc.Objects.Resolve(doc.Catalog[NamesKey]);
    }

    // ── Granular stripping behaviour (#10) ────────────────────────────────

    [Fact]
    public void Compress_Default_PreservesAllStrippableCategories()
    {
        byte[] source = BuildWithStrippables();

        (_, byte[] output) = Compress(source);

        using PdfDocument doc = Open(output);
        PdfDictionary catalog = doc.Catalog;
        catalog.ContainsKey(OpenActionKey).Should().BeTrue();
        catalog.ContainsKey(AaKey).Should().BeTrue();
        catalog.ContainsKey(PieceInfoKey).Should().BeTrue();
        catalog.ContainsKey(StructTreeRootKey).Should().BeTrue();
        catalog.ContainsKey(MarkInfoKey).Should().BeTrue();
        catalog.ContainsKey(AfKey).Should().BeTrue();
        CatalogNames(doc).ContainsKey(JavaScriptKey).Should().BeTrue();
        CatalogNames(doc).ContainsKey(EmbeddedFilesKey).Should().BeTrue();
        PdfDictionary page = FirstPage(doc);
        page.ContainsKey(ThumbKey).Should().BeTrue();
        page.ContainsKey(PieceInfoKey).Should().BeTrue();
        page.ContainsKey(AnnotsKey).Should().BeTrue();
    }

    [Fact]
    public void Compress_RemoveJavaScript_DropsScriptsAndActions()
    {
        byte[] source = BuildWithStrippables();
        CompressionOptions options = new() { RemoveJavaScript = true };

        (_, byte[] output) = Compress(source, options);

        using PdfDocument doc = Open(output);
        doc.Catalog.ContainsKey(OpenActionKey).Should().BeFalse();
        doc.Catalog.ContainsKey(AaKey).Should().BeFalse();
        CatalogNames(doc).ContainsKey(JavaScriptKey).Should().BeFalse();
        CatalogNames(doc).ContainsKey(EmbeddedFilesKey).Should().BeTrue();
        Encoding.Latin1.GetString(output).Should().NotContain("secret-js-code");
    }

    [Fact]
    public void Compress_RemoveAttachments_DropsEmbeddedFilesAndFileAnnots()
    {
        byte[] source = BuildWithStrippables();
        CompressionOptions options = new() { RemoveAttachments = true };

        (_, byte[] output) = Compress(source, options);

        using PdfDocument doc = Open(output);
        doc.Catalog.ContainsKey(AfKey).Should().BeFalse();
        CatalogNames(doc).ContainsKey(EmbeddedFilesKey).Should().BeFalse();

        PdfDictionary page = FirstPage(doc);
        PdfArray annots = (PdfArray)doc.Objects.Resolve(page[AnnotsKey]);
        annots.Count.Should().Be(1);
        PdfDictionary kept = (PdfDictionary)doc.Objects.Resolve(annots[0]);
        ((PdfName)doc.Objects.Resolve(kept[SubtypeKey])).Value.Should().Be("Link");
        Encoding.Latin1.GetString(output).Should().NotContain("embedded-file-secret-payload");
    }

    [Fact]
    public void Compress_RemoveThumbnails_DropsPageThumb()
    {
        byte[] source = BuildWithStrippables();
        CompressionOptions options = new() { RemoveThumbnails = true };

        (_, byte[] output) = Compress(source, options);

        using PdfDocument doc = Open(output);
        FirstPage(doc).ContainsKey(ThumbKey).Should().BeFalse();
        Encoding.Latin1.GetString(output).Should().NotContain("thumbnail-pixels-secret");
    }

    [Fact]
    public void Compress_RemovePieceInfo_DropsCatalogAndPagePieceInfo()
    {
        byte[] source = BuildWithStrippables();
        CompressionOptions options = new() { RemovePieceInfo = true };

        (_, byte[] output) = Compress(source, options);

        using PdfDocument doc = Open(output);
        doc.Catalog.ContainsKey(PieceInfoKey).Should().BeFalse();
        FirstPage(doc).ContainsKey(PieceInfoKey).Should().BeFalse();
    }

    [Fact]
    public void Compress_RemoveStructTree_DropsStructureAndMarkInfo()
    {
        byte[] source = BuildWithStrippables();
        CompressionOptions options = new() { RemoveStructTree = true };

        (_, byte[] output) = Compress(source, options);

        using PdfDocument doc = Open(output);
        doc.Catalog.ContainsKey(StructTreeRootKey).Should().BeFalse();
        doc.Catalog.ContainsKey(MarkInfoKey).Should().BeFalse();
        Encoding.Latin1.GetString(output).Should().NotContain("structtree-secret");
    }

    [Fact]
    public void Compress_RemoveAnnotations_DropsPageAnnots()
    {
        byte[] source = BuildWithStrippables();
        CompressionOptions options = new() { RemoveAnnotations = true };

        (_, byte[] output) = Compress(source, options);

        using PdfDocument doc = Open(output);
        FirstPage(doc).ContainsKey(AnnotsKey).Should().BeFalse();
    }

    [Fact]
    public void Compress_RemoveAll_StripsEverythingAndStaysReadable()
    {
        byte[] source = BuildWithStrippables();
        CompressionOptions options = new()
        {
            RemoveMetadata = true,
            RemoveJavaScript = true,
            RemoveAttachments = true,
            RemoveThumbnails = true,
            RemovePieceInfo = true,
            RemoveStructTree = true,
            RemoveAnnotations = true,
        };

        (CompressionResult result, byte[] output) = Compress(source, options);

        result.ObjectsRemoved.Should().BeGreaterThanOrEqualTo(1);
        using PdfDocument doc = Open(output);
        doc.PageCount.Should().Be(1);
        PdfDictionary catalog = doc.Catalog;
        catalog.ContainsKey(OpenActionKey).Should().BeFalse();
        catalog.ContainsKey(AaKey).Should().BeFalse();
        catalog.ContainsKey(PieceInfoKey).Should().BeFalse();
        catalog.ContainsKey(StructTreeRootKey).Should().BeFalse();
        catalog.ContainsKey(MarkInfoKey).Should().BeFalse();
        catalog.ContainsKey(AfKey).Should().BeFalse();
        PdfDictionary page = FirstPage(doc);
        page.ContainsKey(ThumbKey).Should().BeFalse();
        page.ContainsKey(PieceInfoKey).Should().BeFalse();
        page.ContainsKey(AnnotsKey).Should().BeFalse();
    }

    // ── Incremental-update flattening (#6) ────────────────────────────────

    [Fact]
    public void Compress_IncrementalUpdate_FlattensToSingleGeneration()
    {
        byte[] v0 = BuildSinglePage(VerboseContent);

        // Supersede the catalog with an added marker via an incremental update,
        // producing a two-section file chained by /Prev.
        int rootNumber;
        byte[] v1;
        using (PdfDocument d0 = Open(v0))
        {
            PdfReference rootRef = (PdfReference)d0.Trailer[PdfName.Root];
            rootNumber = rootRef.ObjectNumber;
            PdfDictionary catalog = (PdfDictionary)d0.Objects.ResolveById(new PdfObjectId(rootNumber, 0));
            PdfDictionary updated = new();
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in catalog)
            {
                updated.Set(entry.Key, entry.Value);
            }
            updated.Set(PdfName.Intern("CvMarker"), new PdfString("VERSION_TWO"));
            PdfIndirectObject updatedObj = new(new PdfObjectId(rootNumber, 0), updated);
            v1 = PdfWriter.WriteIncrementalUpdate(v0, new[] { updatedObj });
        }

        using (PdfDocument d1 = Open(v1))
        {
            d1.Trailer.ContainsKey(PdfName.Intern("Prev")).Should().BeTrue();
        }

        (CompressionResult result, byte[] v2) = Compress(v1);
        result.SkipReason.Should().Be(CompressionSkipReason.None);

        using PdfDocument d2 = Open(v2);
        d2.Trailer.ContainsKey(PdfName.Intern("Prev")).Should().BeFalse();
        PdfReference root2 = (PdfReference)d2.Trailer[PdfName.Root];
        PdfDictionary catalog2 = (PdfDictionary)d2.Objects.ResolveById(new PdfObjectId(root2.ObjectNumber, 0));
        catalog2.TryGetValue(PdfName.Intern("CvMarker"), out PdfPrimitive? marker).Should().BeTrue();
        ((PdfString)marker!).ToTextString().Should().Be("VERSION_TWO");
    }
}
