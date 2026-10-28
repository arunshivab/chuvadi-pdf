// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.7 — Object streams (an object stream shall not
//        itself be stored in an object stream)
// PHASE: 3.24 — found by mutation fuzzing: two object streams each listed as
//        stored inside the other made object loading recurse until the
//        process died.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class ObjectStreamLoopTests
{
    [Fact]
    public void ObjectStreamsStoredInEachOther_AreTreatedAsMissing()
    {
        // Objects 1-3 are classic; 5 and 6 are listed as compressed in object
        // streams 6 and 5 respectively; the page's contents point at 5.
        List<string> classic = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 100 100]/Contents 5 0 R>>",
        };
        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < classic.Count; i++)
        {
            offsets.Add(ms.Length);
            Write((i + 1) + " 0 obj\n" + classic[i] + "\nendobj\n");
        }

        long xrefOffset = ms.Length;
        List<byte> rows = new List<byte>();
        void Row(byte type, long field2, int field3)
        {
            rows.Add(type);
            rows.Add((byte)(field2 >> 24));
            rows.Add((byte)(field2 >> 16));
            rows.Add((byte)(field2 >> 8));
            rows.Add((byte)field2);
            rows.Add((byte)(field3 >> 8));
            rows.Add((byte)field3);
        }

        Row(0, 0, 65535);
        foreach (long off in offsets)
        {
            Row(1, off, 0);
        }

        Row(1, xrefOffset, 0); // object 4: this cross-reference stream
        Row(2, 6, 0);          // object 5: in object stream 6
        Row(2, 5, 0);          // object 6: in object stream 5
        Write("4 0 obj\n<</Type/XRef/Size 7/W[1 4 2]/Root 1 0 R/Length " + rows.Count + ">>\nstream\n");
        ms.Write(rows.ToArray());
        Write("\nendstream\nendobj\nstartxref\n" + xrefOffset + "\n%%EOF\n");

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(ms.ToArray()), new PdfOpenOptions());

        doc.Objects.ResolveById(new Chuvadi.Pdf.Primitives.PdfObjectId(5, 0)).Should().BeOfType<Chuvadi.Pdf.Primitives.PdfNull>();
        doc.Pages[0].Width.Should().Be(100);
    }
}
