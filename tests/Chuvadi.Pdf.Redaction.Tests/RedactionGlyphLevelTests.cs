// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4.3 (text-showing operators)
// PHASE: Glyph-level redaction — fixes whole-operator over-removal
//
// Redacting a word inside a larger Tj run must remove only that word's glyphs
// and keep the neighbours (previously the whole run was dropped). The redacted
// word is gone from extraction; the surrounding words remain.

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

public sealed class RedactionGlyphLevelTests
{
    [Fact]
    public void Apply_RedactsOnlyMatchedGlyphs_KeepsNeighbours()
    {
        // "Secret amount here" in Helvetica 14 at (40, 700). "amount" spans
        // roughly user-X 84..131; the rect covers it without touching the
        // neighbours.
        using MemoryStream source = BuildTextPdf("Secret amount here");
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(85, 696, 45, 20)),
            },
        };
        Redactor.Apply(output, doc, opts);

        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        string after = new TextExtractor(result.Objects).ExtractText(result.Pages[0]);

        after.Should().Contain("Secret", "the word before the redacted span must survive");
        after.Should().Contain("here", "the word after the redacted span must survive");
        after.Should().NotContain("amount", "the matched glyphs must be removed");
    }

    [Fact]
    public void Apply_WholeRunInRegion_RemovesEverything()
    {
        // A rect covering the entire run removes all of it (no neighbours).
        using MemoryStream source = BuildTextPdf("Secret amount here");
        using PdfDocument doc = PdfDocument.Open(source, leaveOpen: true);
        using MemoryStream output = new MemoryStream();

        RedactionOptions opts = new RedactionOptions
        {
            Rectangles = new List<RedactionRect>
            {
                new RedactionRect(0, new RectangleF(30, 696, 220, 20)),
            },
        };
        Redactor.Apply(output, doc, opts);

        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        string after = new TextExtractor(result.Objects).ExtractText(result.Pages[0]);
        after.Should().NotContain("Secret");
        after.Should().NotContain("amount");
        after.Should().NotContain("here");
    }

    private static MemoryStream BuildTextPdf(string line)
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

        byte[] content = Encoding.ASCII.GetBytes($"BT /F1 14 Tf 40 700 Td ({line}) Tj ET");
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
