// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 (outline), §14.3.2 (metadata), §14.3.3 (/Info)
// PHASE: 3.34.0 (#18) — redaction scrubs bookmarks and XMP; /Info stays out.

using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Redaction;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class RedactDocumentText3340Tests
{
    private const string Secret = "123-45-6789";

    [Fact]
    public void DocumentWidePattern_ScrubsBookmarksAndXmp_AndInfoIsLeftOut()
    {
        using PdfDocument doc = TestPdf.Open(Document());
        _ = doc.Info; // loaded, so its object is in the store
        using MemoryStream output = new MemoryStream();

        RedactionReport report = Redactor.ApplyWithReport(output, doc, new RedactionOptions
        {
            Patterns = { new PatternRule(@"\d{3}-\d{2}-\d{4}") },
        });

        report.DocumentMatchesRemoved.Should().Be(2, "one in the bookmark title, one in the XMP");
        Encoding.Latin1.GetString(output.ToArray()).Should().NotContain(Secret);

        output.Position = 0;
        using PdfDocument redacted = PdfDocument.Open(output, leaveOpen: true);
        (redacted.Info?.ContainsKey(PdfName.Intern("Title")) ?? false).Should().BeFalse("the source /Info is not carried");
        redacted.Catalog.TryGetValue(PdfName.Intern("Metadata"), out PdfPrimitive? metadata).Should().BeTrue();
        string xmp = Encoding.UTF8.GetString(((PdfStream)redacted.Objects.Resolve(metadata!)).RawBytes);
        xmp.Should().Contain("<dc:title>").And.Contain("Report for ");
    }

    [Fact]
    public void PagePattern_LeavesDocumentText()
    {
        using PdfDocument doc = TestPdf.Open(Document());
        using MemoryStream output = new MemoryStream();

        RedactionReport report = Redactor.ApplyWithReport(output, doc, new RedactionOptions
        {
            Patterns = { new PatternRule(@"\d{3}-\d{2}-\d{4}", pageIndices: new[] { 0 }) },
        });

        report.DocumentMatchesRemoved.Should().Be(0, "a pattern for some pages does not apply to the bookmarks or the metadata");
    }

    // A catalog entry nothing had loaded is written with the file, not named
    // and left out (Word-made files carry /ViewerPreferences this way).
    [Fact]
    public void ViewerPreferences_AreWritten()
    {
        using PdfDocument doc = TestPdf.Open(Document());
        using MemoryStream output = new MemoryStream();
        Redactor.Apply(output, doc, new RedactionOptions
        {
            Patterns = { new PatternRule(@"\d{3}-\d{2}-\d{4}") },
        });

        output.Position = 0;
        using PdfDocument redacted = PdfDocument.Open(output, leaveOpen: true);
        PdfDictionary? prefs = redacted.Objects.ResolveAs<PdfDictionary>(redacted.Catalog[PdfName.Intern("ViewerPreferences")]);
        prefs.Should().NotBeNull();
        prefs!.GetBoolean(PdfName.Intern("DisplayDocTitle")).Should().BeTrue();
    }

    private static byte[] Document()
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int outlines = pdf.Reserve();
        int item = pdf.Reserve();
        string packet = "<?xpacket begin=\"\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
            + "<rdf:Description xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>Report for " + Secret + "</dc:title></rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>";
        int metadata = pdf.AddStream("<</Type/Metadata/Subtype/XML>>", Encoding.UTF8.GetBytes(packet));
        int contents = pdf.AddStream("<<>>", "BT /F1 12 Tf 20 100 Td (SSN " + Secret + ") Tj ET");
        int page = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources<</Font<</F1<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>>>>>/Contents " + N(contents) + " 0 R>>");
        pdf.Set(item, "<</Title(Patient " + Secret + ")/Parent " + N(outlines) + " 0 R/Dest[" + N(page) + " 0 R/Fit]>>");
        pdf.Set(outlines, "<</Type/Outlines/First " + N(item) + " 0 R/Last " + N(item) + " 0 R/Count 1>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page) + " 0 R]/Count 1>>");
        int prefs = pdf.Add("<</DisplayDocTitle true>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R/Outlines " + N(outlines) + " 0 R/Metadata " + N(metadata) + " 0 R/ViewerPreferences " + N(prefs) + " 0 R>>");
        int info = pdf.Add("<</Title(Record " + Secret + ")>>");
        string text = Encoding.Latin1.GetString(pdf.Build(catalog));
        return Encoding.Latin1.GetBytes(text.Replace("/Root " + N(catalog) + " 0 R", "/Root " + N(catalog) + " 0 R/Info " + N(info) + " 0 R", System.StringComparison.Ordinal));
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
