// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.3.3 — Document information dictionary
//        PDF 32000-1:2008 §14.3.2 — Metadata streams
//        PDF 32000-1:2008 §7.9.4 — Date strings
// PHASE: v2.0.0 — metadata read-side tests

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Documents.Tests;

public sealed class DocumentMetadataTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal PDF with an /Info dictionary populated from the
    /// given entries. Each entry maps a PDF name (e.g. "CreationDate") to
    /// any <see cref="PdfPrimitive"/>.
    /// </summary>
    private static MemoryStream BuildPdfWithInfo(IReadOnlyDictionary<string, PdfPrimitive> infoEntries)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId infoId = new PdfObjectId(3, 0);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([]));
        pagesDict.Set(PdfName.Count, 0);

        PdfDictionary infoDict = new PdfDictionary();

        foreach (KeyValuePair<string, PdfPrimitive> kvp in infoEntries)
        {
            infoDict.Set(PdfName.Intern(kvp.Key), kvp.Value);
        }

        PdfIndirectObject[] objects =
        [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(infoId, infoDict),
        ];

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        trailer.Set(PdfName.Intern("Info"), new PdfReference(infoId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }

    /// <summary>Builds a minimal PDF with no /Info entry in the trailer.</summary>
    private static MemoryStream BuildPdfWithoutInfo()
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
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }

    // Hand-assembled PDF that genuinely lacks /Info and /Metadata (the writer
    // now always injects those), to exercise the reader's absent-metadata path.
    private static MemoryStream BuildRawPdfWithoutMetadata()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("%PDF-1.7\n");
        int off1 = sb.Length;
        sb.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        int off2 = sb.Length;
        sb.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");
        int xrefPos = sb.Length;
        sb.Append("xref\n0 3\n");
        sb.Append("0000000000 65535 f\r\n");
        sb.Append(off1.ToString("D10", CultureInfo.InvariantCulture));
        sb.Append(" 00000 n\r\n");
        sb.Append(off2.ToString("D10", CultureInfo.InvariantCulture));
        sb.Append(" 00000 n\r\n");
        sb.Append("trailer\n<< /Root 1 0 R /Size 3 >>\nstartxref\n");
        sb.Append(xrefPos.ToString(CultureInfo.InvariantCulture));
        sb.Append("\n%%EOF");
        return new MemoryStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    /// <summary>
    /// Builds a minimal PDF with a /Metadata stream attached to the Catalog.
    /// </summary>
    private static MemoryStream BuildPdfWithMetadataStream(byte[] xmpBytes)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId metaId = new PdfObjectId(3, 0);

        PdfDictionary metaDictHeader = new PdfDictionary();
        metaDictHeader.Set(PdfName.Type, PdfName.Intern("Metadata"));
        metaDictHeader.Set(PdfName.Subtype, PdfName.Intern("XML"));
        metaDictHeader.Set(PdfName.Length, xmpBytes.Length);
        PdfStream metaStream = new PdfStream(metaDictHeader, xmpBytes);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));
        catalogDict.Set(PdfName.Intern("Metadata"), new PdfReference(metaId));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([]));
        pagesDict.Set(PdfName.Count, 0);

        PdfIndirectObject[] objects =
        [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(metaId, metaStream),
        ];

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }

    // ── CreationDate ──────────────────────────────────────────────────────

    [Fact]
    public void CreationDate_AbsentInfo_IsNull()
    {
        using (MemoryStream ms = BuildPdfWithoutInfo())
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            doc.CreationDate.Should().BeNull();
        }
    }

    [Fact]
    public void CreationDate_AbsentEntry_IsNull()
    {
        Dictionary<string, PdfPrimitive> info = new Dictionary<string, PdfPrimitive>
        {
            ["Title"] = new PdfString("Untitled"),
        };

        using (MemoryStream ms = BuildPdfWithInfo(info))
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            doc.CreationDate.Should().BeNull();
        }
    }

    [Fact]
    public void CreationDate_UtcDate_ParsesCorrectly()
    {
        Dictionary<string, PdfPrimitive> info = new Dictionary<string, PdfPrimitive>
        {
            ["CreationDate"] = new PdfString("D:20260522143000Z"),
        };

        using (MemoryStream ms = BuildPdfWithInfo(info))
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            DateTimeOffset? d = doc.CreationDate;
            d.Should().NotBeNull();
            d!.Value.Year.Should().Be(2026);
            d.Value.Month.Should().Be(5);
            d.Value.Day.Should().Be(22);
            d.Value.Hour.Should().Be(14);
            d.Value.Minute.Should().Be(30);
            d.Value.Second.Should().Be(0);
            d.Value.Offset.Should().Be(TimeSpan.Zero);
        }
    }

    [Fact]
    public void CreationDate_OffsetDate_ParsesOffsetCorrectly()
    {
        Dictionary<string, PdfPrimitive> info = new Dictionary<string, PdfPrimitive>
        {
            ["CreationDate"] = new PdfString("D:20260522143000+05'30'"),
        };

        using (MemoryStream ms = BuildPdfWithInfo(info))
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            DateTimeOffset? d = doc.CreationDate;
            d.Should().NotBeNull();
            d!.Value.Offset.Should().Be(new TimeSpan(5, 30, 0));
        }
    }

    [Fact]
    public void CreationDate_DateOnly_DefaultsToMidnightUtc()
    {
        Dictionary<string, PdfPrimitive> info = new Dictionary<string, PdfPrimitive>
        {
            ["CreationDate"] = new PdfString("D:20260522"),
        };

        using (MemoryStream ms = BuildPdfWithInfo(info))
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            DateTimeOffset? d = doc.CreationDate;
            d.Should().NotBeNull();
            d!.Value.Hour.Should().Be(0);
            d.Value.Minute.Should().Be(0);
            d.Value.Second.Should().Be(0);
            d.Value.Offset.Should().Be(TimeSpan.Zero);
        }
    }

    [Fact]
    public void CreationDate_Malformed_IsNull()
    {
        Dictionary<string, PdfPrimitive> info = new Dictionary<string, PdfPrimitive>
        {
            ["CreationDate"] = new PdfString("not-a-date"),
        };

        using (MemoryStream ms = BuildPdfWithInfo(info))
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            doc.CreationDate.Should().BeNull();
        }
    }

    // ── ModDate ───────────────────────────────────────────────────────────

    [Fact]
    public void ModDate_AbsentInfo_IsNull()
    {
        using (MemoryStream ms = BuildPdfWithoutInfo())
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            doc.ModDate.Should().BeNull();
        }
    }

    [Fact]
    public void ModDate_PresentDate_Parses()
    {
        Dictionary<string, PdfPrimitive> info = new Dictionary<string, PdfPrimitive>
        {
            ["ModDate"] = new PdfString("D:20251231235959Z"),
        };

        using (MemoryStream ms = BuildPdfWithInfo(info))
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            DateTimeOffset? d = doc.ModDate;
            d.Should().NotBeNull();
            d!.Value.Year.Should().Be(2025);
            d.Value.Month.Should().Be(12);
            d.Value.Day.Should().Be(31);
            d.Value.Hour.Should().Be(23);
            d.Value.Minute.Should().Be(59);
            d.Value.Second.Should().Be(59);
        }
    }

    // ── Trapped ───────────────────────────────────────────────────────────

    [Fact]
    public void Trapped_AbsentInfo_IsNull()
    {
        using (MemoryStream ms = BuildPdfWithoutInfo())
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            doc.Trapped.Should().BeNull();
        }
    }

    [Fact]
    public void Trapped_AbsentEntry_IsNull()
    {
        Dictionary<string, PdfPrimitive> info = new Dictionary<string, PdfPrimitive>
        {
            ["Title"] = new PdfString("Untitled"),
        };

        using (MemoryStream ms = BuildPdfWithInfo(info))
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            doc.Trapped.Should().BeNull();
        }
    }

    [Fact]
    public void Trapped_AsName_ReturnsValue()
    {
        Dictionary<string, PdfPrimitive> info = new Dictionary<string, PdfPrimitive>
        {
            ["Trapped"] = PdfName.Intern("True"),
        };

        using (MemoryStream ms = BuildPdfWithInfo(info))
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            doc.Trapped.Should().Be("True");
        }
    }

    [Fact]
    public void Trapped_AsString_LenientlyAccepted()
    {
        // Some producers erroneously write /Trapped as a string.
        // The reader accepts both forms.
        Dictionary<string, PdfPrimitive> info = new Dictionary<string, PdfPrimitive>
        {
            ["Trapped"] = new PdfString("False"),
        };

        using (MemoryStream ms = BuildPdfWithInfo(info))
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            doc.Trapped.Should().Be("False");
        }
    }

    // ── XmpMetadata ───────────────────────────────────────────────────────

    [Fact]
    public void XmpMetadata_AbsentEntry_IsNull()
    {
        using (MemoryStream ms = BuildRawPdfWithoutMetadata())
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            doc.XmpMetadata.Should().BeNull();
        }
    }

    [Fact]
    public void XmpMetadata_PresentStream_ReturnsBytes()
    {
        byte[] xmp = Encoding.UTF8.GetBytes(
            "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>" +
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"></x:xmpmeta>" +
            "<?xpacket end=\"w\"?>");

        using (MemoryStream ms = BuildPdfWithMetadataStream(xmp))
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            byte[]? result = doc.XmpMetadata;
            result.Should().NotBeNull();
            result!.Should().Equal(xmp);
        }
    }

    // ── Encryption — null case only ───────────────────────────────────────
    // (Encrypted-PDF round-trip is covered by Chuvadi.Pdf.Encryption.Tests;
    //  direct EncryptionInfo unit tests live in EncryptionInfoTests.cs.)

    [Fact]
    public void Encryption_UnencryptedDocument_IsNull()
    {
        using (MemoryStream ms = BuildPdfWithoutInfo())
        using (PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true))
        {
            doc.Encryption.Should().BeNull();
        }
    }
}
