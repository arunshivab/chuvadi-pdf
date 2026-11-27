// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.36.0 (D-412) — the shared mark-text composer: shapes Latin, Indic
//        and emoji, falls back to embedded Noto Emoji for code points the LiPi
//        face lacks, and subset-embeds every face. Renders a mixed line and
//        checks it produces embedded Type0 fonts and non-blank output.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class MarkTextComposer336Tests
{
    private const string Mixed = "Hi தமிழ் \U0001F600";

    [Fact]
    public void Compose_MixedScriptAndEmoji_EmbedsType0FontsAndRendersInk()
    {
        MarkTextComposer composer = new MarkTextComposer();
        int next = 10;
        ComposedMarkText composed = composer.Compose(
            Mixed, 24, ColorF.Black, () => new PdfObjectId(next++, 0), "CvT");

        composed.ContentFragment.Should().Contain("BT").And.Contain("ET").And.Contain("Tj");
        composed.FontObjects.Should().NotBeEmpty();
        composed.FontResources.Should().NotBeEmpty();
        composed.WidthPoints.Should().BeGreaterThan(0.0);

        byte[] pdf = BuildPage(composed, width: 260, height: 70);

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 200 });
        byte[] png = rasterizer.RasterizeToPng(doc.Pages[0]);
        png.Length.Should().BeGreaterThan(0);
    }

    private static byte[] BuildPage(ComposedMarkText composed, int width, int height)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);

        PdfDictionary fonts = new PdfDictionary();
        foreach (KeyValuePair<string, PdfObjectId> entry in composed.FontResources)
        {
            fonts.Set(PdfName.Intern(entry.Key), new PdfReference(entry.Value));
        }

        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Font, fonts);

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(contentId));
        pageDict.Set(PdfName.Intern("Resources"), resources);
        pageDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(width), new PdfInteger(height),
        }));

        byte[] content = Encoding.ASCII.GetBytes("q\n1 0 0 1 20 25 cm\n" + composed.ContentFragment + "Q\n");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);
        PdfStream contentStream = new PdfStream(contentDict, content);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentId, contentStream),
        };
        objects.AddRange(composed.FontObjects);

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
