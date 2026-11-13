// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3 (object syntax), §7.3.8 (streams),
//        §7.6.5 (crypt filters), §7.7.3 (page tree), Table 30 (MediaBox)
// PHASE: 3.34.0 (#52a, #52c, #52d, #52f, #52g, #52h; D-353) — damaged input
//        read past with a warning instead of failing the page or the open.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering;
using Chuvadi.Pdf.Svg;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Integration.Tests;

public sealed class DamagedInput3340Tests
{
    private const string Font = "<</Font<</F1<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>>>>>";

    // ── #52a: content syntax damage ───────────────────────────────────────

    [Fact]
    public void UnterminatedString_DrawsWhatCameBefore_AndWarns()
    {
        List<PdfWarning> warnings = new List<PdfWarning>();
        using PdfDocument doc = TestPdf.Open(
            TestPdf.OnePage("0 0 1 rg 20 20 60 60 re f BT /F1 12 Tf 10 150 Td (Hello) Tj ET BT (never closed", Font, Array.Empty<string>()),
            warnings);

        TestPdf.Ink(doc, AnnotationMode.View, 20, 20, 80, 80).Should().BeGreaterThan(3000);
        warnings.Should().Contain(w => w.Kind == PdfWarningKind.StreamDamaged);

        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Should().Contain("Hello");
        new SvgRenderer().RenderPage(doc, 0).Should().Contain("<svg");
    }

    // ── #52c: missing /Length and damaged resources ───────────────────────

    [Fact]
    public void StreamWithoutLength_IsReadToEndstream()
    {
        // The content stream has no /Length: it is read up to endstream.
        byte[] bytes = Raw(
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Contents 4 0 R>>",
            "<<>>\nstream\n0 0 1 rg 20 20 60 60 re f\nendstream");
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions());

