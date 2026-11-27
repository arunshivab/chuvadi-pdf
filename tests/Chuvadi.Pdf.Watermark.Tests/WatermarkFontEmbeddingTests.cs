// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7 — Composite (Type0) fonts
// Coverage (LA-18, updated for 3.36.0 / D-412): a text watermark is shaped and
// subset-embedded through the shared mark-text composer. With FontData the
// supplied face is embedded as a Type0/CIDFontType2 (/FontFile2) font; without
// it the LiPi Sans face is embedded the same way — the watermark is always an
// embedded composite font, never the former standard Helvetica. The drawn text
// uses Identity-H glyph shows.

using System;
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

namespace Chuvadi.Pdf.Watermark.Tests;

public sealed class WatermarkFontEmbeddingTests
{
    private const string WatermarkText = "DRAFTé"; // includes non-ASCII 'é'

    [Fact]
    public void FontData_EmbedsType0CidFontWithFontFile2()
    {
        using PdfDocument outDoc = StampAndReopen();
        PdfObjectStore store = outDoc.Objects;

        PdfDictionary type0 = SingleFont(store, outDoc.Pages[0].Dictionary);
        NameOf(store, type0, "Subtype").Should().Be("Type0");
        NameOf(store, type0, "Encoding").Should().Be("Identity-H");

        PdfArray descendants = (store.Resolve(type0[PdfName.Intern("DescendantFonts")]) as PdfArray)!;
        PdfDictionary cidFont = (store.Resolve(descendants[0]) as PdfDictionary)!;
        NameOf(store, cidFont, "Subtype").Should().Be("CIDFontType2");

        PdfDictionary descriptor = (store.Resolve(cidFont[PdfName.Intern("FontDescriptor")]) as PdfDictionary)!;
        descriptor.TryGetValue(PdfName.Intern("FontFile2"), out PdfPrimitive? _)
            .Should().BeTrue("the supplied TrueType program must be embedded as /FontFile2");
    }

    [Fact]
    public void FontData_DrawsIdentityHGlyphShowsFromTheEmbeddedFont()
    {
        (string content, string resourceName) = WatermarkContentStream();

        // The text is shaped, so it is drawn as per-glyph Identity-H hex shows
        // against the embedded face's resource, not a single literal string.
        content.Should().Contain("/" + resourceName);
        content.Should().MatchRegex("<[0-9A-Fa-f]{4,}> Tj");
        content.Should().NotContain("(DRAFT");
    }

    [Fact]
    public void NoFontData_EmbedsLipiType0FontNotStandardHelvetica()
    {
        using PdfDocument outDoc = StampAndReopen(embed: false);
        PdfObjectStore store = outDoc.Objects;

        PdfDictionary type0 = SingleFont(store, outDoc.Pages[0].Dictionary);
        NameOf(store, type0, "Subtype").Should().Be("Type0", "marks are always embedded as of 3.36.0");
        NameOf(store, type0, "Encoding").Should().Be("Identity-H");

        PdfArray descendants = (store.Resolve(type0[PdfName.Intern("DescendantFonts")]) as PdfArray)!;
        PdfDictionary cidFont = (store.Resolve(descendants[0]) as PdfDictionary)!;
        NameOf(store, cidFont, "Subtype").Should().Be("CIDFontType2");
        NameOf(store, type0, "BaseFont").Should().NotBe("Helvetica");
    }

    private static PdfDocument StampAndReopen(bool embed = true)
    {
        MemoryStream output = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Open(BuildPlainPdf(), leaveOpen: true))
        {
            TextWatermarkOptions options = embed
                ? new TextWatermarkOptions(WatermarkText)
                {
                    FontName = "LiberationSerif",
                    FontData = LoadFixtureFont(),
                    FontSize = 40,
                }
                : new TextWatermarkOptions(WatermarkText) { FontSize = 40 };
            WatermarkStamper.ApplyText(output, doc, options);
        }

        output.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(output, leaveOpen: true);
    }

    private static (string Content, string ResourceName) WatermarkContentStream()
    {
        MemoryStream output = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Open(BuildPlainPdf(), leaveOpen: true))
        {
            WatermarkStamper.ApplyText(output, doc, new TextWatermarkOptions(WatermarkText)
            {
                FontName = "LiberationSerif",
                FontData = LoadFixtureFont(),
                FontSize = 40,
            });
        }

        using PdfDocument outDoc = PdfDocument.Open(output, leaveOpen: true);
        PdfObjectStore store = outDoc.Objects;
        string resourceName = FontResource(store, outDoc.Pages[0].Dictionary)
            .Keys.Select(k => k.Value).First();

        PdfPrimitive contents = store.Resolve(outDoc.Pages[0].Dictionary[PdfName.Intern("Contents")]);
        List<PdfPrimitive> streamRefs = contents is PdfArray array
            ? array.ToList()
            : new List<PdfPrimitive> { outDoc.Pages[0].Dictionary[PdfName.Intern("Contents")] };

        StringBuilder all = new StringBuilder();
        foreach (PdfPrimitive streamRef in streamRefs)
        {
            if (store.Resolve(streamRef) is PdfStream s)
            {
                all.Append(Encoding.Latin1.GetString(s.RawBytes));
                all.Append('\n');
            }
        }

        return (all.ToString(), resourceName);
    }

    private static PdfDictionary FontResource(PdfObjectStore store, PdfDictionary pageDict)
    {
        PdfDictionary resources = (store.Resolve(pageDict[PdfName.Intern("Resources")]) as PdfDictionary)!;
        return (store.Resolve(resources[PdfName.Intern("Font")]) as PdfDictionary)!;
    }

    // The composer assigns its own resource names, so the single embedded mark
    // font is found by resolving the one entry in the page's /Font dictionary.
    private static PdfDictionary SingleFont(PdfObjectStore store, PdfDictionary pageDict)
    {
        PdfDictionary fonts = FontResource(store, pageDict);
        PdfName key = fonts.Keys.First();
        return (store.Resolve(fonts[key]) as PdfDictionary)!;
    }

    private static string NameOf(PdfObjectStore store, PdfDictionary dict, string key)
    {
        return store.Resolve(dict[PdfName.Intern(key)]) is PdfName name ? name.Value : string.Empty;
    }

    private static byte[] LoadFixtureFont() =>
        File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LiberationSerif-Regular.ttf"));

    private static MemoryStream BuildPlainPdf()
    {
        PdfObjectId catId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Intern("Pages"));
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Intern("Page"));
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.Contents, new PdfReference(contentId));

        byte[] contentBytes = Encoding.ASCII.GetBytes("0 0 1 rg 50 50 100 100 re f");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, contentBytes.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, contentBytes)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }
}
