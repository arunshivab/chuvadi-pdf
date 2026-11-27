// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7 (composite fonts)
// PHASE: 3.36.0 (D-412) — the caller-chosen primary font threads through the
//        header/footer and text-stamp entry points into the shared mark-text
//        composer, so supplying FontData changes the drawn (shaped, embedded)
//        glyphs for the covered text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class MarkFontChoice336Tests
{
    [Fact]
    public void HeaderFooter_FontData_ChangesDrawnGlyphsAndEmbeds()
    {
        string withDefault = ApplyHeaderFooter(null);
        string withChosen = ApplyHeaderFooter(ChosenFont());

        withChosen.Should().Contain("Identity-H");
        withChosen.Should().NotBe(withDefault, "the chosen font changes the drawn glyph ids");
    }

    [Fact]
    public void TextStamper_FontData_ChangesDrawnGlyphsAndEmbeds()
    {
        string withDefault = ApplyStamp(null);
        string withChosen = ApplyStamp(ChosenFont());

        withChosen.Should().Contain("Identity-H");
        withChosen.Should().NotBe(withDefault, "the chosen font changes the drawn glyph ids");
    }

    private static string ApplyHeaderFooter(byte[]? fontData)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(OnePage()), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        HeaderFooter.Apply(output, doc, new HeaderFooterOptions
        {
            Header = new BandText(center: "Report"),
            Fit = PageContentFit.Overlay,
            FontData = fontData,
        });
        return AllObjectBytes(output.ToArray());
    }

    private static string ApplyStamp(byte[]? fontData)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(OnePage()), leaveOpen: false);
        using MemoryStream output = new MemoryStream();
        TextStamper.Apply(
            output, doc, null, "Report", StampAnchor.TopCenter, 36, 24, 12, ColorF.Black,
            filePath: null, timestamp: null, fontData: fontData);
        return AllObjectBytes(output.ToArray());
    }

    private static string AllObjectBytes(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    private static byte[] ChosenFont() =>
        File.ReadAllBytes(System.IO.Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LiberationSerif-Regular.ttf"));

    private static byte[] OnePage()
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

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(contentId));
        pageDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(400), new PdfInteger(500),
        }));

        byte[] content = Encoding.ASCII.GetBytes("1 1 1 rg 0 0 400 500 re f");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
        };

        MemoryStream ms = new MemoryStream();
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        Chuvadi.Pdf.IO.PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
