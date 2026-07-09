// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4.3 (text-showing operators)
// PHASE: Redaction hit testing — tight per-glyph ink bounds
//
// Deciding whether a redaction rectangle targets a glyph uses the glyph's
// actual ink extent, not an inflated line box. Previously every glyph's hit
// box extended 0.25 em + 1.5 pt below its baseline, so a rectangle drawn in
// the blank gap between two lines "intersected" the line above and silently
// deleted words the user never covered (a box around one word ate part of
// the heading above it). Detection is now tight; removal of anything
// genuinely hit stays as over-redacting as before (B15) — a rectangle that
// covers only a descender tail still removes those glyphs.
//
// Fixture geometry (Helvetica): "Introduction" 12 pt at baseline y=720;
// "Oncology Professional details" 10 pt at baseline y=706. "Oncology" spans
// user-X ≈ 40..82. Tight bounds: heading ink bottom ≈ 719.5; body ascent top
// ≈ 714; body descender floor ≈ 703.5.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Redaction.Tests;

public sealed class RedactionInkBoundsTests
{
    [Fact]
    public void RectInInterLineGap_DoesNotRemoveLineAbove()
    {
        // Top edge y=718 sits in the blank gap under the heading (its glyphs
        // end at the baseline, y=720; no descenders in "Introduction"). The
        // old inflated hit box reached down to 715.5 and ate the heading.
        string after = Redact(new RectangleF(38, 703, 44, 15));

        after.Should().Contain("Introduction", "the line above was never touched by the rectangle");
        after.Should().NotContain("Oncology", "the covered word must be removed");
        after.Should().Contain("Professional", "the rest of the covered word's line must survive");
    }

    [Fact]
    public void RectCrossingBaselineAbove_StillRemovesThatText()
    {
        // Top edge y=720.5 crosses the heading's baseline — a genuine hit;
        // over-redaction of genuinely hit text is intended (B15).
        string after = Redact(new RectangleF(38, 703, 44, 17.5));

        after.Should().NotContain("Introduction");
        after.Should().NotContain("Oncology");
    }

    [Fact]
    public void RectOverDescenderTailsOnly_RemovesDescenderGlyphs()
    {
        // A strip below the baseline (702..704.2) covers only the descender
        // tails of 'g' and 'y' in "Oncology" — those glyphs must still be
        // treated as hit (safety), while glyphs sitting on the baseline stay.
        string after = Redact(new RectangleF(38, 702, 48, 2.2));

        after.Should().NotContain("Oncology", "the descender glyphs are removed, breaking the word");
        after.Should().Contain("Oncolo", "glyphs whose ink the rectangle never touched survive");
        after.Should().Contain("Introduction");
        after.Should().Contain("Professional");
    }

    [Fact]
    public void RectFullyInWhitespaceBetweenLines_RemovesNothing()
    {
        // 715..719 lies between the heading's ink bottom (≈719.5) and the
        // body's ascent top (≈714). The old inflated boxes intersected BOTH
        // lines from this rectangle; the tight bounds hit neither.
        string after = Redact(new RectangleF(38, 715, 48, 4));

        after.Should().Contain("Introduction");
        after.Should().Contain("Oncology Professional details");
    }

    private static string Redact(RectangleF rect)
    {
        using MemoryStream source = BuildTwoLinePdf();
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, rect),
            },
        };
        Redactor.Apply(output, doc, opts);

        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        return new TextExtractor(result.Objects).ExtractText(result.Pages[0]);
    }

    private static MemoryStream BuildTwoLinePdf()
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
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
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

        byte[] content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 40 720 Td (Introduction) Tj ET "
            + "BT /F1 10 Tf 40 706 Td (Oncology Professional details) Tj ET");
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
