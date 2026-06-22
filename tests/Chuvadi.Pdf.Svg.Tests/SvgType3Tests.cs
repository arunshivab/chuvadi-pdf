// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.5 — Type 3 fonts; §9.6.5.2 — d0/d1
// PHASE: Phase 2 — item 26, Type 3 fonts (rendering, SVG sink)
//
// Each Type 3 glyph is emitted as its glyph-space content wrapped in a transform
// group composing FontMatrix · text-scale · text matrix · CTM. A d1 (uncoloured)
// glyph must paint with the text fill colour; a d0 (coloured) glyph keeps the
// colour it sets itself.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class SvgType3Tests
{
    [Fact]
    public void Render_D1UncolouredGlyph_UsesTextColourUnderComposition()
    {
        string svg = RenderType3('A');

        // FontMatrix 0.01 * fontSize 24 = 0.24; Td (50,50) translation.
        svg.Should().Contain("matrix(0.24 0 0 0.24 50 50)");
        svg.Should().Contain("fill=\"rgb(255,0,0)\"", "a d1 glyph paints with the red text colour");
    }

    [Fact]
    public void Render_D0ColouredGlyph_KeepsOwnColour()
    {
        string svg = RenderType3('B');

        svg.Should().Contain("matrix(0.24 0 0 0.24 50 50)");
        svg.Should().Contain("fill=\"rgb(0,255,0)\"", "a d0 glyph keeps its own green colour");
    }

    private static string RenderType3(char show)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(BuildType3Pdf(show)), leaveOpen: false);
        return new SvgRenderer().RenderPage(doc, 0);
    }

    private static byte[] BuildType3Pdf(char show)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId fontId = new PdfObjectId(5, 0);
        PdfObjectId procAId = new PdfObjectId(10, 0);
        PdfObjectId procBId = new PdfObjectId(11, 0);

        byte[] procA = Encoding.ASCII.GetBytes("100 0 d1\n0 0 100 100 re\nf\n");
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

        string content = "1 0 0 rg\nBT\n/T3 24 Tf\n50 50 Td\n(" + show + ") Tj\nET\n";
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
            new PdfIndirectObject(contentId, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content))),
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
