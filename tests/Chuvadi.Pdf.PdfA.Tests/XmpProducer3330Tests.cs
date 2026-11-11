// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-1 §6.7.3 (document information dictionary and XMP)
// PHASE: 3.33.0 (D-318) — PDF/A-1b: the XMP Producer and CreatorTool equal
//        /Info's /Producer and /Creator; "Chuvadi PDF/A" when /Info has none.

using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.PdfA.Tests;

public sealed class XmpProducer3330Tests
{
    [Fact]
    public void PdfA1B_TakesProducerAndCreatorFromInfo()
    {
        string xml = Xmp(PdfAConformance.PdfA1B, info =>
        {
            info.Set(PdfName.Intern("Producer"), new PdfString(Encoding.ASCII.GetBytes("Some Producer")));
            info.Set(PdfName.Intern("Creator"), new PdfString(Encoding.ASCII.GetBytes("Some Creator")));
        });

        xml.Should().Contain("<pdf:Producer>Some Producer</pdf:Producer>");
        xml.Should().Contain("<xmp:CreatorTool>Some Creator</xmp:CreatorTool>");
    }

    [Fact]
    public void PdfA1B_InfoWithoutProducerOrCreator_UsesChuvadiPdfA()
    {
        string xml = Xmp(PdfAConformance.PdfA1B, info =>
        {
            info.Remove(PdfName.Intern("Producer"));
            info.Remove(PdfName.Intern("Creator"));
        });

        xml.Should().Contain("<pdf:Producer>Chuvadi PDF/A</pdf:Producer>");
        xml.Should().Contain("<xmp:CreatorTool>Chuvadi PDF/A</xmp:CreatorTool>");
    }

    [Fact]
    public void PdfA2B_IsUnchanged()
    {
        string xml = Xmp(PdfAConformance.PdfA2B, info =>
            info.Set(PdfName.Intern("Producer"), new PdfString(Encoding.ASCII.GetBytes("Some Producer"))));

        xml.Should().Contain("<pdf:Producer>Chuvadi PDF/A</pdf:Producer>");
        xml.Should().Contain("<xmp:CreatorTool>Chuvadi PDF/A</xmp:CreatorTool>");
    }

    private static string Xmp(PdfAConformance conformance, System.Action<PdfDictionary> editInfo)
    {
        using PdfDocument document = PdfDocument.Open(new MemoryStream(TestPdf.WithSimpleFont("Helvetica")));
        PdfDictionary info = (PdfDictionary)document.Objects.Resolve(document.Trailer[PdfName.Info]);
        editInfo(info);

        using MemoryStream output = new MemoryStream();
        PdfAWriter.Write(output, document, new PdfAOptions { Conformance = conformance }).Succeeded.Should().BeTrue();
        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(output.ToArray()));
        return Encoding.UTF8.GetString(reopened.XmpMetadata!);
    }
}
