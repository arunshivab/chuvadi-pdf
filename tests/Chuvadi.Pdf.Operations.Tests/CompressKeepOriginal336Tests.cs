// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 (file structure)
// PHASE: 3.36.0 (D-410 D, item 9) — compression never makes a file bigger: a
//        rewrite that comes out no smaller is dropped and the original bytes
//        are written unchanged, and the result reports the honest reduction
//        (zero when the original is kept) against the real output size.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class CompressKeepOriginal336Tests
{
    [Fact]
    public void RewriteNotSmaller_KeepsTheOriginalBytes()
    {
        // A tiny document: the object-stream rewrite carries more structural
        // overhead than it saves, so it comes out no smaller and is dropped.
        byte[] input = OnePage("BT /F1 12 Tf 72 700 Td (hi) Tj ET");

        (CompressionResult result, byte[] written) = Compress(input);

        result.KeptOriginal.Should().BeTrue();
        result.OriginalBytes.Should().Be(input.Length);
        result.CompressedBytes.Should().Be(input.Length);
        result.PercentReduced.Should().Be(0.0);
        written.Should().Equal(input);
        result.ObjectsRemoved.Should().Be(0);
    }

    [Fact]
    public void RewriteSmaller_ReportsHonestReduction()
    {
        // A large, highly compressible content stream: the rewrite Flate-packs
        // it and comes out smaller, so it is written and the reduction is real.
        StringBuilder content = new StringBuilder();
        for (int i = 0; i < 2000; i++)
        {
            content.Append("0 0 100 100 re f\n");
        }

        byte[] input = OnePage(content.ToString());

        (CompressionResult result, byte[] written) = Compress(input);

        result.KeptOriginal.Should().BeFalse();
        result.CompressedBytes.Should().BeLessThan(result.OriginalBytes);
        result.PercentReduced.Should().BeGreaterThan(0.0);
        written.Length.Should().Be((int)result.CompressedBytes);
    }

    private static (CompressionResult Result, byte[] Written) Compress(byte[] input)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(input), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        CompressionResult result = PdfCompressor.Compress(doc, output, new CompressionOptions());
        return (result, output.ToArray());
    }

    private static byte[] OnePage(string content)
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>"),
            Stream(content),
            Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"),
        };
        return Build(bodies);
    }

    private static byte[] Obj(string body) => Encoding.Latin1.GetBytes(body);

    private static byte[] Stream(string content)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(content);
        using MemoryStream ms = new MemoryStream();
        ms.Write(Encoding.Latin1.GetBytes(
            "<</Length " + bytes.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"));
        ms.Write(bytes);
        ms.Write(Encoding.Latin1.GetBytes("\nendstream"));
        return ms.ToArray();
    }

    private static byte[] Build(List<byte[]> bodies)
    {
        using MemoryStream ms = new MemoryStream();
        ms.Write(Encoding.Latin1.GetBytes("%PDF-1.7\n"));
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            ms.Write(Encoding.Latin1.GetBytes((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n"));
            ms.Write(bodies[i]);
            ms.Write(Encoding.Latin1.GetBytes("\nendobj\n"));
        }

        long xref = ms.Length;
        ms.Write(Encoding.Latin1.GetBytes(
            "xref\n0 " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n"));
        foreach (long off in offsets)
        {
            ms.Write(Encoding.Latin1.GetBytes(off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n"));
        }

        ms.Write(Encoding.Latin1.GetBytes(
            "trailer <</Size " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture)
            + "/Root 1 0 R>>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF"));
        return ms.ToArray();
    }
}
