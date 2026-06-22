// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.5 — Type 3 fonts; §9.6.5.2 — d0/d1
// PHASE: Phase 2 — item 26, Type 3 fonts (rendering, raster sink)
//
// A Type 3 glyph is a content stream drawn in glyph space and mapped to text
// space by the FontMatrix. These tests render a glyph that fills its glyph-space
// box and check the painted pixels: a d1 (uncoloured) glyph must take the text
// fill colour, while a d0 (coloured) glyph keeps the colour it sets itself.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class RasterType3Tests
{
    [Fact]
    public void Render_D1UncolouredGlyph_PaintsInTextColour()
    {
        // Text fill is red; the d1 glyph fills its box and must come out red.
        byte[] pdf = BuildType3Pdf(showCode: 'A');
        PixelBuffer buffer = Rasterize(pdf);

        (byte B, byte G, byte R, byte A) inside = buffer.GetPixelBgra(60, 138);
        inside.R.Should().BeGreaterThan(200);
        inside.G.Should().BeLessThan(60);
        inside.B.Should().BeLessThan(60);

        (byte B, byte G, byte R, byte A) outside = buffer.GetPixelBgra(10, 10);
        outside.R.Should().BeGreaterThan(200);
        outside.G.Should().BeGreaterThan(200);
        outside.B.Should().BeGreaterThan(200);
    }

    [Fact]
    public void Render_D0ColouredGlyph_KeepsItsOwnColour()
    {
        // Text fill is red, but the d0 glyph sets green — green must win.
        byte[] pdf = BuildType3Pdf(showCode: 'B');
        PixelBuffer buffer = Rasterize(pdf);

        (byte B, byte G, byte R, byte A) inside = buffer.GetPixelBgra(60, 138);
        inside.G.Should().BeGreaterThan(200);
        inside.R.Should().BeLessThan(60);
        inside.B.Should().BeLessThan(60);
    }

    private static PixelBuffer Rasterize(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        return new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 }).Rasterize(doc.Pages[0]);
    }

    private static byte[] BuildType3Pdf(char showCode)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId fontId = new PdfObjectId(5, 0);
        PdfObjectId procAId = new PdfObjectId(10, 0);
        PdfObjectId procBId = new PdfObjectId(11, 0);

        // d1 uncoloured glyph 'a': fills the glyph-space box, no colour ops.
        byte[] procA = Encoding.ASCII.GetBytes("100 0 d1\n0 0 100 100 re\nf\n");
        // d0 coloured glyph 'b': sets green, fills the box.
        byte[] procB = Encoding.ASCII.GetBytes("100 0 d0\n0 1 0 rg\n0 0 100 100 re\nf\n");

        PdfDictionary charProcs = new PdfDictionary();
        charProcs.Set(PdfName.Intern("a"), new PdfReference(procAId));
        charProcs.Set(PdfName.Intern("b"), new PdfReference(procBId));

        PdfDictionary encoding = new PdfDictionary();
        encoding.Set(PdfName.Type, PdfName.Intern("Encoding"));
        encoding.Set(PdfName.Intern("Differences"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(65), PdfName.Intern("a"), new PdfInteger(66), PdfName.Intern("b"),
        }));

        PdfDictionary font = new PdfDictionary();
        font.Set(PdfName.Type, PdfName.Intern("Font"));
        font.Set(PdfName.Subtype, PdfName.Intern("Type3"));
        font.Set(PdfName.Intern("FontBBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100),
        }));
        font.Set(PdfName.Intern("FontMatrix"), new PdfArray(new PdfPrimitive[]
        {
            new PdfReal(0.01), new PdfInteger(0), new PdfInteger(0),
            new PdfReal(0.01), new PdfInteger(0), new PdfInteger(0),
        }));
        font.Set(PdfName.Intern("CharProcs"), charProcs);
        font.Set(PdfName.Intern("Encoding"), encoding);
        font.Set(PdfName.Intern("FirstChar"), 65);
        font.Set(PdfName.Intern("LastChar"), 66);
        font.Set(PdfName.Intern("Widths"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(100), new PdfInteger(100),
        }));
        font.Set(PdfName.Intern("Resources"), new PdfDictionary());

        PdfDictionary fontResource = new PdfDictionary();
        fontResource.Set(PdfName.Intern("T3"), new PdfReference(fontId));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("Font"), fontResource);

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Intern("Contents"), new PdfReference(contentId));
        pageDict.Set(PdfName.Intern("Resources"), resources);
        pageDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200),
        }));

        string content = "1 0 0 rg\nBT\n/T3 24 Tf\n50 50 Td\n(" + showCode + ") Tj\nET\n";
        byte[] contentBytes = Encoding.ASCII.GetBytes(content);
        PdfDictionary contentDict = new PdfDictionary();

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, contentBytes)),
            new PdfIndirectObject(fontId, font),
            new PdfIndirectObject(procAId, new PdfStream(new PdfDictionary(), procA)),
            new PdfIndirectObject(procBId, new PdfStream(new PdfDictionary(), procB)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
