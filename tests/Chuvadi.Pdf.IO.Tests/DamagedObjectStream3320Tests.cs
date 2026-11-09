// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.7 (object streams), §7.5.8 (xref streams)
// PHASE: 3.32.0 (D-271) — an object stream whose Flate data is invalid is
//        treated as damaged: the file still opens, the objects stored in it
//        are missing, and Warnings says why.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class DamagedObjectStream3320Tests
{
    [Fact]
    public void OverSubscribedObjectStream_FileOpens_AndWarnsWhy()
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(BuildPdf(OverSubscribedFlate())), leaveOpen: false);

        doc.PageCount.Should().Be(1, "the page tree is not in the damaged object stream");
        doc.Objects.Resolve(new PdfReference(5, 0)).Should().BeOfType<PdfNull>("the object stored in the damaged stream is missing");

        doc.Warnings.Should().ContainSingle(w => w.Contains("Object stream 4 0 R", StringComparison.Ordinal));
        string warning = doc.Warnings.Single(w => w.Contains("Object stream 4 0 R", StringComparison.Ordinal));
        warning.Should().Contain("damaged").And.Contain("over-subscribed");
        doc.IsRecovered.Should().BeTrue();
    }

    [Fact]
    public void OverSubscribedObjectStream_IsReported_OnTheWarningChannel()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = PdfDocument.Open(
            new MemoryStream(BuildPdf(OverSubscribedFlate())), new PdfOpenOptions { OnWarning = warnings.Add });

        doc.Objects.Resolve(new PdfReference(5, 0));
        doc.Objects.Resolve(new PdfReference(5, 0));

        PdfWarning warning = warnings.Should().ContainSingle("one warning per damaged object stream").Subject;
        warning.Kind.Should().Be(PdfWarningKind.StreamDamaged);
        warning.Subject.Should().Be("object stream 4 0 R");
        warning.Reason.Should().Contain("over-subscribed");
    }

    [Fact]
    public void ValidObjectStream_GivesItsObject_AndNoWarning()
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(BuildPdf(null)), leaveOpen: false);

        PdfDictionary info = (PdfDictionary)doc.Objects.Resolve(new PdfReference(5, 0));
        ((PdfString)info[PdfName.Intern("Title")]).ToString().Should().Contain("T");
        doc.Warnings.Should().BeEmpty();
    }

    // Objects 1-3: catalog, pages, page. Object 4: an object stream holding
    // object 5 (a dictionary). Object 6: the xref stream (uncompressed).
    private static byte[] BuildPdf(byte[]? damagedData)
    {
        const string Body = "5 0 << /Title (T) >>";
        byte[] objStmData = damagedData ?? Deflate(Encoding.ASCII.GetBytes(Body));
        int first = "5 0 ".Length;

        MemoryStream file = new MemoryStream();
        long[] offsets = new long[7];
        void Text(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            file.Write(b, 0, b.Length);
        }

        Text("%PDF-1.5\n");
        offsets[1] = file.Position;
        Text("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = file.Position;
        Text("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        offsets[3] = file.Position;
        Text("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>\nendobj\n");
        offsets[4] = file.Position;
        Text($"4 0 obj\n<< /Type /ObjStm /N 1 /First {first} /Filter /FlateDecode /Length {objStmData.Length} >>\nstream\n");
        file.Write(objStmData, 0, objStmData.Length);
        Text("\nendstream\nendobj\n");
        offsets[6] = file.Position;

        List<byte> xref = new List<byte>();
        void Entry(int type, long field2, int field3)
        {
            xref.Add((byte)type);
            xref.Add((byte)(field2 >> 24));
            xref.Add((byte)(field2 >> 16));
            xref.Add((byte)(field2 >> 8));
            xref.Add((byte)field2);
            xref.Add((byte)(field3 >> 8));
            xref.Add((byte)field3);
        }

        Entry(0, 0, 65535);
        Entry(1, offsets[1], 0);
        Entry(1, offsets[2], 0);
        Entry(1, offsets[3], 0);
        Entry(1, offsets[4], 0);
        Entry(2, 4, 0);
        Entry(1, offsets[6], 0);

        Text($"6 0 obj\n<< /Type /XRef /Size 7 /W [1 4 2] /Root 1 0 R /Length {xref.Count} >>\nstream\n");
        file.Write(xref.ToArray(), 0, xref.Count);
        Text("\nendstream\nendobj\n");
        Text($"startxref\n{offsets[6]}\n%%EOF\n");
        return file.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using MemoryStream output = new MemoryStream();
        using (System.IO.Compression.ZLibStream z = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    // A zlib stream whose dynamic block has an over-subscribed code-length code.
    private static byte[] OverSubscribedFlate()
    {
        // BFINAL 1, BTYPE 10, HLIT 0, HDIST 0, HCLEN 0, then four 3-bit lengths of 1.
        return new byte[] { 0x78, 0x9C, 0x05, 0x00, 0x92, 0x04, 0, 0, 0, 0, 0, 0 };
    }
}
