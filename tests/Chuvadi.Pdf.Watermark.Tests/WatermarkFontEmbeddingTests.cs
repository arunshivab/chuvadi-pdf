// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7 — Composite (Type0) fonts
// Regression coverage (LA-18): a text watermark with TextWatermarkOptions.
// FontData embeds the supplied TrueType face as a Type0/CIDFontType2 font and
// draws the text as Identity-H glyph IDs, so non-Latin (Indic) watermark text
// can render with the caller's own font.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Watermark.Tests;

public sealed class WatermarkFontEmbeddingTests
{
    private const string WatermarkText = "DRAFT\u00e9"; // includes non-ASCII 'é'

    [Fact]
    public void FontData_EmbedsType0CidFontWithFontFile2()
    {
        using PdfDocument outDoc = StampAndReopen();
        PdfObjectStore store = outDoc.Objects;

        PdfDictionary fonts = FontResource(store, outDoc.Pages[0].Dictionary);
        fonts.Keys.Select(k => k.Value).Should().Contain("LiberationSerif");

        PdfDictionary type0 = (store.Resolve(fonts[PdfName.Intern("LiberationSerif")]) as PdfDictionary)!;
        NameOf(store, type0, "Subtype").Should().Be("Type0");
        NameOf(store, type0, "Encoding").Should().Be("Identity-H");

        PdfArray descendants = (store.Resolve(type0[PdfName.Intern("DescendantFonts")]) as PdfArray)!;
        PdfDictionary cidFont = (store.Resolve(descendants[0]) as PdfDictionary)!;
        NameOf(store, cidFont, "Subtype").Should().Be("CIDFontType2");

        PdfDictionary descriptor = (store.Resolve(cidFont[PdfName.Intern("FontDescriptor")]) as PdfDictionary)!;
        descriptor.TryGetValue(PdfName.Intern("FontFile2"), out PdfPrimitive? _)
            .Should().BeTrue("the TrueType program must be embedded as /FontFile2");
    }

    [Fact]
    public void FontData_DrawsTextAsIdentityHGlyphIds()
    {
        byte[] ttf = LoadFixtureFont();
        TrueTypeLoader loader = new TrueTypeLoader(ttf);

        StringBuilder expected = new StringBuilder();
        foreach (char c in WatermarkText)
        {
            int gid = loader.GetGlyphIndex(c);
            if (gid < 0) { gid = 0; }
            expected.Append(((gid >> 8) & 0xFF).ToString("X2"));
            expected.Append((gid & 0xFF).ToString("X2"));
        }

        string content = WatermarkContentStream();
        content.Should().Contain($"<{expected}> Tj");
        content.Should().Contain("/LiberationSerif");
        // Every glyph used here is real (non-.notdef).
        expected.ToString().Should().NotContain("0000");
    }

    [Fact]
    public void NoFontData_UsesStandardHelvetica()
    {
        using PdfDocument outDoc = StampAndReopen(embed: false);
        PdfObjectStore store = outDoc.Objects;

        PdfDictionary fonts = FontResource(store, outDoc.Pages[0].Dictionary);
        PdfDictionary helv = (store.Resolve(fonts[PdfName.Intern("Helvetica")]) as PdfDictionary)!;
        NameOf(store, helv, "Subtype").Should().Be("Type1");
        NameOf(store, helv, "BaseFont").Should().Be("Helvetica");
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

    private static string WatermarkContentStream()
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

        return all.ToString();
    }

    private static PdfDictionary FontResource(PdfObjectStore store, PdfDictionary pageDict)
    {
        PdfDictionary resources = (store.Resolve(pageDict[PdfName.Intern("Resources")]) as PdfDictionary)!;
        return (store.Resolve(resources[PdfName.Intern("Font")]) as PdfDictionary)!;
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
