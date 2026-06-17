// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.7 — Object streams
//        PDF 32000-1:2008 §7.5.8 — Cross-reference streams
//        PDF 32000-1:2008 §7.6   — Encryption
//
// Coverage for XrefStyle.Stream output: object streams plus a compressed
// cross-reference stream. Verifies round-tripping (plain and encrypted),
// chunking of /ObjStm containers, that streams stay direct, that the file
// shrinks versus the classic table, and that the classic path is unchanged.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class PdfWriterObjectStreamTests
{
    // Builds catalog/pages/page/content plus <paramref name="fillerCount"/>
    // plain filler dictionaries (object numbers 5..). Each filler carries an
    // /Index integer and a /Note string so round-trips can be value-checked.
    private static List<PdfIndirectObject> BuildObjects(int fillerCount)
    {
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Intern("Catalog"));
        catalog.Set(PdfName.Intern("Pages"), new PdfReference(2, 0));
        objects.Add(new PdfIndirectObject(new PdfObjectId(1, 0), catalog));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Intern("Pages"));
        pages.Set(PdfName.Intern("Kids"), new PdfArray(new PdfPrimitive[] { new PdfReference(3, 0) }));
        pages.Set(PdfName.Intern("Count"), 1);
        objects.Add(new PdfIndirectObject(new PdfObjectId(2, 0), pages));

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Intern("Page"));
        page.Set(PdfName.Intern("Parent"), new PdfReference(2, 0));
        page.Set(PdfName.Intern("MediaBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));
        page.Set(PdfName.Intern("Contents"), new PdfReference(4, 0));
        objects.Add(new PdfIndirectObject(new PdfObjectId(3, 0), page));

        byte[] content = Encoding.ASCII.GetBytes("BT /F1 24 Tf 72 700 Td (Hello) Tj ET");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);
        objects.Add(new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(contentDict, content)));

        for (int i = 0; i < fillerCount; i++)
        {
            PdfDictionary filler = new PdfDictionary();
            filler.Set(PdfName.Intern("Index"), i);
            filler.Set(PdfName.Intern("Note"), new PdfString(Encoding.ASCII.GetBytes("filler-" + i)));
            objects.Add(new PdfIndirectObject(new PdfObjectId(5 + i, 0), filler));
        }

        return objects;
    }

    private static PdfDictionary BuildTrailer()
    {
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        return trailer;
    }

    private static byte[] Write(
        IEnumerable<PdfIndirectObject> objects,
        XrefStyle style,
        EncryptionOptions? encryption = null)
    {
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, BuildTrailer(), encryption, SynthesizedMetadata.All, style);
        return output.ToArray();
    }

    private static int Occurrences(byte[] bytes, string needle)
    {
        string text = Encoding.Latin1.GetString(bytes);
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    [Fact]
    public void StreamMode_RoundTripsAllObjects()
    {
        byte[] bytes = Write(BuildObjects(20), XrefStyle.Stream);

        using MemoryStream input = new MemoryStream(bytes, writable: false);
        using PdfReader reader = PdfReader.Open(input, leaveOpen: false);

        reader.Objects.ResolveById(new PdfObjectId(1, 0)).Should().BeOfType<PdfDictionary>();
        reader.Objects.ResolveById(new PdfObjectId(3, 0)).Should().BeOfType<PdfDictionary>();

        PdfPrimitive filler = reader.Objects.ResolveById(new PdfObjectId(10, 0));
        filler.Should().BeOfType<PdfDictionary>();
        ((PdfDictionary)filler).TryGetValue(PdfName.Intern("Note"), out PdfPrimitive? note).Should().BeTrue();
        note.Should().BeOfType<PdfString>();
        Encoding.ASCII.GetString(((PdfString)note!).Bytes).Should().Be("filler-5");
    }

    [Fact]
    public void StreamMode_EmitsXRefStreamAndObjStm_WithoutTrailerKeyword()
    {
        byte[] bytes = Write(BuildObjects(20), XrefStyle.Stream);
        string text = Encoding.Latin1.GetString(bytes);

        text.Should().Contain("/ObjStm");
        text.Should().Contain("/XRef");
        text.Should().NotContain("\ntrailer");
    }

    [Fact]
    public void StreamMode_KeepsStreamsDirect()
    {
        // The content stream (object 4) is a stream, so it must NOT be packed
        // into an object stream; it stays a direct object and resolves to the
        // original bytes.
        byte[] bytes = Write(BuildObjects(5), XrefStyle.Stream);

        using MemoryStream input = new MemoryStream(bytes, writable: false);
        using PdfReader reader = PdfReader.Open(input, leaveOpen: false);

        PdfPrimitive contents = reader.Objects.ResolveById(new PdfObjectId(4, 0));
        contents.Should().BeOfType<PdfStream>();
        Encoding.ASCII.GetString(((PdfStream)contents).RawBytes)
            .Should().Be("BT /F1 24 Tf 72 700 Td (Hello) Tj ET");
    }

    [Fact]
    public void StreamMode_ChunksObjectStreamsBeyondCap()
    {
        // 454 compressible objects exceed the 200-per-container cap, so the
        // writer must emit multiple /ObjStm containers (200 + 200 + 54).
        byte[] bytes = Write(BuildObjects(450), XrefStyle.Stream);
        Occurrences(bytes, "/ObjStm").Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void StreamMode_IsSmallerThanClassic()
    {
        List<PdfIndirectObject> objects = BuildObjects(40);
        byte[] classic = Write(objects, XrefStyle.Classic);
        byte[] stream = Write(BuildObjects(40), XrefStyle.Stream);

        stream.Length.Should().BeLessThan(classic.Length);
    }

    [Fact]
    public void ClassicMode_StillWritesTrailerAndXrefTable()
    {
        byte[] bytes = Write(BuildObjects(5), XrefStyle.Classic);
        string text = Encoding.Latin1.GetString(bytes);

        text.Should().Contain("\ntrailer");
        text.Should().Contain("\nxref");
        text.Should().NotContain("/ObjStm");
        text.Should().NotContain("/XRef");
    }

    [Fact]
    public void DefaultWrite_IsClassic()
    {
        // The 3-arg overload (no XrefStyle) must default to the classic table.
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, BuildObjects(5), BuildTrailer());
        string text = Encoding.Latin1.GetString(output.ToArray());

        text.Should().Contain("\ntrailer");
        text.Should().NotContain("/XRef");
    }

    [Fact]
    public void StreamMode_Encrypted_RoundTripsThroughObjectStream()
    {
        EncryptionOptions encryption = EncryptionOptions.Aes256("user-pw", "owner-pw");
        byte[] bytes = Write(BuildObjects(10), XrefStyle.Stream, encryption);

        string text = Encoding.Latin1.GetString(bytes);
        text.Should().Contain("/Encrypt");
        text.Should().Contain("/XRef");
        text.Should().Contain("/ObjStm");

        using MemoryStream input = new MemoryStream(bytes, writable: false);
        using PdfReader reader = PdfReader.Open(input, "user-pw", leaveOpen: false);

        reader.Objects.ResolveById(new PdfObjectId(1, 0)).Should().BeOfType<PdfDictionary>();

        // A packed (compressed) object must decrypt correctly: the container is
        // decrypted as a whole and its members are not individually encrypted.
        PdfPrimitive filler = reader.Objects.ResolveById(new PdfObjectId(8, 0));
        filler.Should().BeOfType<PdfDictionary>();
        ((PdfDictionary)filler).TryGetValue(PdfName.Intern("Note"), out PdfPrimitive? note).Should().BeTrue();
        Encoding.ASCII.GetString(((PdfString)note!).Bytes).Should().Be("filler-3");
    }
}
