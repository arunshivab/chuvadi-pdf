// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4.4 (text space), §9.4.2 (Td/Tm)
//
// Verifies TextRun.BoundingBox for rotated text: a 90-degree-rotated run has a
// non-zero width (the earlier advance-as-X assumption collapsed it to zero),
// and a horizontal run remains wider than tall.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.DisplayList.Tests;

public sealed class TextRunBoundsTests
{
    [Fact]
    public void RotatedText_HasNonZeroWidthAndIsTallerThanWide()
    {
        // Tm "0 1 -1 0 200 100" rotates text 90 degrees (runs upward).
        using PdfDocument doc = BuildTextPage(
            "BT\n/F1 24 Tf\n0 1 -1 0 200 100 Tm\n(Vertical) Tj\nET");

        TextRun run = DisplayListBuilder.Build(doc, 0).ExtractTextRuns().Single();

        run.BoundingBox.Width.Should().BeGreaterThan(0);
        run.BoundingBox.Height.Should().BeGreaterThan(0);
        run.BoundingBox.Height.Should().BeGreaterThan(run.BoundingBox.Width);
    }

    [Fact]
    public void HorizontalText_IsWiderThanTall()
    {
        using PdfDocument doc = BuildTextPage(
            "BT\n/F1 24 Tf\n1 0 0 1 100 200 Tm\n(Horizontal) Tj\nET");

        TextRun run = DisplayListBuilder.Build(doc, 0).ExtractTextRuns().Single();

        run.BoundingBox.Width.Should().BeGreaterThan(0);
        run.BoundingBox.Height.Should().BeGreaterThan(0);
        run.BoundingBox.Width.Should().BeGreaterThan(run.BoundingBox.Height);
    }

    private static PdfDocument BuildTextPage(string content)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId fontId = new PdfObjectId(5, 0);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        PdfDictionary font = new PdfDictionary();
        font.Set(PdfName.Type, PdfName.Intern("Font"));
        font.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        font.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Helvetica"));
        objects.Add(new PdfIndirectObject(fontId, font));

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Intern("Pages"));
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(400), new PdfInteger(400),
        }));

        PdfDictionary resources = new PdfDictionary();
        PdfDictionary fontRes = new PdfDictionary();
        fontRes.Set(PdfName.Intern("F1"), new PdfReference(fontId));
        resources.Set(PdfName.Intern("Font"), fontRes);

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Intern("Page"));
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(contentId));
        pageDict.Set(PdfName.Intern("Resources"), resources);

        byte[] contentBytes = Encoding.ASCII.GetBytes(content);
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, contentBytes.Length);

        objects.Insert(0, new PdfIndirectObject(catalogId, catalog));
        objects.Insert(1, new PdfIndirectObject(pagesId, pages));
        objects.Insert(2, new PdfIndirectObject(pageId, pageDict));
        objects.Insert(3, new PdfIndirectObject(contentId, new PdfStream(contentDict, contentBytes)));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }
}