        TestPdf.Ink(doc, AnnotationMode.View, 20, 20, 80, 80).Should().BeGreaterThan(3000);
        doc.Warnings.Should().Contain(w => w.Contains("Length", StringComparison.Ordinal));
    }

    [Fact]
    public void DamagedToUnicode_AndDamagedForm_DoNotFailTextExtraction()
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int cmap = pdf.AddStream("<</Filter/FlateDecode>>", new byte[] { 0x78, 0x9C, 0xFF, 0xFF, 0x00, 0x13 });
        int form = pdf.AddStream("<</Type/XObject/Subtype/Form/BBox[0 0 200 200]/Filter/FlateDecode>>", new byte[] { 0x78, 0x9C, 0xFF, 0xFF, 0x00, 0x13 });
        int contents = pdf.AddStream("<<>>", "BT /F1 12 Tf 10 150 Td (Hello) Tj ET /Fx Do");
        int page = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/MediaBox[0 0 200 200]/Resources<</Font<</F1<</Type/Font/Subtype/Type1/BaseFont/Helvetica/ToUnicode "
            + N(cmap) + " 0 R>>>>/XObject<</Fx " + N(form) + " 0 R>>>>/Contents " + N(contents) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        using PdfDocument doc = TestPdf.Open(pdf.Build(catalog));

        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Should().Contain("Hello");
    }

    // ── #52d: RepairIfDamaged ─────────────────────────────────────────────

    [Fact]
    public void CatalogPagesThatIsNotAPageTree_IsRepaired()
    {
        byte[] bytes = Raw(
            "<</Type/Catalog/Pages 5 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Contents 4 0 R>>",
            "<</Length 25>>\nstream\n0 0 1 rg 20 20 60 60 re f\nendstream",
            "(not a page tree)");

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions { RepairIfDamaged = true });

        doc.PageCount.Should().Be(1);
        TestPdf.Ink(doc, AnnotationMode.View, 20, 20, 80, 80).Should().BeGreaterThan(3000);
        doc.Warnings.Should().Contain(w => w.Contains("page tree", StringComparison.Ordinal));
    }

    [Fact]
    public void UndecodableCrossReferenceStream_IsRepaired()
    {
        // startxref points at a cross-reference stream whose data is not Flate.
        StringBuilder text = new StringBuilder("%PDF-1.7\n");
        text.Append("1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n");
        text.Append("2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n");
        text.Append("3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>\nendobj\n");
        long xref = text.Length;
        text.Append("4 0 obj\n<</Type/XRef/Size 5/W[1 2 1]/Root 1 0 R/Filter/FlateDecode/Length 4>>\nstream\nabcd\nendstream\nendobj\n");
        text.Append("startxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        byte[] bytes = Encoding.Latin1.GetBytes(text.ToString());

        Action plain = () => PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions()).Dispose();
        plain.Should().Throw<PdfException>();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions { RepairIfDamaged = true });
        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public void EncryptEntryThatIsNotADictionary_OpensUnencryptedWithRepair()
    {
        byte[] bytes = Raw(
            new[]
            {
                "<</Type/Catalog/Pages 2 0 R>>",
                "<</Type/Pages/Kids[3 0 R]/Count 1>>",
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>",
                "(damaged)",
            },
            "/Encrypt 4 0 R");

        Action plain = () => PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions()).Dispose();
        plain.Should().Throw<PdfParseException>();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions { RepairIfDamaged = true });
        doc.PageCount.Should().Be(1);
        doc.Warnings.Should().Contain(w => w.Contains("opened as unencrypted", StringComparison.Ordinal));
    }

    // ── #52d: crypt filters (valid files) ─────────────────────────────────

    [Fact]
    public void OnlyEmbeddedFilesEncrypted_OpensWithoutPassword()
    {
        string hex48 = new string('A', 96);
        string hex32 = new string('B', 64);
        byte[] bytes = Raw(
            new[]
            {
                "<</Type/Catalog/Pages 2 0 R>>",
                "<</Type/Pages/Kids[3 0 R]/Count 1>>",
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]/Contents 5 0 R>>",
                "<</Filter/Standard/V 5/R 6/Length 256/P -4/CF<</StdCF<</CFM/AESV3/AuthEvent/EFOpen/Length 32>>>>/StmF/Identity/StrF/Identity/EFF/StdCF"
                    + "/O<" + hex48 + ">/U<" + hex48 + ">/OE<" + hex32 + ">/UE<" + hex32 + ">/Perms<" + new string('C', 32) + ">>>",
                "<</Length 25>>\nstream\n0 0 1 rg 20 20 60 60 re f\nendstream",
            },
            "/Encrypt 4 0 R/ID[<00112233445566778899AABBCCDDEEFF><00112233445566778899AABBCCDDEEFF>]");

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions());

        TestPdf.Ink(doc, AnnotationMode.View, 20, 20, 80, 80).Should().BeGreaterThan(3000);
        doc.Warnings.Should().Contain(w => w.Contains("embedded files", StringComparison.Ordinal));
        PdfDocument.RequiresPassword(new MemoryStream(bytes)).Should().BeFalse();

        Action wrong = () => PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions { Password = "wrong" }).Dispose();
        wrong.Should().Throw<PdfPasswordRequiredException>();
    }

    // ── #52f: MediaBox ────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("/MediaBox[0 0 0 0]")]
    [InlineData("/MediaBox[0 0 /x 100]")]
    public void MissingOrInvalidMediaBox_IsUsLetter_WithAWarning(string box)
    {
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int page = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R" + box + ">>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        using PdfDocument doc = TestPdf.Open(pdf.Build(catalog));

        doc.Pages[0].Width.Should().Be(612);
        doc.Pages[0].Height.Should().Be(792);
        PixelBuffer px = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);
        px.Width.Should().Be(612);
        doc.Warnings.Should().ContainSingle(w => w.Contains("US Letter", StringComparison.Ordinal));
    }

    // ── #52g: page tree loops ─────────────────────────────────────────────

    [Fact]
    public void LoopingPageTree_SkipsTheLoop_AndRepairCountsByWalking()
    {
        // /Count says 2, but the second kid is a /Pages chain that loops back
        // (3 -> 4 -> 3) and holds no page.
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int root = pdf.Reserve();
        int a = pdf.Reserve();
        int b = pdf.Reserve();
        int contents = pdf.AddStream("<<>>", "0 0 1 rg 20 20 60 60 re f");
        int page = pdf.Add("<</Type/Page/Parent " + N(root) + " 0 R/MediaBox[0 0 200 200]/Contents " + N(contents) + " 0 R>>");
        pdf.Set(root, "<</Type/Pages/Kids[" + N(page) + " 0 R " + N(a) + " 0 R]/Count 2>>");
        pdf.Set(a, "<</Type/Pages/Parent " + N(root) + " 0 R/Kids[" + N(b) + " 0 R]/Count 1>>");
        pdf.Set(b, "<</Type/Pages/Parent " + N(a) + " 0 R/Kids[" + N(a) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(root) + " 0 R>>");
        byte[] bytes = pdf.Build(catalog);

        using (PdfDocument doc = TestPdf.Open(bytes))
        {
            doc.PageCount.Should().Be(2);
            TestPdf.Ink(doc, AnnotationMode.View, 20, 20, 80, 80).Should().BeGreaterThan(3000);
            Action second = () => { PdfPage _ = doc.Pages[1]; };
            second.Should().Throw<PdfCorruptionException>().WithMessage("*not found*");
            doc.Warnings.Should().Contain(w => w.Contains("loops back", StringComparison.Ordinal));
        }

        using (PdfDocument repaired = PdfDocument.Open(new MemoryStream(bytes), new PdfOpenOptions { RepairIfDamaged = true }))
        {
            repaired.PageCount.Should().Be(1);
            repaired.Warnings.Should().Contain(w => w.Contains("1 page(s), where /Count says 2", StringComparison.Ordinal));
        }
    }

    // ── #52h: damaged object syntax ───────────────────────────────────────

    [Fact]
    public void NonNameKey_AndEmptyName_AreSkippedWithAWarning()
    {
        // "/BaseFont /Arial Unicode MS" splits into a name and two keywords in
        // key position; "/ 5" is an empty key with its value.
        TestPdf pdf = new TestPdf();
        int catalog = pdf.Reserve();
        int pages = pdf.Reserve();
        int contents = pdf.AddStream("<<>>", "0 0 1 rg 20 20 60 60 re f BT /F1 12 Tf 10 150 Td (Hello) Tj ET");
        int font = pdf.Add("<</Type/Font/Subtype/Type1/BaseFont/Arial Unicode MS/Encoding/WinAnsiEncoding>>");
        int page = pdf.Add("<</Type/Page/Parent " + N(pages) + " 0 R/ 5/MediaBox[0 0 200 200]/Resources<</Font<</F1 " + N(font) + " 0 R>>>>/Contents " + N(contents) + " 0 R>>");
        pdf.Set(pages, "<</Type/Pages/Kids[" + N(page) + " 0 R]/Count 1>>");
        pdf.Set(catalog, "<</Type/Catalog/Pages " + N(pages) + " 0 R>>");
        using PdfDocument doc = TestPdf.Open(pdf.Build(catalog));

        TestPdf.Ink(doc, AnnotationMode.View, 20, 20, 80, 80).Should().BeGreaterThan(3000);
        new TextExtractor(doc.Objects).ExtractText(doc.Pages[0]).Should().Contain("Hello");
        doc.Warnings.Should().Contain(w => w.Contains("not a name", StringComparison.Ordinal));
        doc.Warnings.Should().Contain(w => w.Contains("empty name", StringComparison.Ordinal));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // A file of numbered objects (bodies given whole, streams included) with
    // a classic cross-reference table; the catalog is object 1.
    private static byte[] Raw(params string[] bodies) => Raw(bodies, string.Empty);

    private static byte[] Raw(string[] bodies, string trailerExtra)
    {
        using MemoryStream ms = new MemoryStream();
        void W(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        W("%PDF-1.7\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Length; i++)
        {
            offsets.Add(ms.Position);
            W(N(i + 1) + " 0 obj\n" + bodies[i] + "\nendobj\n");
        }

        long xref = ms.Position;
        W("xref\n0 " + N(bodies.Length + 1) + "\n0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            W(off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        W("trailer\n<</Size " + N(bodies.Length + 1) + "/Root 1 0 R" + trailerExtra + ">>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
        return ms.ToArray();
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
}
