// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-1 §6.7.3 (document information dictionary and XMP)
// PHASE: 3.34.0 (D-335) — PDF/A-1b: pdf:Keywords equals /Info's /Keywords.

using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.PdfA.Tests;

public sealed class XmpKeywords3340Tests
{
    [Fact]
    public void PdfA1B_TakesKeywordsFromInfo()
    {
        string xml = Xmp(PdfAConformance.PdfA1B, info =>
            info.Set(PdfName.Intern("Keywords"), new PdfString(Encoding.Latin1.GetBytes("discharge; ward 4 & ICU"))));

        xml.Should().Contain("<pdf:Keywords>discharge; ward 4 &amp; ICU</pdf:Keywords>");
    }

    [Fact]
    public void PdfA1B_WithoutKeywords_WritesNone()
    {
        string xml = Xmp(PdfAConformance.PdfA1B, info => info.Remove(PdfName.Intern("Keywords")));

        xml.Should().NotContain("pdf:Keywords");
    }

    [Fact]
    public void PdfA2B_IsUnchanged()
    {
        string xml = Xmp(PdfAConformance.PdfA2B, info =>
            info.Set(PdfName.Intern("Keywords"), new PdfString(Encoding.Latin1.GetBytes("ignored"))));

        xml.Should().NotContain("pdf:Keywords", "only PDF/A-1b takes /Info into the XMP (as with the 3.33.0 entries)");
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
