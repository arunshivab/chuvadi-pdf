// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 19005-1 §6.7.3 (document information dictionary and XMP)
// PHASE: 3.33.0 (ticket 49, D-325) — PDF/A-1b: dc:title, dc:creator and
//        dc:description equal /Info's /Title, /Author and /Subject; the
//        options' Title and Author where /Info has none.

using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.PdfA.Tests;

public sealed class XmpDublinCore3330Tests
{
    [Fact]
    public void PdfA1B_TakesTitleAuthorAndSubjectFromInfo()
    {
        string xml = Xmp(PdfAConformance.PdfA1B, null, info =>
        {
            info.Set(PdfName.Intern("Title"), Text("Discharge summary"));
            info.Set(PdfName.Intern("Author"), Text("Dr. A. Shiva"));
            info.Set(PdfName.Intern("Subject"), Text("Ward 4 & ICU"));
        });

        xml.Should().Contain("<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">Discharge summary</rdf:li></rdf:Alt></dc:title>");
        xml.Should().Contain("<rdf:li>Dr. A. Shiva</rdf:li>");
        xml.Should().Contain("<dc:description><rdf:Alt><rdf:li xml:lang=\"x-default\">Ward 4 &amp; ICU</rdf:li></rdf:Alt></dc:description>");
    }

    [Fact]
    public void PdfA1B_UnicodeInfoTitle_IsWrittenAsUtf8()
    {
        string xml = Xmp(PdfAConformance.PdfA1B, null, info =>
            info.Set(PdfName.Intern("Title"), Utf16("சுவடி அறிக்கை")));

        xml.Should().Contain(">சுவடி அறிக்கை</rdf:li>");
    }

    [Fact]
    public void PdfA1B_InfoWins_OverTheOptions()
    {
        string xml = Xmp(PdfAConformance.PdfA1B, new PdfAOptions { Conformance = PdfAConformance.PdfA1B, Title = "From options", Author = "Options author" }, info =>
        {
            info.Set(PdfName.Intern("Title"), Text("From info"));
            info.Set(PdfName.Intern("Author"), Text("Info author"));
        });

        xml.Should().Contain(">From info</rdf:li>");
        xml.Should().Contain("<rdf:li>Info author</rdf:li>");
        xml.Should().NotContain("From options");
        xml.Should().NotContain("Options author");
    }

    [Fact]
    public void PdfA1B_InfoWithoutTheEntries_UsesTheOptions_AndNoDescription()
    {
        string xml = Xmp(PdfAConformance.PdfA1B, new PdfAOptions { Conformance = PdfAConformance.PdfA1B, Title = "From options", Author = "Options author" }, info =>
        {
            info.Remove(PdfName.Intern("Title"));
            info.Remove(PdfName.Intern("Author"));
            info.Remove(PdfName.Intern("Subject"));
        });

        xml.Should().Contain(">From options</rdf:li>");
        xml.Should().Contain("<rdf:li>Options author</rdf:li>");
        xml.Should().NotContain("dc:description");
    }

    [Fact]
    public void PdfA1B_AControlCharacter_LeavesTheEntryOut()
    {
        string xml = Xmp(PdfAConformance.PdfA1B, null, info =>
        {
            info.Set(PdfName.Intern("Title"), Text("Bad\u0001title"));
            info.Set(PdfName.Intern("Subject"), Text("Fine subject"));
        });

        xml.Should().NotContain("Bad");
        xml.Should().NotContain("dc:title");
        xml.Should().Contain(">Fine subject</rdf:li>");
    }

    [Fact]
    public void PdfA2B_IsUnchanged()
    {
        string xml = Xmp(PdfAConformance.PdfA2B, null, info =>
        {
            info.Set(PdfName.Intern("Title"), Text("From info"));
            info.Set(PdfName.Intern("Subject"), Text("A subject"));
        });

        xml.Should().NotContain("From info");
        xml.Should().NotContain("dc:description");
    }

    private static PdfString Text(string ascii) => new PdfString(Encoding.Latin1.GetBytes(ascii));

    private static PdfString Utf16(string text)
    {
        byte[] body = Encoding.BigEndianUnicode.GetBytes(text);
        byte[] bytes = new byte[body.Length + 2];
        bytes[0] = 0xFE;
        bytes[1] = 0xFF;
        body.CopyTo(bytes, 2);
        return new PdfString(bytes);
    }

    private static string Xmp(PdfAConformance conformance, PdfAOptions? options, System.Action<PdfDictionary> editInfo)
    {
        using PdfDocument document = PdfDocument.Open(new MemoryStream(TestPdf.WithSimpleFont("Helvetica")));
        PdfDictionary info = (PdfDictionary)document.Objects.Resolve(document.Trailer[PdfName.Info]);
        editInfo(info);

        using MemoryStream output = new MemoryStream();
        PdfAWriter.Write(output, document, options ?? new PdfAOptions { Conformance = conformance }).Succeeded.Should().BeTrue();
        using PdfDocument reopened = PdfDocument.Open(new MemoryStream(output.ToArray()));
        return Encoding.UTF8.GetString(reopened.XmpMetadata!);
    }
}
