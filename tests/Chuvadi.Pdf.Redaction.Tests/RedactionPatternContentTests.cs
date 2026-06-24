// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 (text objects), §9.10 (ToUnicode)
// PHASE: Phase 2 — content-based pattern redaction
//
// Cover for the content-based pattern path: matches are found by decoding each
// show operator's glyphs to Unicode and testing the pattern against that text,
// then removing the matched glyphs directly (independent of layout geometry).
// These assert that every occurrence is removed (not just the first), that
// neighbouring words survive, that case-insensitive matching is opt-in, and that
// the overlay paints a box over a removed run only when enabled.

using System;
using System.Collections.Generic;
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

public sealed class RedactionPatternContentTests
{
    [Fact]
    public void Apply_MultipleOccurrences_AllRemoved_AndNeighboursSurvive()
    {
        // The original defect removed only some occurrences (and over-removed
        // whole lines). Every "Cat" must go; "dog" and "bird" must remain.
        string body = "BT /F1 12 Tf 1 0 0 1 60 700 Tm (Cat dog Cat bird Cat) Tj ET";
        string after = RedactPattern(body, "Cat", out _, drawOverlay: false, overlayColor: null);

        Regex.Matches(after, "Cat").Count.Should().Be(
            0, "every occurrence of the pattern must be removed");
        after.Should().Contain("dog", "non-matching neighbours must survive");
        after.Should().Contain("bird", "non-matching neighbours must survive");
    }

    [Fact]
    public void Apply_CaseSensitiveByDefault_DoesNotMatchOtherCase()
    {
        string body = "BT /F1 12 Tf 1 0 0 1 60 700 Tm (Oncology ward) Tj ET";
        string after = RedactPattern(
            body, new PatternRule("oncology"), out _, drawOverlay: false);

        after.Should().Contain(
            "Oncology", "a lower-case pattern must not match capitalised text by default");
    }

    [Fact]
    public void Apply_IgnoreCase_MatchesRegardlessOfCase()
    {
        string body = "BT /F1 12 Tf 1 0 0 1 60 700 Tm (Oncology ward) Tj ET";
        string after = RedactPattern(
            body, new PatternRule("oncology", null, null, true), out _, drawOverlay: false);

        after.Should().NotContain(
            "Oncology", "ignoreCase must match capitalised text");
        after.Should().Contain("ward", "non-matching neighbours must survive");
    }

    [Fact]
    public void Apply_PatternOverlayEnabled_PaintsBoxOverRemovedRun()
    {
        // The shown text sits at the page origin region so its box is on-page and
        // therefore not suppressed by the out-of-bounds guard.
        string body = "BT /F1 12 Tf 1 0 0 1 60 700 Tm (Secret here) Tj ET";
        RedactPattern(body, new PatternRule("Secret"), out string raw, drawOverlay: true);

        raw.Should().Contain(" rg", "the overlay sets a fill colour when enabled");
        raw.Should().Contain(" re", "a box is painted over the removed pattern run");
    }

    [Fact]
    public void Apply_PatternOverlayDisabled_RemovesTextWithoutBox()
    {
        string body = "BT /F1 12 Tf 1 0 0 1 60 700 Tm (Secret here) Tj ET";
        string after = RedactPattern(
            body, new PatternRule("Secret"), out string raw, drawOverlay: false);

        after.Should().NotContain("Secret", "the text is removed even with no box");
        after.Should().Contain("here", "neighbours survive");
        raw.Should().NotContain(" re", "no box is painted when the overlay is disabled");
    }

    private static string RedactPattern(
        string body, string pattern, out string rawOutput, bool drawOverlay, ColorF? overlayColor)
    {
        return RedactPattern(
            body, new PatternRule(pattern), out rawOutput, drawOverlay, overlayColor);
    }

    private static string RedactPattern(
        string body, PatternRule rule, out string rawOutput, bool drawOverlay)
    {
        return RedactPattern(body, rule, out rawOutput, drawOverlay, overlayColor: null);
    }

    private static string RedactPattern(
        string body, PatternRule rule, out string rawOutput, bool drawOverlay, ColorF? overlayColor)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(rule);

        using MemoryStream source = BuildPdf(body);
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Patterns = new List<PatternRule> { rule },
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
