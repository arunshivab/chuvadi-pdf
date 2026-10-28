// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.24 — embedding API (A2): PdfSanitizer disarms active content.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class PdfSanitizerTests
{
    [Fact]
    public void Sanitize_RemovesScriptsActionsAndEmbeddedFiles()
    {
        using PdfDocument doc = Open(ArmedPdf());
        using MemoryStream output = new MemoryStream();

        SanitizeReport report = PdfSanitizer.Sanitize(doc, output);

        string text = Encoding.Latin1.GetString(output.ToArray());
        foreach (string marker in new[] { "/JavaScript", "/Launch", "/EmbeddedFiles", "/XFA", "/AA", "/OpenAction",
            "/SubmitForm", "/FileAttachment", "/RichMedia", "/URI", "/GoToR", "QZXSCRIPT", "QZXFILE", "/EF" })
        {
            text.Should().NotContain(marker);
        }

        text.Should().Contain("/GoTo", "in-document links stay");
        text.Should().Contain("/NextPage");
        report.FoundActiveContent.Should().BeTrue();
        report.XfaRemoved.Should().BeTrue();
        report.JavaScriptTreesRemoved.Should().Be(1);
        report.AnnotationsRemoved.Should().Be(2);

        output.Position = 0;
        using PdfDocument clean = PdfDocument.Open(output, leaveOpen: true);
        clean.PageCount.Should().Be(1);
        PdfSanitizer.Inspect(clean).FoundActiveContent.Should().BeFalse();
    }

    [Fact]
    public void Inspect_ReportsWithoutChangingTheDocument()
    {
        byte[] input = ArmedPdf();
        using PdfDocument doc = Open(input);

        SanitizeReport report = PdfSanitizer.Inspect(doc);

        report.FoundActiveContent.Should().BeTrue();
        report.ActionsRemoved.Should().BeGreaterThanOrEqualTo(4);
        using MemoryStream output = new MemoryStream();
        PdfSanitizer.Sanitize(doc, output).ActionsRemoved.Should().Be(report.ActionsRemoved, "inspecting changed nothing");
    }

    [Fact]
    public void KeepUriLinks_KeepsWebLinksOnly()
    {
        using PdfDocument doc = Open(ArmedPdf());
        using MemoryStream output = new MemoryStream();

        PdfSanitizer.Sanitize(doc, output, new SanitizeOptions { KeepUriLinks = true });

        string text = Encoding.Latin1.GetString(output.ToArray());
        text.Should().Contain("/URI");
        text.Should().NotContain("/Launch");
    }

    [Fact]
    public void ACleanDocument_ReportsNothing()
    {
        using PdfDocument doc = Open(Build(new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>",
        }));

        PdfSanitizer.Inspect(doc).FoundActiveContent.Should().BeFalse();
    }

    [Fact]
    public void ParentActionsAndStrayEmbeddedFiles_AreRemoved_AndInspectAgrees()
    {
        byte[] input = Build(new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R/Private 5 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Annots[4 0 R]>>",
            "<</Type/Annot/Subtype/Link/Rect[0 0 10 10]/PA<</S/JavaScript/JS(QZXSCRIPT)>>>>",
            "<</Type/EmbeddedFile/Length 7>>\nstream\nQZXFILE\nendstream",
        });
        using PdfDocument doc = Open(input);
        SanitizeReport inspected = PdfSanitizer.Inspect(doc);
        using MemoryStream output = new MemoryStream();

        SanitizeReport removed = PdfSanitizer.Sanitize(doc, output);

        string text = Encoding.Latin1.GetString(output.ToArray());
        text.Should().NotContain("QZXSCRIPT");
        text.Should().NotContain("QZXFILE");
        inspected.ActionsRemoved.Should().Be(removed.ActionsRemoved).And.Be(1);
        inspected.EmbeddedFilesRemoved.Should().Be(removed.EmbeddedFilesRemoved).And.Be(1);
    }

    [Fact]
    public void InspectAndSanitize_CountTheSame()
    {
        using PdfDocument doc = Open(ArmedPdf());
        SanitizeReport inspected = PdfSanitizer.Inspect(doc);
        using MemoryStream output = new MemoryStream();

        SanitizeReport removed = PdfSanitizer.Sanitize(doc, output);

        inspected.Should().BeEquivalentTo(removed);
    }

    [Fact]
    public void ALongReferenceChain_DoesNotOverflowTheStack()
    {
        // 50,000 outline items linked by /Next, each its own object: the
        // reachability walk followed them recursively until the process died.
        const int Items = 50_000;
        List<string> bodies = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R/Outlines 4 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>",
            "<</Type/Outlines/First 5 0 R/Count " + Items + ">>",
        };
        for (int i = 0; i < Items; i++)
        {
            string next = i + 1 < Items ? "/Next " + (6 + i) + " 0 R" : string.Empty;
            bodies.Add("<</Title(i)/Parent 4 0 R" + next + "/Dest[3 0 R/Fit]>>");
        }

        using PdfDocument doc = Open(Build(bodies));
        using MemoryStream output = new MemoryStream();

        PdfSanitizer.Inspect(doc).FoundActiveContent.Should().BeFalse();
        PdfSanitizer.Sanitize(doc, output);
        output.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EncryptedInput_NeedsExplicitConsent()
    {
        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(new PdfObjectId(2, 0)));
        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray([new PdfReference(new PdfObjectId(3, 0))]));
        pages.Set(PdfName.Count, 1);
        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(new PdfObjectId(2, 0)));
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(new PdfObjectId(1, 0)));
        using MemoryStream encrypted = new MemoryStream();
        PdfWriter.Write(
            encrypted,
            new List<PdfIndirectObject>
            {
                new PdfIndirectObject(new PdfObjectId(1, 0), catalog),
                new PdfIndirectObject(new PdfObjectId(2, 0), pages),
                new PdfIndirectObject(new PdfObjectId(3, 0), page),
            },
            trailer,
            EncryptionOptions.Aes256("pw"));
        encrypted.Position = 0;
        using PdfDocument doc = PdfDocument.Open(encrypted, "pw", leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        Assert.Throws<OperationsException>(() => PdfSanitizer.Sanitize(doc, output));
        PdfSanitizer.Sanitize(doc, output, new SanitizeOptions { AllowEncryptedInput = true });
        output.Length.Should().BeGreaterThan(0);
    }

    // A page carrying every kind of active content plus one safe link.
    private static byte[] ArmedPdf() => Build(new List<string>
    {
        "<</Type/Catalog/Pages 2 0 R/OpenAction<</S/JavaScript/JS(app.alert('QZXSCRIPT'))>>"
            + "/Names<</JavaScript<</Names[(a) 6 0 R]>>/EmbeddedFiles<</Names[(f) 7 0 R]>>>>"
            + "/AcroForm<</Fields[]/XFA 9 0 R>>/AA<</WC<</S/JavaScript/JS(1)>>>>/Outlines 10 0 R>>",
        "<</Type/Pages/Kids[3 0 R]/Count 1>>",
        "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Contents 4 0 R/AA<</O<</S/Launch/F(cmd.exe)>>>>/Annots[5 0 R 11 0 R 12 0 R 13 0 R 14 0 R 15 0 R]>>",
        "<</Length 14>>\nstream\n0 0 10 10 re f\nendstream",
        "<</Type/Annot/Subtype/Link/Rect[0 0 10 10]/A<</S/Launch/F(calc.exe)>>>>",
        "<</S/JavaScript/JS(app.alert('QZXSCRIPT'))>>",
        "<</Type/Filespec/F(a.exe)/EF<</F 8 0 R>>>>",
        "<</Type/EmbeddedFile/Length 7>>\nstream\nQZXFILE\nendstream",
        "<</Length 10>>\nstream\n<xdp:xdp/>\nendstream",
        "<</Type/Outlines/First 16 0 R/Last 16 0 R/Count 1>>",
        "<</Type/Annot/Subtype/Link/Rect[10 0 20 10]/A<</S/GoTo/D[3 0 R/Fit]/Next<</S/Named/N/NextPage>>>>>>",
        "<</Type/Annot/Subtype/Link/Rect[20 0 30 10]/A<</S/URI/URI(https://example.invalid/)>>>>",
        "<</Type/Annot/Subtype/FileAttachment/Rect[30 0 40 10]/FS 7 0 R>>",
        "<</Type/Annot/Subtype/RichMedia/Rect[40 0 50 10]>>",
        "<</Type/Annot/Subtype/Widget/Rect[50 0 60 10]/A<</S/SubmitForm/F(https://example.invalid/)>>/FT/Tx/T(f)>>",
        "<</Title(t)/Parent 10 0 R/A<</S/GoTo/D[3 0 R/Fit]/Next<</S/GoToR/F(x.pdf)/D[0/Fit]>>>>>>",
    });

    private static PdfDocument Open(byte[] bytes) => PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);

    private static byte[] Build(List<string> bodies)
    {
        StringBuilder sb = new StringBuilder("%PDF-1.7\n");
        List<int> offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(i + 1).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }

        int xref = sb.Length;
        sb.Append("xref\n0 ").Append(bodies.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        sb.Append("trailer\n<</Size ").Append(bodies.Count + 1).Append("/Root 1 0 R>>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}
