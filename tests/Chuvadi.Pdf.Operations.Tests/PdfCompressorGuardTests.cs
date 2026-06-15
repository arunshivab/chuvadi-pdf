// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 0 — PdfCompressor signed/encrypted safety guard

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

/// <summary>
/// Tests the safety guard that stops <see cref="PdfCompressor.Compress"/> from
/// silently breaking digital signatures or stripping encryption. The default is
/// batch-friendly: a hazardous document is skipped (nothing written) and the
/// reason is reported, rather than throwing. Callers opt in per hazard.
/// </summary>
public sealed class PdfCompressorGuardTests
{
    [Fact]
    public void Compress_SignedDocument_SkipsByDefault()
    {
        byte[] pdf = BuildSignedPdf();
        using MemoryStream source = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        CompressionResult result = PdfCompressor.Compress(doc, output);

        result.Skipped.Should().BeTrue();
        result.SkipReason.Should().Be(CompressionSkipReason.Signed);
        output.Length.Should().Be(0);
    }

    [Fact]
    public void Compress_SignedDocument_RewritesWhenAllowed()
    {
        byte[] pdf = BuildSignedPdf();
        using MemoryStream source = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        CompressionResult result = PdfCompressor.Compress(
            doc, output, new CompressionOptions { AllowSignedRewrite = true });

        result.Skipped.Should().BeFalse();
        result.SkipReason.Should().Be(CompressionSkipReason.None);
        output.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Compress_EncryptedDocument_SkipsByDefault()
    {
        byte[] pdf = BuildEncryptedPdf();
        using MemoryStream source = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Open(source, "pw", leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        CompressionResult result = PdfCompressor.Compress(doc, output);

        result.Skipped.Should().BeTrue();
        result.SkipReason.Should().Be(CompressionSkipReason.Encrypted);
        output.Length.Should().Be(0);
    }

    [Fact]
    public void Compress_EncryptedDocument_RewritesDecryptedWhenAllowed()
    {
        byte[] pdf = BuildEncryptedPdf();
        using MemoryStream source = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Open(source, "pw", leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        CompressionResult result = PdfCompressor.Compress(
            doc, output, new CompressionOptions { AllowEncryptedRewrite = true });

        result.Skipped.Should().BeFalse();
        output.Length.Should().BeGreaterThan(0);

        output.Position = 0;
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);
        reopened.Encryption.Should().BeNull();
        reopened.PageCount.Should().Be(1);
    }

    [Fact]
    public void Compress_PlainDocument_IsNotSkipped()
    {
        byte[] pdf = BuildPlainPdf();
        using MemoryStream source = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        CompressionResult result = PdfCompressor.Compress(doc, output);

        result.Skipped.Should().BeFalse();
        result.SkipReason.Should().Be(CompressionSkipReason.None);
        output.Length.Should().BeGreaterThan(0);
    }

    // ── builders ──────────────────────────────────────────────────────────

    private static byte[] BuildSignedPdf()
    {
        List<PdfIndirectObject> objects =
            BuildOnePageObjects(out PdfObjectId catalogId, out PdfDictionary catalog);

        PdfDictionary acroForm = new PdfDictionary();
        acroForm.Set(PdfName.Intern("Fields"), new PdfArray([]));
        // SigFlags 3 = SignaturesExist (bit 1) | AppendOnly (bit 2).
        acroForm.Set(PdfName.Intern("SigFlags"), 3);
        PdfObjectId acroId = new PdfObjectId(5, 0);
        objects.Add(new PdfIndirectObject(acroId, acroForm));
        catalog.Set(PdfName.Intern("AcroForm"), new PdfReference(acroId));

        return WritePlain(objects, catalogId);
    }

    private static byte[] BuildEncryptedPdf()
    {
        List<PdfIndirectObject> objects = BuildOnePageObjects(out PdfObjectId catalogId, out _);
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer, EncryptionOptions.Aes128("pw"));
        return ms.ToArray();
    }

    private static byte[] BuildPlainPdf()
    {
        List<PdfIndirectObject> objects = BuildOnePageObjects(out PdfObjectId catalogId, out _);
        return WritePlain(objects, catalogId);
    }

    private static byte[] WritePlain(List<PdfIndirectObject> objects, PdfObjectId catalogId)
    {
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }

    private static List<PdfIndirectObject> BuildOnePageObjects(
        out PdfObjectId catalogId, out PdfDictionary catalog)
    {
        catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        byte[] content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello) Tj ET");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);
        objects.Add(new PdfIndirectObject(contentId, new PdfStream(contentDict, content)));

        PdfArray mediaBox = new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(595), new PdfInteger(842),
        ]);

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, mediaBox);
        pageDict.Set(PdfName.Intern("Contents"), new PdfReference(contentId));
        objects.Add(new PdfIndirectObject(pageId, pageDict));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);
        objects.Add(new PdfIndirectObject(pagesId, pagesDict));

        catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        objects.Add(new PdfIndirectObject(catalogId, catalog));

        return objects;
    }
}
