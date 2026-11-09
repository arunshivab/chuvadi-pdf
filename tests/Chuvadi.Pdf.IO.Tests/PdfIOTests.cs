// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 — File structure
// PHASE: Phase 1 — Chuvadi.Pdf.IO tests

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class PdfWriterTests
{
    // ── Helper ────────────────────────────────────────────────────────────

    private static MemoryStream BuildMinimalPdf()
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([]));
        pagesDict.Set(PdfName.Count, 0);

        PdfIndirectObject[] objects =
        [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
        ];

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }

    // ── PdfParseException ─────────────────────────────────────────────────

    [Fact]
    public void PdfParseException_DefaultConstructor_HasMessage()
    {
        PdfParseException ex = new PdfParseException();
        ex.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void PdfParseException_Message_Preserved()
    {
        PdfParseException ex = new PdfParseException("test error");
        ex.Message.Should().Be("test error");
    }

    [Fact]
    public void PdfParseException_InnerException_Preserved()
    {
        InvalidOperationException inner = new InvalidOperationException("inner");
        PdfParseException ex = new PdfParseException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }

    // ── PdfWriter — structural correctness ────────────────────────────────

    [Fact]
    public void Write_ProducesNonEmptyStream()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Length.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Write_StartsWithPdfHeader()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Seek(0, SeekOrigin.Begin);
            byte[] header = new byte[8];
            ms.Read(header, 0, 8);
            Encoding.ASCII.GetString(header).Should().StartWith("%PDF-1.");
        }
    }

    [Fact]
    public void Write_ContainsStartxrefAndEof()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            string content = Encoding.Latin1.GetString(ms.ToArray());
            content.Should().Contain("startxref");
            content.Should().Contain("%%EOF");
        }
    }

    [Fact]
    public void Write_ContainsXrefSection()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            string content = Encoding.Latin1.GetString(ms.ToArray());
            content.Should().Contain("xref");
            content.Should().Contain("trailer");
        }
    }

    [Fact]
    public void Write_ContainsObjectDefinitions()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            string content = Encoding.Latin1.GetString(ms.ToArray());
            content.Should().Contain("1 0 obj");
            content.Should().Contain("2 0 obj");
            content.Should().Contain("endobj");
        }
    }

    [Fact]
    public void Write_NullOutput_Throws()
    {
        Action act = () => PdfWriter.Write(null!, [], new PdfDictionary());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Write_NullObjects_Throws()
    {
        Action act = () => PdfWriter.Write(new MemoryStream(), null!, new PdfDictionary());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Write_NullTrailer_Throws()
    {
        Action act = () => PdfWriter.Write(new MemoryStream(), [], null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── PdfReader — open written PDF ──────────────────────────────────────

    [Fact]
    public void Reader_Open_ReadsTrailer()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfReader reader = PdfReader.Open(ms, leaveOpen: true))
            {
                reader.Trailer.Should().NotBeNull();
                reader.Trailer.ContainsKey(PdfName.Root).Should().BeTrue();
                reader.Trailer.ContainsKey(PdfName.Size).Should().BeTrue();
            }
        }
    }

    [Fact]
    public void Reader_Open_ReadsCatalog()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfReader reader = PdfReader.Open(ms, leaveOpen: true))
            {
                PdfDictionary? catalog = reader.Catalog;
                catalog.Should().NotBeNull();
                catalog!.Type.Should().Be(PdfName.Catalog);
            }
        }
    }

    [Fact]
    public void Reader_Open_ReadsPageTree()
    {
        using (MemoryStream ms = BuildMinimalPdf())
        {
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfReader reader = PdfReader.Open(ms, leaveOpen: true))
            {
                PdfDictionary? catalog = reader.Catalog;
                catalog.Should().NotBeNull();

                PdfPrimitive? pagesRef = catalog!.GetAs<PdfPrimitive>(PdfName.Pages);
                pagesRef.Should().BeOfType<PdfReference>();

                PdfPrimitive resolvedPages = reader.Objects.Resolve(pagesRef!);
                resolvedPages.Should().BeOfType<PdfDictionary>();

                PdfDictionary pagesDict = (PdfDictionary)resolvedPages;
                pagesDict.Type.Should().Be(PdfName.Pages);
                pagesDict.GetInteger(PdfName.Count).Should().Be(0);
            }
        }
    }

    [Fact]
    public void Reader_Open_NullStream_Throws()
    {
        Action act = () => PdfReader.Open(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Reader_Open_EmptyStream_Throws()
    {
        using (MemoryStream ms = new MemoryStream())
        {
            Action act = () => PdfReader.Open(ms, leaveOpen: true);
            act.Should().Throw<PdfParseException>();
        }
    }

    // ── PdfReader.OpenAsync ───────────────────────────────────────────────

    [Fact]
    public async Task Reader_OpenAsync_ValidStream_ReadsTrailer()
    {
        // OpenAsync round-trip: buffer the PDF bytes, parse, verify trailer
        // contents match what Open() produces.
        using MemoryStream ms = BuildMinimalPdf();
        ms.Seek(0, SeekOrigin.Begin);

        using PdfReader reader = await PdfReader.OpenAsync(ms);

        reader.Trailer.Should().NotBeNull();
        reader.Trailer.ContainsKey(PdfName.Root).Should().BeTrue();
        reader.Trailer.ContainsKey(PdfName.Size).Should().BeTrue();
    }

    [Fact]
    public async Task Reader_OpenAsync_NullStream_Throws()
    {
        Func<Task> act = async () => await PdfReader.OpenAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Reader_OpenAsync_CancelledToken_Throws()
    {
        // A token cancelled before invocation should surface as
        // OperationCanceledException without parsing any bytes.
        using MemoryStream ms = BuildMinimalPdf();
        ms.Seek(0, SeekOrigin.Begin);

        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await PdfReader.OpenAsync(ms, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── PdfReader — malformed input handling ──────────────────────────────

    [Fact]
    public void Open_OversizedIntegerInDictionary_OpensWithTheValueAsAReal()
    {
        // A structurally valid minimal PDF whose pages dict has a /Count value
        // that exceeds Int32.MaxValue. It once caused an unhandled
        // OverflowException (found by the pdf-open fuzz target); up to 3.31.2
        // it threw PdfParseException. Since 3.32.0 (D-278) an integer
        // outside the 32-bit range is kept as a real, as other readers do, so
        // the file opens; the page tree is read from /Kids, which is empty.
        byte[] pdf = BuildPdfWithOversizedInteger();

        using MemoryStream ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: false);

        doc.PageCount.Should().Be(0);
        PdfDictionary pages = (PdfDictionary)doc.Objects.Resolve(new PdfReference(2, 0));
        pages[PdfName.Intern("Count")].Should().BeOfType<PdfReal>().Which.Value.Should().Be(9999999999d);
    }

    private static byte[] BuildPdfWithOversizedInteger()
    {
        // Hand-built minimal PDF, xref-consistent, with the pages dict's
        // /Count set to 9999999999 (exceeds Int32.MaxValue = 2147483647).
        // Byte offsets are computed against the literal layout below; do
        // not edit individual lines without recomputing the xref entries
        // and the startxref offset.
        string pdf =
            "%PDF-1.4\n" +                                       //  9 bytes (0-8)
            "1 0 obj\n" +                                        //  8 bytes (9-16)
            "<< /Type /Catalog /Pages 2 0 R >>\n" +              // 34 bytes (17-50)
            "endobj\n" +                                         //  7 bytes (51-57)
            "2 0 obj\n" +                                        //  8 bytes (58-65)
            "<< /Type /Pages /Kids [] /Count 9999999999 >>\n" +  // 46 bytes (66-111)
            "endobj\n" +                                         //  7 bytes (112-118)
            "xref\n" +                                           //  5 bytes (119-123)
            "0 3\n" +                                            //  4 bytes (124-127)
            "0000000000 65535 f \n" +                            // 20 bytes (128-147)
            "0000000009 00000 n \n" +                            // 20 bytes (148-167)
            "0000000058 00000 n \n" +                            // 20 bytes (168-187)
            "trailer\n" +                                        //  8 bytes (188-195)
            "<< /Root 1 0 R /Size 3 >>\n" +                      // 26 bytes (196-221)
            "startxref\n" +                                      // 10 bytes (222-231)
            "119\n" +                                            //  4 bytes (232-235)
            "%%EOF";                                             //  5 bytes (236-240)
        return Encoding.Latin1.GetBytes(pdf);
    }

    // ── Round-trip: integer value preserved ───────────────────────────────

    [Fact]
    public void RoundTrip_IntegerValue_PreservesValue()
    {
        using (MemoryStream ms = new MemoryStream())
        {
            PdfObjectId catalogId = new PdfObjectId(1, 0);
            PdfObjectId pagesId = new PdfObjectId(2, 0);
            PdfObjectId dataId = new PdfObjectId(3, 0);

            PdfDictionary catalogDict = new PdfDictionary();
            catalogDict.Set(PdfName.Type, PdfName.Catalog);
            catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

            PdfDictionary pagesDict = new PdfDictionary();
            pagesDict.Set(PdfName.Type, PdfName.Pages);
            pagesDict.Set(PdfName.Kids, new PdfArray([]));
            pagesDict.Set(PdfName.Count, 0);

            PdfDictionary dataDict = new PdfDictionary();
            dataDict.Set(PdfName.Intern("Answer"), (PdfPrimitive)new PdfInteger(42));

            PdfIndirectObject[] objects =
            [
                new PdfIndirectObject(catalogId, catalogDict),
                new PdfIndirectObject(pagesId, pagesDict),
                new PdfIndirectObject(dataId, dataDict),
            ];

            PdfDictionary trailer = new PdfDictionary();
            trailer.Set(PdfName.Root, new PdfReference(catalogId));

            PdfWriter.Write(ms, objects, trailer);
            ms.Seek(0, SeekOrigin.Begin);

            using (PdfReader reader = PdfReader.Open(ms, leaveOpen: true))
            {
                PdfPrimitive dataObj = reader.Objects.ResolveById(dataId);
                dataObj.Should().BeOfType<PdfDictionary>();

                PdfDictionary dataResult = (PdfDictionary)dataObj;
                dataResult.GetInteger(PdfName.Intern("Answer")).Should().Be(42);
            }
        }
    }
}
