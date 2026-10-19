// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.2.2 (whitespace), §7.3.10 (references to
//        undefined objects resolve to null), §7.5.4 (cross-reference table)
// PHASE: 3.21 — xref recovery: whitespace-tolerant definition scan; a stale
//        offset never resolves to a different live object; repairs are
//        reported through Warnings.

using System.IO;
using System.Text;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class XrefRecoveryToleranceTests
{
    private const string Content = "% RECOVERED-CONTENT\n";

    [Theory]
    [InlineData("4  0 obj")]
    [InlineData("4 0\nobj")]
    [InlineData("4\r\n0\r\nobj")]
    [InlineData("4\t0 obj")]
    [InlineData("4 00 obj")]
    public void UnusualHeaderWhitespace_IsFoundByTheRecoveryScan(string header)
    {
        byte[] pdf = Build(object4Header: header, object4OffsetPointsAt: 5);
        using PdfReader reader = PdfReader.Open(new MemoryStream(pdf), leaveOpen: true);

        PdfPrimitive resolved = reader.Objects.ResolveById(new PdfObjectId(4, 0));

        resolved.Should().BeOfType<PdfStream>();
        Encoding.Latin1.GetString(((PdfStream)resolved).RawBytes).Should().Contain("RECOVERED-CONTENT");
        reader.Warnings.Should().ContainSingle(w => w.Contains("recovered by scanning"));
    }

    [Fact]
    public void NoDefinitionAnywhere_AndTheOffsetHoldsALiveObject_ResolvesToNull()
    {
        // Object 4 is not defined at all; its offset points at object 5,
        // which the table also lists. Object 5 must NOT be handed back as 4.
        byte[] pdf = Build(object4Header: null, object4OffsetPointsAt: 5);
        using PdfReader reader = PdfReader.Open(new MemoryStream(pdf), leaveOpen: true);

        PdfPrimitive resolved = reader.Objects.ResolveById(new PdfObjectId(4, 0));

        resolved.Should().BeOfType<PdfNull>("a reference to an undefined object is null (§7.3.10)");
        reader.Warnings.Should().ContainSingle(w => w.Contains("treated as missing"));
        reader.Objects.ResolveById(new PdfObjectId(5, 0)).Should().BeOfType<PdfDictionary>(
            "object 5 itself still resolves normally");
    }

    [Fact]
    public void MisnumberedHeader_ThatNoOtherEntryUses_IsReadAsTheRequestedObject()
    {
        // Object 4's bytes sit at its offset, but the writer labelled them
        // "9 0 obj" — and the table has no object 9.
        byte[] pdf = Build(object4Header: "9 0 obj", object4OffsetPointsAt: 4);
        using PdfReader reader = PdfReader.Open(new MemoryStream(pdf), leaveOpen: true);

        PdfPrimitive resolved = reader.Objects.ResolveById(new PdfObjectId(4, 0));

        resolved.Should().BeOfType<PdfStream>();
        Encoding.Latin1.GetString(((PdfStream)resolved).RawBytes).Should().Contain("RECOVERED-CONTENT");
        reader.Warnings.Should().ContainSingle(w => w.Contains("which no other cross-reference entry uses"));
    }

    [Fact]
    public void MisnumberedHeader_ThatAnotherEntryUses_IsNotBorrowed()
    {
        // Object 4's bytes are labelled "5 0 obj" — but object 5 is live in
        // the table, so the bytes cannot be trusted as object 4.
        byte[] pdf = Build(object4Header: "5 0 obj", object4OffsetPointsAt: 4);
        using PdfReader reader = PdfReader.Open(new MemoryStream(pdf), leaveOpen: true);

        reader.Objects.ResolveById(new PdfObjectId(4, 0)).Should().BeOfType<PdfNull>();
        reader.Warnings.Should().ContainSingle(w => w.Contains("treated as missing"));
    }

    [Fact]
    public void Warnings_GrowWhenADamagedObjectIsFirstLoaded()
    {
        byte[] pdf = Build(object4Header: "4  0 obj", object4OffsetPointsAt: 5);
        using PdfReader reader = PdfReader.Open(new MemoryStream(pdf), leaveOpen: true);
        reader.Warnings.Should().BeEmpty("object 4 has not been loaded yet");

        _ = reader.Objects.ResolveById(new PdfObjectId(4, 0));

        reader.Warnings.Should().HaveCount(1);
    }

    [Fact]
    public void HealthyFile_HasNoWarnings()
    {
        byte[] pdf = Build(object4Header: "4 0 obj", object4OffsetPointsAt: 4);
        using PdfReader reader = PdfReader.Open(new MemoryStream(pdf), leaveOpen: true);

        for (int i = 1; i <= 5; i++)
        {
            _ = reader.Objects.ResolveById(new PdfObjectId(i, 0));
        }

        reader.Warnings.Should().BeEmpty();
    }

    // Five objects: catalog, pages, page, object 4 (content stream, written
    // with object4Header, or omitted entirely when null), object 5. Object 4's
    // xref entry points at the definition of object number
    // object4OffsetPointsAt (4 = its own bytes, 5 = object 5's bytes).
    private static byte[] Build(string? object4Header, int object4OffsetPointsAt)
    {
        string stream4 = "<</Length " + Content.Length + ">>\nstream\n" + Content + "\nendstream";
        string object5 = "<</Type/Annot/Subtype/Text/Rect[0 0 1 1]>>";

        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");

        long[] offsets = new long[6];
        string[] bodies =
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Resources<<>>/Contents 4 0 R>>",
        };
        for (int i = 0; i < bodies.Length; i++)
        {
            offsets[i + 1] = ms.Length;
            Write((i + 1) + " 0 obj " + bodies[i] + " endobj\n");
        }

        if (object4Header is not null)
        {
            offsets[4] = ms.Length;
            Write(object4Header + " " + stream4 + " endobj\n");
        }

        offsets[5] = ms.Length;
        Write("5 0 obj " + object5 + " endobj\n");

        long object4Offset = object4OffsetPointsAt == 5 || object4Header is null ? offsets[5] : offsets[4];

        long xref = ms.Length;
        Write("xref\n0 6\n0000000000 65535 f \n");
        for (int i = 1; i <= 5; i++)
        {
            long value = i == 4 ? object4Offset : offsets[i];
            Write(value.ToString("D10") + " 00000 n \n");
        }

        Write("trailer <</Size 6/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF");
        return ms.ToArray();
    }
}
