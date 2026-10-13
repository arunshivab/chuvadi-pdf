// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.4 — Cross-reference table
// PHASE: 3.18 — xref-offset recovery.
//
// A classic xref entry pointing at the wrong byte offset used to resolve the
// affected object to whatever happened to live at that offset. The loader now
// validates the object number parsed at the offset and, when it is provably
// wrong (a different object, or not an object at all), scans the file for the
// real definition. Healthy files keep the fast strict path: the check reuses
// the object number every load parses anyway.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class XrefOffsetRecoveryTests
{
    [Fact]
    public void WrongOffsetPointingAtAnotherObject_RecoversTheRealDefinition()
    {
        byte[] pdf = BuildPdf(corruptObject4OffsetTo: 5);
        using PdfReader reader = PdfReader.Open(new MemoryStream(pdf), leaveOpen: true);

        PdfPrimitive resolved = reader.Objects.ResolveById(new PdfObjectId(4, 0));

        resolved.Should().BeOfType<PdfStream>(
            "object 4 is the content stream, not the dictionary its stale offset points at");
        string content = Encoding.Latin1.GetString(((PdfStream)resolved).RawBytes);
        content.Should().Contain("RECOVERED-CONTENT");
    }

    [Fact]
    public void WrongOffsetPointingIntoGarbage_RecoversByScan()
    {
        byte[] pdf = BuildPdf(corruptObject4OffsetTo: -1);
        using PdfReader reader = PdfReader.Open(new MemoryStream(pdf), leaveOpen: true);

        PdfPrimitive resolved = reader.Objects.ResolveById(new PdfObjectId(4, 0));

        resolved.Should().BeOfType<PdfStream>();
        string content = Encoding.Latin1.GetString(((PdfStream)resolved).RawBytes);
        content.Should().Contain("RECOVERED-CONTENT");
    }

    [Fact]
    public void HealthyFile_ResolvesNormally()
    {
        byte[] pdf = BuildPdf(corruptObject4OffsetTo: 0);
        using PdfReader reader = PdfReader.Open(new MemoryStream(pdf), leaveOpen: true);

        PdfPrimitive resolved = reader.Objects.ResolveById(new PdfObjectId(4, 0));

        resolved.Should().BeOfType<PdfStream>();
    }

    // Builds a five-object PDF. corruptObject4OffsetTo: 0 = healthy; N>0 =
    // object 4's xref entry points at object N's definition; -1 = object 4's
    // xref entry points into the middle of the content bytes (garbage).
    private static byte[] BuildPdf(int corruptObject4OffsetTo)
    {
        byte[] content = Encoding.Latin1.GetBytes("% RECOVERED-CONTENT\n");
        List<byte[]> bodies = new List<byte[]>
        {
            Encoding.Latin1.GetBytes("<</Type/Catalog/Pages 2 0 R>>"),
            Encoding.Latin1.GetBytes("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Encoding.Latin1.GetBytes(
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Resources<<>>/Contents 4 0 R>>"),
            Concat(
                Encoding.Latin1.GetBytes("<</Length " + content.Length + ">>\nstream\n"),
                content,
                Encoding.Latin1.GetBytes("\nendstream")),
            Encoding.Latin1.GetBytes("<</Type/Annot/Subtype/Text/Rect[0 0 1 1]>>"),
        };

        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));

        Write("%PDF-1.7\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            Write((i + 1) + " 0 obj ");
            ms.Write(bodies[i]);
            Write(" endobj\n");
        }

        // Corrupt object 4's xref offset per the scenario.
        long object4Offset = offsets[3];
        if (corruptObject4OffsetTo > 0)
        {
            object4Offset = offsets[corruptObject4OffsetTo - 1];
        }
        else if (corruptObject4OffsetTo < 0)
        {
            object4Offset = offsets[3] + 30; // middle of the stream dict/body
        }

        long xref = ms.Length;
        Write("xref\n0 " + (bodies.Count + 1) + "\n0000000000 65535 f \n");
        for (int i = 0; i < offsets.Count; i++)
        {
            long value = i == 3 ? object4Offset : offsets[i];
            Write(value.ToString("D10") + " 00000 n \n");
        }

        Write("trailer <</Size " + (bodies.Count + 1) + "/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF");
        return ms.ToArray();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        using MemoryStream ms = new MemoryStream();
        foreach (byte[] part in parts)
        {
            ms.Write(part);
        }

        return ms.ToArray();
    }
}
