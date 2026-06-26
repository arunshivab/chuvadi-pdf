// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.PdfA.Tests;

public sealed class PdfAWriterTests
{
    private static (byte[] Bytes, PdfAResult Result) WritePdfA(string baseFont, PdfAConformance conformance)
    {
        byte[] input = TestPdf.WithSimpleFont(baseFont);
        using PdfDocument document = PdfDocument.Open(new MemoryStream(input));
        PdfAOptions options = new PdfAOptions
        {
            Conformance = conformance,
            Title = "Conformance Test",
            Author = "Chuvadi",
        };
        using MemoryStream output = new MemoryStream();
        PdfAResult result = PdfAWriter.Write(output, document, options);
        return (output.ToArray(), result);
    }

    [Fact]
    public void Write_PdfA1B_ProducesConformingStructure()
    {
        (byte[] bytes, PdfAResult result) = WritePdfA("Helvetica", PdfAConformance.PdfA1B);

        result.Succeeded.Should().BeTrue();
        result.Violations.Should().BeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 8).Should().Be("%PDF-1.4");

        string body = Encoding.Latin1.GetString(bytes);
        body.Should().Contain("GTS_PDFA1");
        body.Should().Contain("DestOutputProfile");
        body.Should().Contain("FontFile2");
        body.Should().Contain("LiberationSans");
        body.Should().NotContain("/Type /XRef", "PDF/A-1b requires a classic cross-reference table");
    }

    [Fact]
    public void Write_PdfA2B_UsesHeaderVersion17AndPart2()
    {
        (byte[] bytes, PdfAResult result) = WritePdfA("Times-Roman", PdfAConformance.PdfA2B);

        result.Succeeded.Should().BeTrue();
        Encoding.ASCII.GetString(bytes, 0, 8).Should().Be("%PDF-1.7");
        Encoding.Latin1.GetString(bytes).Should().Contain("LiberationSerif");
    }

    [Fact]
    public void Write_EmbedsXmpWithPdfaId()
    {
        (byte[] bytes, _) = WritePdfA("Helvetica", PdfAConformance.PdfA1B);

        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(bytes));
        reopened.Catalog.ContainsKey(PdfName.Intern("OutputIntents")).Should().BeTrue();
        reopened.Catalog.ContainsKey(PdfName.Intern("Metadata")).Should().BeTrue();

        byte[]? xmp = reopened.XmpMetadata;
        xmp.Should().NotBeNull();
        string xml = Encoding.UTF8.GetString(xmp!);
        xml.Should().Contain("<pdfaid:part>1</pdfaid:part>");
        xml.Should().Contain("<pdfaid:conformance>B</pdfaid:conformance>");
    }

    [Fact]
    public void Write_NonEmbeddableFont_FailsAndWritesNothing()
    {
        (byte[] bytes, PdfAResult result) = WritePdfA("CustomMysteryFont", PdfAConformance.PdfA1B);

        result.Succeeded.Should().BeFalse();
        result.Violations.Should().ContainSingle().Which.Should().Contain("CustomMysteryFont");
        bytes.Should().BeEmpty();
    }
}
