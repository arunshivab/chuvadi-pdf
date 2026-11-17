// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for 3.35.0 (D-396, D-397; audit A-08 and the Checker's probes):
// pattern redaction matches across operators and inside forms, pattern
// cells and annotations, fails closed, keeps the document's other parts,
// removes attachments it cannot check, uses precise pattern sets, and
// clips shadings out of the boxes; RedactionVerifier checks patterns.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class RedactionAudit3350Tests
{
    private const string Ssn = "123-45-6789";

    private const string Form = "/Type/XObject/Subtype/Form/BBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>>>";

    private static readonly RectangleF WholePage = new RectangleF(0, 0, 400, 400);

    private static readonly int[] FirstPage = { 0 };

    // ── Text drawn in pieces (audit RED-01, Checker) ─────────────────────

    [Theory]
    [InlineData("BT /F1 12 Tf 50 100 Td (SSN 123-45-) Tj (6789 end) Tj ET")]
    [InlineData("BT /F1 12 Tf 50 100 Td [(SSN 123-4) -20 (5-6789 end)] TJ ET")]
    [InlineData("BT /F1 12 Tf 50 100 Td (SSN 123-45-) Tj ET BT /F1 12 Tf 109.37 100 Td (6789 end) Tj ET")]
    [InlineData("BT /F1 12 Tf 50 100 Td (1) Tj (2) Tj (3) Tj (-) Tj (4) Tj (5) Tj (-) Tj (6) Tj (7) Tj (8) Tj (9) Tj ET")]
    [InlineData("q 1 0 0 rg BT /F1 12 Tf 50 100 Td (123-45-) Tj ET Q q 0 0 1 rg BT /F1 12 Tf 90.04 100 Td (6789) Tj ET Q")]
    [InlineData("BT /F1 12 Tf 14 TL 50 114 Td (SSN 123-45-6789) ' ET")]
    [InlineData("BT /F1 12 Tf 50 100 Td 0 Tc (SSN 123-45-6789) Tj ET")]
    public void Ssn_DrawnInPieces_IsRemoved(string content)
    {
        byte[] input = Doc(string.Empty, string.Empty, Keep + content, "/Font<</F1 5 0 R>>");

        (byte[] output, RedactionReport report) = RedactSsn(input);

        report.Pages[0].GlyphsRemoved.Should().BeGreaterThan(0);
        AssertGone(output, "6789", "123-45-");
        PageText(output).Should().Contain("KEEPME");
        Verify(output).IsClean.Should().BeTrue();
    }

    [Fact]
    public void Name_SplitByATjGap_IsMatchedWithTheSpace()
    {
        byte[] input = Doc(string.Empty, string.Empty, Keep + "BT /F1 12 Tf 50 100 Td [(John)-333(Smith)] TJ ET", "/Font<</F1 5 0 R>>");

        (byte[] output, RedactionReport _) = Redact(input, new PatternRule("John Smith"));

        AssertGone(output, "John", "Smith");
        PageText(output).Should().Contain("KEEPME");
    }

    [Fact]
    public void String_SplitAcrossContentStreams_IsRemoved()
    {
        byte[] input = Build(new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R>>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]/Resources<</Font<</F1 5 0 R>>>>/Contents[4 0 R 6 0 R]>>"),
            Stream(string.Empty, Keep + "BT /F1 12 Tf 50 100 Td (ZQXJ"),
            Helvetica(),
            Stream(string.Empty, "9KWV) Tj ET"),
        });

        (byte[] output, RedactionReport _) = Redact(input, new PatternRule("ZQXJ9KWV"));

        AssertGone(output, "ZQXJ", "9KWV");
        PageText(output).Should().Contain("KEEPME");
    }

    // ── Forms, pattern cells, annotations (RED-02, RED-03) ───────────────

    [Fact]
    public void Ssn_InAFormXObject_IsRemoved()
    {
        byte[] input = Doc(
            string.Empty, string.Empty, Keep + "q /X0 Do Q", "/Font<</F1 5 0 R>>/XObject<</X0 6 0 R>>",
            Stream(Form, "BT /F1 12 Tf 50 100 Td (SSN 123-45-) Tj (6789) Tj ET"));

        (byte[] output, RedactionReport _) = RedactSsn(input);

        AssertGone(output, "6789", "123-45-");
        PageText(output).Should().Contain("KEEPME");
    }

    [Fact]
    public void Ssn_InANestedFormWithAMatrix_IsRemoved()
    {
        byte[] input = Doc(
            string.Empty, string.Empty, Keep + "q /X0 Do Q", "/Font<</F1 5 0 R>>/XObject<</X0 6 0 R>>",
            Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]/Resources<</XObject<</X1 7 0 R>>>>", "q 0.5 0 0 0.5 20 20 cm /X1 Do Q"),
            Stream("/Type/XObject/Subtype/Form/BBox[0 0 400 400]/Matrix[1 0 0 1 10 0]/Resources<</Font<</F1 5 0 R>>>>",
                "BT /F1 24 Tf 50 100 Td (123-45-6789) Tj ET"));

        (byte[] output, RedactionReport _) = RedactSsn(input);

        AssertGone(output, "6789");
        PageText(output).Should().Contain("KEEPME");
    }

    [Fact]
    public void Ssn_OnThePageAndInAFormDrawnElsewhere_IsRemovedFromBoth()
    {
        byte[] input = Doc(
            string.Empty, string.Empty, Keep + "BT /F1 12 Tf 50 100 Td (123-45-6789) Tj ET q 1 0 0 1 0 150 cm /X0 Do Q",
            "/Font<</F1 5 0 R>>/XObject<</X0 6 0 R>>",
            Stream(Form, "BT /F1 12 Tf 50 100 Td (123-45-6789) Tj ET"));

        (byte[] output, RedactionReport _) = RedactSsn(input);

        AssertGone(output, "6789");
        PageText(output).Should().Contain("KEEPME");
    }

    [Fact]
    public void Ssn_InATilingPatternCell_IsRemoved()
    {
        byte[] input = Doc(
            string.Empty, string.Empty, Keep + "/Pattern cs /P0 scn 0 0 400 400 re f", "/Font<</F1 5 0 R>>/Pattern<</P0 6 0 R>>",
            Stream("/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 400 400]/XStep 400/YStep 400/Resources<</Font<</F1 5 0 R>>>>",
                "BT /F1 12 Tf 50 100 Td (123-45-6789) Tj ET"));

        (byte[] output, RedactionReport _) = RedactSsn(input);

        AssertGone(output, "6789");
        PageText(output).Should().Contain("KEEPME");
    }

    [Fact]
    public void Ssn_InACellRepeatedTooOftenToPlace_FailsClosed()
    {
        byte[] input = Doc(
            string.Empty, string.Empty, Keep + "/Pattern cs /P0 scn 0 0 400 400 re f", "/Font<</F1 5 0 R>>/Pattern<</P0 6 0 R>>",
            Stream("/PatternType 1/PaintType 1/TilingType 1/BBox[0 0 2 2]/XStep 2/YStep 2/Resources<</Font<</F1 5 0 R>>>>",
                "BT /F1 0.1 Tf 0 1 Td (123-45-6789) Tj ET"));

        using PdfDocument doc = Open(input);
        using MemoryStream output = new MemoryStream();
        Action act = () => Redactor.ApplyWithReport(output, doc, SsnOptions());

        act.Should().Throw<RedactionException>().Which.Message.Should().NotContain(Ssn);
        output.Length.Should().Be(0, "nothing is written when a match cannot be removed");
    }

    [Fact]
    public void Ssn_InAnAnnotationAppearance_RemovesTheAnnotation()
    {
        byte[] input = Doc(
            string.Empty, "/Annots[6 0 R]", Keep, "/Font<</F1 5 0 R>>",
            Obj("<</Type/Annot/Subtype/Stamp/Rect[40 90 200 130]/AP<</N 7 0 R>>>>"),
            Stream("/Type/XObject/Subtype/Form/BBox[0 0 160 40]/Resources<</Font<</F1 5 0 R>>>>",
                "BT /F1 12 Tf 5 10 Td (123-45-) Tj (6789) Tj ET"));

        (byte[] output, RedactionReport report) = RedactSsn(input);

        report.Pages[0].AnnotationsRemoved.Should().Be(1);
        AssertGone(output, "6789");
    }

    [Theory]
    [InlineData("<</Type/Annot/Subtype/FreeText/Rect[200 300 380 380]/Contents(Patient SSN 123-45-6789)/DA(/F1 10 Tf)>>")]
    [InlineData("<</Type/Annot/Subtype/Text/Rect[200 300 220 320]/Contents(see 123-45-6789)>>")]
    [InlineData("<</Type/Annot/Subtype/Link/Rect[200 300 380 320]/A<</S/URI/URI(https://example.com/?ssn=123-45-6789)>>>>")]
    [InlineData("<</Type/Annot/Subtype/Link/Rect[200 300 380 320]/A<</S/JavaScript/JS(app.alert('123-45-6789'))>>>>")]
    public void Ssn_InAnAnnotationsText_RemovesTheAnnotation(string annotation)
    {
        byte[] input = Doc(string.Empty, "/Annots[6 0 R]", Keep, "/Font<</F1 5 0 R>>", Obj(annotation));

        (byte[] output, RedactionReport report) = RedactSsn(input);

        report.Pages[0].AnnotationsRemoved.Should().Be(1);
        AssertGone(output, "6789");
        PageText(output).Should().Contain("KEEPME");
    }

    [Fact]
    public void Ssn_InAFormFieldValue_IsCleared()
    {
        byte[] input = Doc(
            "/AcroForm<</Fields[6 0 R]>>", "/Annots[6 0 R]", Keep, "/Font<</F1 5 0 R>>",
            Obj("<</Type/Annot/Subtype/Widget/FT/Tx/T(ssn)/V(123-45-6789)/DV(123-45-6789)/Rect[200 300 380 320]/P 3 0 R>>"));

        (byte[] output, RedactionReport report) = RedactSsn(input);

        report.Pages[0].FieldValuesCleared.Should().Be(1);
        AssertGone(output, "6789");
    }

    [Fact]
    public void Ssn_InActualText_IsRemovedWithTheTextItStandsFor()
    {
        byte[] input = Doc(
            string.Empty, string.Empty,
            Keep + "/Span <</ActualText (123-45-6789)>> BDC BT /F1 12 Tf 50 100 Td (xxxxxxxx) Tj ET EMC",
            "/Font<</F1 5 0 R>>");

        (byte[] output, RedactionReport report) = RedactSsn(input);

        report.Pages[0].AlternateTextsRemoved.Should().Be(1);
        report.Pages[0].GlyphsRemoved.Should().Be(8);
        AssertGone(output, "6789", "xxxxxxxx");
        PageText(output).Should().Contain("KEEPME");
    }

    // ── The rest of the document (RED-05, D-397) ─────────────────────────

    [Fact]
    public void NamedDestinationsJavaScriptAndOpenAction_AreKept()
    {
        byte[] input = Doc(
            "/Names<</Dests<</Names[(d1) 6 0 R]>>/JavaScript<</Names[(j) 7 0 R]>>>>/OpenAction 7 0 R",
            string.Empty, Keep, "/Font<</F1 5 0 R>>",
            Obj("[3 0 R /Fit]"),
            Obj("<</S/JavaScript/JS(app.alert\\(1\\))>>"));

        (byte[] output, RedactionReport report) = RedactSsn(input);

        string text = AllText(output);
        HasString(text, "d1").Should().BeTrue();
        HasString(text, "app.alert(1)").Should().BeTrue();
        text.Should().Contain("/OpenAction");
        report.CatalogEntriesRemoved.Should().BeEmpty();
        report.ScriptsRemoved.Should().Be(0);
        using PdfDocument redacted = Open(output);
        redacted.Catalog.ContainsKey(Chuvadi.Pdf.Primitives.PdfName.Intern("OpenAction")).Should().BeTrue();
    }

    [Fact]
    public void JavaScriptThatMatches_HasItsScriptRemoved()
    {
        byte[] input = Doc(
            "/Names<</JavaScript<</Names[(j) 6 0 R]>>>>", string.Empty, Keep, "/Font<</F1 5 0 R>>",
            Obj("<</S/JavaScript/JS(var ssn = '123-45-6789';)>>"));

        (byte[] output, RedactionReport report) = RedactSsn(input);

        report.ScriptsRemoved.Should().Be(1);
        AssertGone(output, "6789");
        AllText(output).Should().Contain("/JavaScript");
    }

    [Fact]
    public void TextAttachment_WithoutAMatch_IsKept()
    {
        byte[] input = AttachmentDoc("notes.txt", "/Type/EmbeddedFile/Subtype/text#2Fplain", "nothing secret here");

        (byte[] output, RedactionReport report) = RedactSsn(input);

        report.AttachmentsRemoved.Should().BeEmpty();
        report.AttachmentsNotScanned.Should().BeEmpty();
        HasString(AllText(output), "nothing secret here").Should().BeTrue();
    }

    [Fact]
    public void TextAttachment_WithAMatch_IsRemoved()
    {
        byte[] input = AttachmentDoc("notes.txt", "/Type/EmbeddedFile", "patient 123-45-6789");

        (byte[] output, RedactionReport report) = RedactSsn(input);

        report.AttachmentsRemoved.Should().Equal("notes.txt");
        AssertGone(output, "6789");
    }

    [Fact]
    public void Attachment_WhoseNameMatches_IsRemovedWithItsName()
    {
        byte[] input = AttachmentDoc("123-45-6789.txt", "/Type/EmbeddedFile", "plain");

        (byte[] output, RedactionReport report) = RedactSsn(input);

        report.AttachmentsRemoved.Should().Equal("(name redacted)");
        AssertGone(output, "6789");
    }

    [Fact]
    public void BinaryAttachment_IsRemovedByDefault()
    {
        byte[] input = AttachmentDoc("scan.bin", "/Type/EmbeddedFile", "BIN\0\u0001\u0002DATA");

        (byte[] output, RedactionReport report) = RedactSsn(input);

        report.AttachmentsRemoved.Should().Equal("scan.bin");
        report.AttachmentsNotScanned.Should().BeEmpty();
        AllText(output).Should().NotContain("DATA");
    }

    [Fact]
    public void BinaryAttachment_IsKeptAndListed_WithKeepAttachments()
    {
        byte[] input = AttachmentDoc("scan.bin", "/Type/EmbeddedFile", "BIN\0\u0001\u0002DATA");

        (byte[] output, RedactionReport report) = Redact(input, new RedactionOptions
        {
            Patterns = new List<PatternRule> { new PatternRule(CommonPatterns.UsSsn) },
            KeepAttachments = true,
        });

        report.AttachmentsRemoved.Should().BeEmpty();
        report.AttachmentsNotScanned.Should().Equal("scan.bin");
        AllText(output).Should().Contain("DATA");
    }

    [Fact]
    public void RectanglesOnly_KeepAttachments_AndListThemAsNotScanned()
    {
        // D-398: with no patterns there is nothing to check an attachment
        // against; it is kept and listed.
        byte[] input = AttachmentDoc("notes.txt", "/Type/EmbeddedFile", "nothing secret here");
        RedactionRect box = new RedactionRect(0, new RectangleF(40, 90, 120, 30));

        (byte[] output, RedactionReport report) = Redact(input, new RedactionOptions { Rectangles = new List<RedactionRect> { box } });

        report.AttachmentsRemoved.Should().BeEmpty();
        report.AttachmentsNotScanned.Should().Equal("notes.txt");
        HasString(AllText(output), "nothing secret here").Should().BeTrue();
    }

    [Fact]
    public void PageLimitedPatternsOnly_RemoveUncheckableAttachments()
    {
        byte[] input = AttachmentDoc("notes.txt", "/Type/EmbeddedFile", "nothing secret here");

        (byte[] _, RedactionReport report) = Redact(input, new RedactionOptions
        {
            Patterns = new List<PatternRule> { new PatternRule(CommonPatterns.UsSsn, FirstPage) },
        });

        report.AttachmentsRemoved.Should().Equal("notes.txt");
    }

    [Fact]
    public void PageTemplates_AreDroppedAndReported()
    {
        byte[] input = Doc(
            "/Names<</Dests<</Names[(d1) 6 0 R]>>/Templates<</Names[(t) 7 0 R]>>>>", string.Empty, Keep, "/Font<</F1 5 0 R>>",
            Obj("[3 0 R /Fit]"),
            Obj("<</Type/Template/MediaBox[0 0 10 10]/Contents 8 0 R>>"),
            Stream(string.Empty, "BT /F1 12 Tf 0 0 Td (123-45-6789) Tj ET"));

        (byte[] output, RedactionReport report) = RedactSsn(input);

        report.CatalogEntriesRemoved.Should().Contain("Names/Templates");
        HasString(AllText(output), "d1").Should().BeTrue();
        AssertGone(output, "6789");
    }

    // ── Pattern sets (RED-11) ────────────────────────────────────────────

    [Theory]
    [InlineData("TREATMENT APPROVED BY ONCOLOGY HOSPITAL")]
    [InlineData("MANAGEMENT INTERNATIONAL CARBONATE")]
    [InlineData("Ref A12 and B2B grade T1D")]
    [InlineData("ITIN 912-93-1234")]
    public void PatternSets_DoNotMatchOrdinaryWords(string text)
    {
        Matches(PatternSets.Financial, text).Should().BeEmpty();
        Matches(PatternSets.Medical, text).Should().BeEmpty();
    }

    [Theory]
    [InlineData("SWIFT: DEUTDEFF", "DEUTDEFF")]
    [InlineData("BIC code DEUTDEFF500", "DEUTDEFF500")]
    [InlineData("pay to HDFCINBB0XX now", "HDFCINBB0XX")]
    [InlineData("ITIN 912-70-1234", "912-70-1234")]
    public void FinancialSet_MatchesRealIdentifiers(string text, string expected)
    {
        Matches(PatternSets.Financial, text).Should().Contain(expected);
    }

    [Theory]
    [InlineData("Dx: E11", "E11")]
    [InlineData("ICD-10 code J45.901", "J45.901")]
    [InlineData("noted E11.9 today", "E11.9")]
    public void MedicalSet_MatchesRealCodes(string text, string expected)
    {
        Matches(PatternSets.Medical, text).Should().Contain(expected);
    }

    [Fact]
    public void FinancialSet_OnAPage_KeepsTheWords()
    {
        byte[] input = Doc(string.Empty, string.Empty, "BT /F1 12 Tf 20 300 Td (TREATMENT APPROVED BY ONCOLOGY HOSPITAL) Tj ET", "/Font<</F1 5 0 R>>");

        (byte[] output, RedactionReport report) = Redact(input, new RedactionOptions { Patterns = PatternSets.Financial.ToList() });

        report.Pages[0].GlyphsRemoved.Should().Be(0);
        PageText(output).Should().Contain("APPROVED").And.Contain("HOSPITAL");
    }

    [Theory]
    [InlineData("DEUTDEFF", true)]
    [InlineData("DEUTXXFF", false)]
    [InlineData("HDFCINBB0XX", true)]
    [InlineData("HOSPITAL", true)]
    [InlineData("ABCD12FF", false)]
    public void Bic_ChecksTheCountryCode(string value, bool expected)
    {
        PatternValidators.Bic(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("912-70-1234", true)]
    [InlineData("912-50-1234", true)]
    [InlineData("912-93-1234", false)]
    [InlineData("812-70-1234", false)]
    public void Itin_ChecksTheGroup(string value, bool expected)
    {
        PatternValidators.Itin(value).Should().Be(expected);
    }

    // ── Shadings under a box (RED-08) ────────────────────────────────────

    [Fact]
    public void Shading_UnderABox_IsClippedOutOfIt()
    {
        const string Shading = "/Shading<</Sh 6 0 R>>";
        byte[] input = Doc(
            string.Empty, string.Empty, Keep + "q /Sh sh Q", "/Font<</F1 5 0 R>>" + Shading,
            Obj("<</ShadingType 2/ColorSpace/DeviceRGB/Coords[0 0 400 0]/Function<</FunctionType 2/Domain[0 1]/C0[1 0 0]/C1[0 0 1]/N 1>>>>"));
        RedactionRect box = new RedactionRect(0, new RectangleF(40, 90, 120, 30));

        using (PdfDocument before = Open(input))
        {
            RedactionVerifier.Verify(before, new[] { box }).Survivors
                .Should().Contain(s => s.Kind == RedactionSurvivorKind.Shading);
        }

        (byte[] output, RedactionReport report) = Redact(
            input, new RedactionOptions { Rectangles = new List<RedactionRect> { box }, DrawOverlay = false });

        report.Pages[0].PathsCut.Should().Be(1);
        using PdfDocument after = Open(output);
        RedactionVerifier.Verify(after, new[] { box }).IsClean.Should().BeTrue();
        AllText(output).Should().Contain("W*").And.Contain("/Sh sh");
    }

    [Fact]
    public void MeshShading_UnderABox_IsRemoved()
    {
        byte[] input = Doc(
            string.Empty, string.Empty, Keep + "q /Sh sh Q", "/Font<</F1 5 0 R>>/Shading<</Sh 6 0 R>>",
            Stream("/ShadingType 4/ColorSpace/DeviceRGB/BitsPerCoordinate 8/BitsPerComponent 8/BitsPerFlag 8/Decode[0 400 0 400 0 1 0 1 0 1]",
                "\0\0\0ÿ\0\0\0ÿ\0\0ÿ\0\0\0ÿ\0\0\0ÿ"));
        RedactionRect box = new RedactionRect(0, new RectangleF(40, 90, 120, 30));

        (byte[] output, RedactionReport report) = Redact(input, new RedactionOptions { Rectangles = new List<RedactionRect> { box } });

        report.Pages[0].PathsRemoved.Should().Be(1);
        Latin1(output).Should().NotContain("/Sh sh");
        AllText(output).Should().NotContain("/Sh sh");
    }

    // ── Verifying a pattern redaction (Checker) ──────────────────────────

    [Fact]
    public void VerifyPatterns_FindsWhatAnUnredactedDocumentHolds()
    {
        byte[] input = Doc(
            "/Names<</JavaScript<</Names[(j) 7 0 R]>>>>", "/Annots[6 0 R]",
            Keep + "BT /F1 12 Tf 50 100 Td (123-45-) Tj (6789) Tj ET", "/Font<</F1 5 0 R>>",
            Obj("<</Type/Annot/Subtype/Text/Rect[200 300 220 320]/Contents(see 123-45-6789)>>"),
            Obj("<</S/JavaScript/JS(var s = '123-45-6789';)>>"));

        using PdfDocument doc = Open(input);
        RedactionVerification v = RedactionVerifier.VerifyPatterns(doc, new[] { new PatternRule(CommonPatterns.UsSsn) });

        v.IsClean.Should().BeFalse();
        v.Survivors.Should().Contain(s => s.Kind == RedactionSurvivorKind.Text && s.PageIndex == 0 && s.Text == Ssn);
        v.Survivors.Should().Contain(s => s.Kind == RedactionSurvivorKind.Annotation);
        v.Survivors.Should().Contain(s => s.Kind == RedactionSurvivorKind.DocumentText && s.PageIndex == -1);
    }

    [Fact]
    public void VerifyPatterns_IsCleanAfterRedaction()
    {
        byte[] input = Doc(
            "/Names<</JavaScript<</Names[(j) 7 0 R]>>>>", "/Annots[6 0 R]",
            Keep + "BT /F1 12 Tf 50 100 Td (123-45-) Tj (6789) Tj ET /Span <</ActualText (123-45-6789)>> BDC EMC",
            "/Font<</F1 5 0 R>>",
            Obj("<</Type/Annot/Subtype/Text/Rect[200 300 220 320]/Contents(see 123-45-6789)>>"),
            Obj("<</S/JavaScript/JS(var s = '123-45-6789';)>>"));

        (byte[] output, RedactionReport _) = RedactSsn(input);

        Verify(output).IsClean.Should().BeTrue();
    }

    [Fact]
    public void VerifyPatterns_ReportsAnAttachmentItCannotCheck()
    {
        byte[] input = AttachmentDoc("scan.bin", "/Type/EmbeddedFile", "BIN\0\u0001DATA");

        using PdfDocument doc = Open(input);
        RedactionVerification v = RedactionVerifier.VerifyPatterns(doc, new[] { new PatternRule(CommonPatterns.UsSsn) });

        v.Survivors.Should().ContainSingle(s => s.Kind == RedactionSurvivorKind.Attachment && s.Text == null);
    }

    [Fact]
    public void VerifyPatterns_RejectsNullArguments()
    {
        using PdfDocument doc = Open(Doc(string.Empty, string.Empty, Keep, "/Font<</F1 5 0 R>>"));

        Action noDoc = () => RedactionVerifier.VerifyPatterns(null!, Array.Empty<PatternRule>());
        Action noPatterns = () => RedactionVerifier.VerifyPatterns(doc, null!);

        noDoc.Should().Throw<ArgumentNullException>();
        noPatterns.Should().Throw<ArgumentNullException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private const string Keep = "BT /F1 12 Tf 50 300 Td (KEEPME) Tj ET ";

    private static RedactionOptions SsnOptions() => new RedactionOptions
    {
        Patterns = new List<PatternRule> { new PatternRule(CommonPatterns.UsSsn) },
    };

    private static (byte[] Output, RedactionReport Report) RedactSsn(byte[] input) => Redact(input, SsnOptions());

    private static (byte[] Output, RedactionReport Report) Redact(byte[] input, PatternRule rule) =>
        Redact(input, new RedactionOptions { Patterns = new List<PatternRule> { rule } });

    private static (byte[] Output, RedactionReport Report) Redact(byte[] input, RedactionOptions options)
    {
        using PdfDocument doc = Open(input);
        using MemoryStream output = new MemoryStream();
        RedactionReport report = Redactor.ApplyWithReport(output, doc, options);
        return (output.ToArray(), report);
    }

    private static RedactionVerification Verify(byte[] output)
    {
        using PdfDocument doc = Open(output);
        return RedactionVerifier.VerifyPatterns(doc, new[] { new PatternRule(CommonPatterns.UsSsn) });
    }

    private static List<string> Matches(IReadOnlyList<PatternRule> set, string text)
    {
        List<string> found = new List<string>();
        foreach (PatternRule rule in set)
        {
            foreach (Match m in rule.Regex.Matches(text))
            {
                if (rule.Validator is null || rule.Validator(m.Value))
                {
                    found.Add(m.Value);
                }
            }
        }

        return found;
    }

    // The glyph text a viewer shows on page 1.
    private static string PageText(byte[] pdf)
    {
        using PdfDocument doc = Open(pdf);
        StringBuilder sb = new StringBuilder();
        foreach (RedactionSurvivor s in RedactionVerifier.ExtractWithin(doc, 0, WholePage))
        {
            if (s.Kind == RedactionSurvivorKind.Text)
            {
                sb.Append(s.Text);
            }
        }

        return sb.ToString();
    }

    // Every piece must be absent from the file: as bytes, inside every
    // inflated stream, as hex and as UTF-16.
    private static void AssertGone(byte[] pdf, params string[] pieces)
    {
        string all = AllText(pdf);
        foreach (string piece in pieces)
        {
            all.Should().NotContain(piece);
            all.Should().NotContain(Convert.ToHexString(Encoding.Latin1.GetBytes(piece)));
            all.Should().NotContain(Encoding.Latin1.GetString(Encoding.BigEndianUnicode.GetBytes(piece)));
        }
    }

    // A string in the file, written literally or as hex.
    private static bool HasString(string all, string value) =>
        all.Contains(value, StringComparison.Ordinal)
        || all.Contains("<" + Convert.ToHexString(Encoding.Latin1.GetBytes(value)) + ">", StringComparison.OrdinalIgnoreCase);

    // The file's bytes followed by every stream inflated where it can be.
    private static string AllText(byte[] pdf)
    {
        string raw = Latin1(pdf);
        StringBuilder sb = new StringBuilder(raw);
        int at = 0;
        while ((at = raw.IndexOf("stream", at, StringComparison.Ordinal)) >= 0)
        {
            int start = at + 6;
            if (start < raw.Length && raw[start] == '\r')
            {
                start++;
            }

            if (start < raw.Length && raw[start] == '\n')
            {
                start++;
            }

            int end = raw.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            byte[] data = new byte[end - start];
            Array.Copy(pdf, start, data, 0, data.Length);
            try
            {
                using MemoryStream input = new MemoryStream(data);
                using ZLibStream z = new ZLibStream(input, CompressionMode.Decompress);
                using MemoryStream inflated = new MemoryStream();
                z.CopyTo(inflated);
                sb.Append('\n').Append(Latin1(inflated.ToArray()));
            }
            catch (InvalidDataException)
            {
                // Not Flate data: already in the raw bytes.
            }

            at = end + 9;
        }

        return sb.ToString();
    }

    private static byte[] AttachmentDoc(string name, string fileEntries, string data) => Doc(
        "/Names<</EmbeddedFiles<</Names[(" + name + ") 6 0 R]>>>>", string.Empty, Keep, "/Font<</F1 5 0 R>>",
        Obj("<</Type/Filespec/F(" + name + ")/UF(" + name + ")/EF<</F 7 0 R>>>>"),
        Stream(fileEntries, data));

    // A one-page document: catalog 1, pages 2, page 3, content 4,
    // Helvetica 5, then the extra objects from 6.
    private static byte[] Doc(string catalogExtra, string pageExtra, string content, string resources, params byte[][] extra)
    {
        List<byte[]> bodies = new List<byte[]>
        {
            Obj("<</Type/Catalog/Pages 2 0 R" + catalogExtra + ">>"),
            Obj("<</Type/Pages/Kids[3 0 R]/Count 1>>"),
            Obj("<</Type/Page/Parent 2 0 R/MediaBox[0 0 400 400]" + pageExtra + "/Resources<<" + resources + ">>/Contents 4 0 R>>"),
            Stream(string.Empty, content),
            Helvetica(),
        };
        bodies.AddRange(extra);
        return Build(bodies);
    }

    private static byte[] Helvetica() => Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica/Encoding/WinAnsiEncoding>>");

    private static PdfDocument Open(byte[] bytes) => PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);

    private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static byte[] Obj(string body) => Encoding.Latin1.GetBytes(body);

    private static byte[] Stream(string entries, string content) => Stream(entries, Encoding.Latin1.GetBytes(content));

    private static byte[] Stream(string entries, byte[] content)
    {
        using MemoryStream ms = new MemoryStream();
        ms.Write(Encoding.Latin1.GetBytes(
            "<<" + entries + "/Length " + content.Length.ToString(CultureInfo.InvariantCulture) + ">>\nstream\n"));
        ms.Write(content);
        ms.Write(Encoding.Latin1.GetBytes("\nendstream"));
        return ms.ToArray();
    }

    private static byte[] Build(List<byte[]> bodies)
    {
        using MemoryStream ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        List<long> offsets = new List<long>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(ms.Length);
            Write((i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            ms.Write(bodies[i]);
            Write("\nendobj\n");
        }

        long xref = ms.Length;
        Write("xref\n0 " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n");
        foreach (long off in offsets)
        {
            Write(off.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        Write("trailer <</Size " + (bodies.Count + 1).ToString(CultureInfo.InvariantCulture)
            + "/Root 1 0 R>>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF");
        return ms.ToArray();
    }
}
