// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 (text objects), §8.3.4 (coordinate systems)
// PHASE: Phase 2 — redaction text-matrix correctness, boxless mode, descender box
//
// Regression cover for a security defect: a redaction box was drawn but the
// matched glyphs survived under any non-identity text matrix. The strip built
// each glyph's position from the text-matrix translation AND then transformed
// that point by the same matrix, applying the translation twice; under a real
// Tm (or a page cm) the test box never intersected the rectangle, so nothing
// was removed. Synthetic content using only Td slipped past because Td leaves
// the text matrix at identity.
//
// These tests assert the matched text is physically removed across Td / Tm /
// cm / scale / y-flip and every show operator (Tj, TJ, ', "), that boxless
// redaction removes text without painting a box, that the overlay box covers
// descender tails, and that in-place replacement works under a non-identity Tm.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class RedactionTextMatrixTests
{
    [Theory]
    // Td baseline (text matrix stays identity) — the case that always worked.
    [InlineData("Tj_Td", "BT /F1 12 Tf 60 700 Td (Father) Tj ET")]
    // Tm translation — the real-world case (Word / LibreOffice emit Tm per line).
    [InlineData("Tj_Tm", "BT /F1 12 Tf 1 0 0 1 60 700 Tm (Father) Tj ET")]
    [InlineData("TJ_Tm", "BT /F1 12 Tf 1 0 0 1 60 700 Tm [(Fa)-5(ther)] TJ ET")]
    // ' shows on a new line then draws; " sets spacings, new line, then draws.
    [InlineData("Apos_Tm", "BT /F1 12 Tf 1 0 0 1 60 700 Tm (Father) ' ET")]
    [InlineData("Quote_Tm", "BT /F1 12 Tf 1 0 0 1 60 700 Tm 0 0 (Father) \" ET")]
    // Page-level cm combined with Tm — the strip must share the parser's frame.
    [InlineData("TJ_cm", "q 1 0 0 1 40 50 cm BT /F1 12 Tf 1 0 0 1 60 700 Tm [(Father)] TJ ET Q")]
    [InlineData("Tj_scale", "q 2 0 0 2 0 0 cm BT /F1 12 Tf 1 0 0 1 30 200 Tm (Father) Tj ET Q")]
    [InlineData("Tj_yflip", "1 0 0 -1 0 842 cm BT /F1 12 Tf 1 0 0 1 60 120 Tm (Father) Tj ET")]
    public void Apply_PatternStripsMatchedText_UnderAnyMatrix(string label, string body)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(body);

        string after = RedactPattern(body, "Father", out _);
        after.Should().NotContain(
            "Father", $"case '{label}' must physically remove the matched glyphs");
    }

    [Fact]
    public void Apply_BoxlessByFlag_RemovesTextAndPaintsNoBox()
    {
        string body = "BT /F1 12 Tf 1 0 0 1 60 700 Tm (Father here) Tj ET";
        string after = RedactPattern(
            body, "Father", out string raw, drawOverlay: false, overlayColor: null);

        after.Should().NotContain("Father", "the matched glyphs must be removed");
        after.Should().Contain("here", "neighbouring text must survive");
        raw.Should().NotContain(" rg", "no overlay colour is set when the box is disabled");
        raw.Should().NotContain(" re", "no overlay rectangle is painted when the box is disabled");
    }

    [Fact]
    public void Apply_BoxlessByTransparentColor_RemovesTextAndPaintsNoBox()
    {
        string body = "BT /F1 12 Tf 1 0 0 1 60 700 Tm (Father here) Tj ET";
        string after = RedactPattern(
            body, "Father", out string raw, drawOverlay: true, overlayColor: ColorF.Transparent);

        after.Should().NotContain("Father");
        raw.Should().NotContain(" rg", "a fully transparent overlay paints nothing");
        raw.Should().NotContain(" re");
    }

    [Fact]
    public void Apply_DefaultOverlay_PaintsBoxThatCoversDescenders()
    {
        // "gypsy" is all descenders/ascenders; the box must extend below the
        // baseline far enough to cover the tails, so its height exceeds the
        // font size rather than sitting on the baseline.
        const double fontSize = 12.0;
        string body = $"BT /F1 {fontSize.ToString(CultureInfo.InvariantCulture)} Tf "
            + "1 0 0 1 60 700 Tm (gypsy) Tj ET";
        RedactPattern(body, "gypsy", out string raw);

        raw.Should().Contain(" rg", "the default overlay sets a fill colour");

        Match m = Regex.Match(
            raw,
            @"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) re");
        m.Success.Should().BeTrue("an overlay rectangle must be painted");

        double height = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
        height.Should().BeGreaterThan(
            fontSize, "the box must cover descender tails below the baseline");
    }

    [Fact]
    public void Apply_ReplacementUnderTm_RemovesOriginalAndDrawsReplacement()
    {
        string body = "BT /F1 12 Tf 1 0 0 1 60 700 Tm (Secret) Tj ET";
        using MemoryStream source = BuildPdf(body);
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(58, 695, 80, 20), "XX"),
            },
        };
        Redactor.Apply(output, doc, opts);

        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        string after = new TextExtractor(result.Objects).ExtractText(result.Pages[0]);

        after.Should().NotContain("Secret", "the original glyphs must be removed under Tm");
        after.Should().Contain("XX", "the in-place replacement must be drawn in the gap");
    }

    private static string RedactPattern(string body, string pattern, out string rawOutput)
    {
        return RedactPattern(body, pattern, out rawOutput, drawOverlay: true, overlayColor: null);
    }

    private static string RedactPattern(
        string body, string pattern, out string rawOutput, bool drawOverlay, ColorF? overlayColor)
    {
        using MemoryStream source = BuildPdf(body);
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Patterns = new List<PatternRule> { new PatternRule(pattern) },
            DrawOverlay = drawOverlay,
            OverlayColor = overlayColor ?? ColorF.Black,
        };

        Redactor.Apply(output, doc, opts);

        byte[] outBytes = output.ToArray();
        rawOutput = Encoding.Latin1.GetString(outBytes);

        using PdfDocument result = PdfDocument.Open(new MemoryStream(outBytes), leaveOpen: true);
        return new TextExtractor(result.Objects).ExtractText(result.Pages[0]);
    }

    private static MemoryStream BuildPdf(string body)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId fontId = new PdfObjectId(5, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(842),
        }));

        PdfDictionary font = new PdfDictionary();
        font.Set(PdfName.Type, PdfName.Intern("Font"));
        font.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        font.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Helvetica"));

        PdfDictionary fontResources = new PdfDictionary();
        fontResources.Set(PdfName.Intern("F1"), new PdfReference(fontId));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("Font"), fontResources);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.Contents, new PdfReference(contentId));
        page.Set(PdfName.Intern("Resources"), resources);

        byte[] content = Encoding.ASCII.GetBytes(body + "\n");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
            new PdfIndirectObject(fontId, font),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Position = 0;
        return ms;
    }
}
