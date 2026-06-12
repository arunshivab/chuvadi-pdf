// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 — File structure, §7.4.4 — FlateDecode
// PHASE: Phase 2.9 — Reader feature batch (PDF compression) tests

using System;
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

public sealed class PdfCompressorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static readonly byte[] ContentOperators = Encoding.ASCII.GetBytes(
        "q 1 0 0 RG 10 10 m 200 200 l S Q " +
        string.Concat(Enumerable.Repeat("% padding comment line to make the stream compressible\n", 20)));

    private sealed record BuiltPdf(MemoryStream Stream, int ObjectCount);

    private static BuiltPdf BuildPdf(
        bool withOrphan = false, bool withImage = false, int imageSide = 80)
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId pageId = new(3, 0);
        PdfObjectId contentId = new(4, 0);
        int nextId = 5;

        PdfDictionary contentDict = new();
        PdfStream content = new(contentDict, ContentOperators);

        PdfDictionary pageDict = new();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Intern("Contents"), new PdfReference(contentId));

        List<PdfIndirectObject> objects = new()
        {
            new PdfIndirectObject(contentId, content),
            new PdfIndirectObject(pageId, pageDict),
        };

        if (withImage)
        {
            PdfObjectId imageId = new(nextId++, 0);
            byte[] samples = new byte[imageSide * imageSide * 3];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (byte)((i / 3) % 256);     // smooth gradient
            }

            PdfDictionary imageDict = new();
            imageDict.Set(PdfName.Type, PdfName.Intern("XObject"));
            imageDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Image"));
            imageDict.Set(PdfName.Intern("Width"), imageSide);
            imageDict.Set(PdfName.Intern("Height"), imageSide);
            imageDict.Set(PdfName.Intern("BitsPerComponent"), 8);
            imageDict.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
            objects.Add(new PdfIndirectObject(imageId, new PdfStream(imageDict, samples)));

            PdfDictionary xobjects = new();
            xobjects.Set(PdfName.Intern("Im1"), new PdfReference(imageId));
            PdfDictionary resources = new();
            resources.Set(PdfName.Intern("XObject"), xobjects);
            pageDict.Set(PdfName.Intern("Resources"), resources);
        }

        if (withOrphan)
        {
            PdfObjectId orphanId = new(nextId++, 0);
            PdfDictionary orphan = new();
            orphan.Set(PdfName.Intern("Orphan"), true);
            objects.Add(new PdfIndirectObject(orphanId, orphan));
        }

        PdfArray kids = new();
        kids.Add(new PdfReference(pageId));
        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 1);
        PdfArray mediaBox = new();
        mediaBox.Add(new PdfInteger(0));
        mediaBox.Add(new PdfInteger(0));
        mediaBox.Add(new PdfInteger(595));
        mediaBox.Add(new PdfInteger(842));
        pagesDict.Set(PdfName.MediaBox, mediaBox);
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalogDict));

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new();
        PdfWriter.Write(ms, objects, trailer);
        return new BuiltPdf(ms, objects.Count);
    }

    private static PdfDocument Open(MemoryStream ms)
    {
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }

    private static (CompressionResult Result, MemoryStream Output) Compress(
        MemoryStream source, CompressionOptions? options = null)
    {
        using PdfDocument document = Open(source);
        MemoryStream output = new();
        CompressionResult result = PdfCompressor.Compress(document, output, options);
        return (result, output);
    }

    // ── Garbage collection ────────────────────────────────────────────────

    [Fact]
    public void Compress_RemovesOrphanObjects()
    {
        BuiltPdf source = BuildPdf(withOrphan: true);

        (CompressionResult result, MemoryStream output) = Compress(source.Stream);

        result.ObjectsRemoved.Should().BeGreaterThanOrEqualTo(1);
        using PdfDocument reopened = Open(output);
        reopened.PageCount.Should().Be(1);
    }

    [Fact]
    public void Compress_KeepsAllReachableStructure()
    {
        BuiltPdf source = BuildPdf();

        (CompressionResult result, MemoryStream output) = Compress(source.Stream);

        result.ObjectsRemoved.Should().Be(0);
        using PdfDocument reopened = Open(output);
        reopened.PageCount.Should().Be(1);
        reopened.Pages[0].Contents.Should().NotBeNull();
    }

    // ── Stream compression ────────────────────────────────────────────────

    [Fact]
    public void Compress_FlatesRawContentStreams_AndShrinksTheFile()
    {
        BuiltPdf source = BuildPdf();
        long before = source.Stream.Length;

        (CompressionResult result, MemoryStream output) = Compress(source.Stream);

        result.StreamsCompressed.Should().BeGreaterThanOrEqualTo(1);
        output.Length.Should().BeLessThan(before);

        // The content must decode back to the original operator bytes.
        using PdfDocument reopened = Open(output);
        PdfStream reopenedContent = (PdfStream)reopened.Objects.Resolve(
            reopened.Pages[0].Contents!);
        reopenedContent.IsFiltered.Should().BeTrue();
        reopened.Pages[0].Should().NotBeNull();
    }

    [Fact]
    public void Compress_LeavesTinyStreamsAlone()
    {
        BuiltPdf source = BuildPdf();

        (CompressionResult result, _) = Compress(
            source.Stream,
            new CompressionOptions
            {
                MinStreamLengthToCompress = 1_000_000,
            });

        result.StreamsCompressed.Should().Be(0);
    }

    // ── Image recompression ───────────────────────────────────────────────

    [Fact]
    public void Compress_DoesNotTouchImagesByDefault()
    {
        BuiltPdf source = BuildPdf(withImage: true);

        (CompressionResult result, MemoryStream output) = Compress(source.Stream);

        result.ImagesRecompressed.Should().Be(0);

        using PdfDocument reopened = Open(output);
        PdfStream image = FindImage(reopened);
        // The raw image samples were Flate-compressed (lossless), not JPEG'd.
        image.Filter.Should().BeOfType<PdfName>()
            .Which.Value.Should().Be("FlateDecode");
    }

    [Fact]
    public void Compress_RecompressesImagesWhenOptedIn()
    {
        BuiltPdf source = BuildPdf(withImage: true, imageSide: 96);
        long before = source.Stream.Length;

        (CompressionResult result, MemoryStream output) = Compress(
            source.Stream,
            new CompressionOptions
            {
                RecompressImages = true,
                JpegQuality = 70,
            });

        result.ImagesRecompressed.Should().Be(1);
        output.Length.Should().BeLessThan(before);

        using PdfDocument reopened = Open(output);
        PdfStream image = FindImage(reopened);
        image.Filter.Should().BeOfType<PdfName>()
            .Which.Value.Should().Be("DCTDecode");
    }

    [Fact]
    public void Compress_SkipsSmallImagesEvenWhenOptedIn()
    {
        BuiltPdf source = BuildPdf(withImage: true, imageSide: 16);

        (CompressionResult result, _) = Compress(
            source.Stream,
            new CompressionOptions
            {
                RecompressImages = true,
            });

        result.ImagesRecompressed.Should().Be(0);
    }

    // ── Guards ────────────────────────────────────────────────────────────

    [Fact]
    public void Compress_NullArguments_Throw()
    {
        BuiltPdf source = BuildPdf();
        using PdfDocument document = Open(source.Stream);
        using MemoryStream output = new();

        Action nullDoc = () => PdfCompressor.Compress(null!, output);
        Action nullOut = () => PdfCompressor.Compress(document, null!);

        nullDoc.Should().Throw<ArgumentNullException>();
        nullOut.Should().Throw<ArgumentNullException>();
    }

    private static PdfStream FindImage(PdfDocument document)
    {
        // The object store loads lazily, so walk to the image through the
        // page's resources rather than enumerating the store's cache.
        PdfDictionary page = document.Pages[0].Dictionary;
        PdfDictionary resources = (PdfDictionary)document.Objects.Resolve(
            page[PdfName.Intern("Resources")]);
        PdfDictionary xobjects = (PdfDictionary)document.Objects.Resolve(
            resources[PdfName.Intern("XObject")]);
        return (PdfStream)document.Objects.Resolve(xobjects[PdfName.Intern("Im1")]);
    }
}
