// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.2 (document catalog)
// PHASE: 3.35.0 (backlog 59, D-385) — catalog entries whose objects are not
//        written are dropped from a redacted file and reported; D-396/D-397:
//        document JavaScript, named destinations and the open action are kept.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Redaction;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class RedactionCatalog3350Tests
{
    [Fact]
    public void UnwrittenFormParts_AreDroppedAndReported_ScriptsAndActionsKept()
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int p1 = pdf.Reserve();
        int widget = pdf.Add("<</Type/Annot/Subtype/Widget/FT/Tx/T(name)/V(Ann)/Rect[20 150 110 170]/P " + N(p1) + " 0 R/F 4>>");
        int hidden = pdf.Add("<</FT/Tx/T(secret)/V(123-45-6789)>>");
        int script = pdf.Add("<</S/JavaScript/JS(app.alert('x'))>>");
        int names = pdf.Add("<</JavaScript<</Names[(a) " + N(script) + " 0 R]>>>>");
        int xfa = pdf.AddStream("<<>>", "<xdp:xdp><data>123-45-6789</data></xdp:xdp>");
        int form = pdf.Add("<</Fields[" + N(widget) + " 0 R " + N(hidden) + " 0 R]/XFA " + N(xfa) + " 0 R>>");
        int content = pdf.AddStream("<<>>", "0 0 1 rg 10 10 40 40 re f");
        pdf.Set(p1, "<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Contents " + N(content) + " 0 R/Annots[" + N(widget) + " 0 R]>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(p1) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R/Names " + N(names) + " 0 R/OpenAction " + N(script) + " 0 R/AcroForm " + N(form) + " 0 R/PageMode/UseNone>>");
        using PdfDocument doc = TestPdf.Open(pdf.Build(catalog));
        using MemoryStream output = new MemoryStream();

        RedactionReport report = Redactor.ApplyWithReport(output, doc, new RedactionOptions
        {
            Rectangles = new List<RedactionRect> { new RedactionRect(0, new RectangleF(0, 0, 60, 60)) },
        });

        report.CatalogEntriesRemoved.Should().Contain("AcroForm/XFA");
        report.CatalogEntriesRemoved.Should().Contain("AcroForm/Fields " + N(hidden) + " 0 R");
        report.CatalogEntriesRemoved.Should().NotContain(new[] { "Names", "OpenAction" });

        using PdfDocument redacted = TestPdf.Open(output.ToArray());
        PdfDictionary root = (PdfDictionary)redacted.Objects.Resolve(redacted.Trailer[PdfName.Root])!;
        root.ContainsKey(PdfName.Intern("Names")).Should().BeTrue("document JavaScript is kept (3.35.0, D-397)");
        root.ContainsKey(PdfName.Intern("OpenAction")).Should().BeTrue();
        root.ContainsKey(PdfName.Intern("PageMode")).Should().BeTrue("entries with no references are kept");
        PdfDictionary acroForm = (PdfDictionary)redacted.Objects.Resolve(root[PdfName.Intern("AcroForm")])!;
        acroForm.ContainsKey(PdfName.Intern("XFA")).Should().BeFalse();
        ((PdfArray)redacted.Objects.Resolve(acroForm[PdfName.Intern("Fields")])!).Count.Should().Be(1, "the field on the page is kept");
    }

    [Fact]
    public void ACleanCatalog_ReportsNothing()
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int content = pdf.AddStream("<<>>", "10 10 40 40 re f");
        int p1 = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Contents " + N(content) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(p1) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        using PdfDocument doc = TestPdf.Open(pdf.Build(catalog));
        using MemoryStream output = new MemoryStream();

        RedactionReport report = Redactor.ApplyWithReport(output, doc, new RedactionOptions
        {
            Rectangles = new List<RedactionRect> { new RedactionRect(0, new RectangleF(0, 0, 60, 60)) },
        });

        report.CatalogEntriesRemoved.Should().BeEmpty();
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
