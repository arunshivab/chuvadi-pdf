// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 (text objects), §8.11 (optional content)
//
// Verifies TextRun.Layers / PageDisplayList.ExtractTextRuns: text shown inside
// an /OC marked-content section carries its OCG layer, text outside any group
// carries none, and the extension method matches TextRunExtractor.Extract.

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

public sealed class TextRunLayersTests
{
    private const string Content =
        "/OC /MC0 BDC\nBT\n/F1 24 Tf\n100 200 Td\n(Wall A) Tj\nET\nEMC\n" +
        "BT\n/F1 24 Tf\n100 100 Td\n(NoLayer) Tj\nET";

    [Fact]
    public void TextInsideOptionalContent_CarriesLayer()
    {
        using PdfDocument doc = BuildTextPage(Content, "Wall");

        IReadOnlyList<TextRun> runs = DisplayListBuilder.Build(doc, 0).ExtractTextRuns();

        TextRun layered = runs.Single(r => r.Unicode == "Wall A");
        layered.Layers.Should().Equal("Wall");
    }

    [Fact]
    public void TextOutsideOptionalContent_HasNoLayers()
    {
        using PdfDocument doc = BuildTextPage(Content, "Wall");

        IReadOnlyList<TextRun> runs = DisplayListBuilder.Build(doc, 0).ExtractTextRuns();

        TextRun plain = runs.Single(r => r.Unicode == "NoLayer");
        plain.Layers.Should().BeEmpty();
    }

    [Fact]
    public void ExtractTextRuns_Extension_MatchesExtractor()
    {
        using PdfDocument doc = BuildTextPage(Content, "Wall");
        PageDisplayList list = DisplayListBuilder.Build(doc, 0);

        IReadOnlyList<TextRun> viaExtension = list.ExtractTextRuns();
        IReadOnlyList<TextRun> viaExtractor = TextRunExtractor.Extract(list);

        viaExtension.Select(r => r.Unicode).Should().Equal(viaExtractor.Select(r => r.Unicode));
    }

    // One-page PDF: a Helvetica text run inside /OC /MC0 (mapped to an OCG with
    // the given name) plus a second run outside any optional content.
    private static PdfDocument BuildTextPage(string content, string ocgName)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId ocgId = new PdfObjectId(5, 0);
        PdfObjectId fontId = new PdfObjectId(6, 0);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        PdfDictionary ocg = new PdfDictionary();
        ocg.Set(PdfName.Type, PdfName.Intern("OCG"));
        ocg.Set(PdfName.Intern("Name"), new PdfString(Encoding.Latin1.GetBytes(ocgName)));
        objects.Add(new PdfIndirectObject(ocgId, ocg));

        PdfDictionary font = new PdfDictionary();
        font.Set(PdfName.Type, PdfName.Intern("Font"));
        font.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        font.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Helvetica"));
        objects.Add(new PdfIndirectObject(fontId, font));

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        PdfDictionary ocProps = new PdfDictionary();
        ocProps.Set(PdfName.Intern("OCGs"), new PdfArray(new PdfPrimitive[] { new PdfReference(ocgId) }));
        PdfDictionary cfg = new PdfDictionary();
        cfg.Set(PdfName.Intern("BaseState"), PdfName.Intern("ON"));
        ocProps.Set(PdfName.Intern("D"), cfg);
        catalog.Set(PdfName.Intern("OCProperties"), ocProps);

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Intern("Pages"));
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(400), new PdfInteger(400),
        }));

        PdfDictionary resources = new PdfDictionary();
        PdfDictionary props = new PdfDictionary();
        props.Set(PdfName.Intern("MC0"), new PdfReference(ocgId));
        resources.Set(PdfName.Intern("Properties"), props);
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
