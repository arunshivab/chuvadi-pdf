// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.4 (cross-reference table), §14.3 (metadata)
// Regression coverage for writer-output conformance found via Chuvadi Reader:
//   - xref entries were 21 bytes (a stray space before CRLF); the spec mandates
//     exactly 20, and Acrobat rebuilt the table on open -> spurious save prompt.
//   - every written file now carries /Info and XMP /Metadata.

using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class XrefAndMetadataTests
{
    [Fact]
    public void XrefEntries_AreExactlyTwentyBytes()
    {
        byte[] pdf = BuildMinimalPdf();
        string text = Encoding.Latin1.GetString(pdf);

        // The xref block must consist solely of 20-byte, CRLF-terminated entries
        // immediately followed by "trailer". A 21-byte entry (type, space, CRLF)
        // would not match this pattern.
        Match block = Regex.Match(
            text, "\nxref\n\\d+ \\d+\n((?:\\d{10} \\d{5} [nf]\r\n)+)trailer");

        block.Success.Should().BeTrue(
            "every xref entry must be exactly 20 bytes ending in CRLF (ISO 32000-1 §7.5.4)");
    }

    [Fact]
    public void Write_AddsInfoWithProducer()
    {
        byte[] pdf = BuildMinimalPdf();
        using MemoryStream ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        doc.Trailer.TryGetValue(PdfName.Intern("Info"), out PdfPrimitive? info).Should().BeTrue();
        info.Should().NotBeNull();

        PdfDictionary? infoDict = doc.Objects.ResolveAs<PdfDictionary>(info!);
        infoDict.Should().NotBeNull();
        infoDict!.ContainsKey(PdfName.Intern("Producer")).Should().BeTrue();
    }

    [Fact]
    public void Write_AddsXmpMetadata()
    {
        byte[] pdf = BuildMinimalPdf();
        Encoding.Latin1.GetString(pdf).Should().Contain("/Type /Metadata")
            .And.Contain("xmpMM:DocumentID");
    }

    private static byte[] BuildMinimalPdf()
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfArray kids = new PdfArray([]);
        kids.Add(new PdfReference(pageId));

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, kids);
        pagesDict.Set(PdfName.Count, 1);
        pagesDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(200), new PdfInteger(200)
        ]));

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));

        PdfIndirectObject[] objects =
        [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
        ];

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
