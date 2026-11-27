// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7.4.3 — CIDFont /W and /DW
// PHASE: 3.36.0 (D-410 C, item 76) — a CID font's /W array and /DW default,
//        and the elements inside /W, may be indirect references (CAD
//        producers write them so). The width builder resolves them; before,
//        an indirect /W or /DW was ignored and every CID fell back to 1000,
//        making Find boxes and redaction extents wrong.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.DisplayList.Tests;

public sealed class FontWidthsIndirect336Tests
{
    [Fact]
    public void IndirectWAndDw_AreResolved()
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(BuildType0WithIndirectWidths()), leaveOpen: false);
        PdfDictionary type0 = (PdfDictionary)doc.Objects.Resolve(new PdfReference(new PdfObjectId(5, 0)));

        FontWidths widths = FontWidths.FromDictionary(type0, doc.Objects);

        widths.GetWidth(10).Should().Be(700);          // /W inner element (direct)
        widths.GetWidth(11).Should().Be(800);          // /W inner element (indirect)
        widths.GetWidth(99).Should().Be(500);          // /DW default (indirect)
    }

    // A Type 0 font whose descendant CIDFont has /DW as an indirect integer and
    // /W as an indirect array one of whose widths is itself an indirect integer.
    private static byte[] BuildType0WithIndirectWidths()
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId cidFontId = new PdfObjectId(4, 0);
        PdfObjectId type0Id = new PdfObjectId(5, 0);
        PdfObjectId dwId = new PdfObjectId(6, 0);
        PdfObjectId wId = new PdfObjectId(7, 0);
        PdfObjectId innerWidthId = new PdfObjectId(8, 0);

        PdfArray innerW = new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(700), new PdfReference(innerWidthId),
        });
        PdfArray wArray = new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(10), innerW,
        });

        PdfDictionary cidFont = new PdfDictionary();
        cidFont.Set(PdfName.Type, PdfName.Intern("Font"));
        cidFont.Set(PdfName.Subtype, PdfName.Intern("CIDFontType2"));
        cidFont.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Test"));
        cidFont.Set(PdfName.Intern("DW"), new PdfReference(dwId));
        cidFont.Set(PdfName.Intern("W"), new PdfReference(wId));

        PdfDictionary type0 = new PdfDictionary();
        type0.Set(PdfName.Type, PdfName.Intern("Font"));
        type0.Set(PdfName.Subtype, PdfName.Intern("Type0"));
        type0.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Test"));
        type0.Set(PdfName.Intern("Encoding"), PdfName.Intern("Identity-H"));
        type0.Set(PdfName.Intern("DescendantFonts"), new PdfArray(new PdfPrimitive[] { new PdfReference(cidFontId) }));

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200),
        }));

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
            new PdfIndirectObject(cidFontId, cidFont),
            new PdfIndirectObject(type0Id, type0),
            new PdfIndirectObject(dwId, new PdfInteger(500)),
            new PdfIndirectObject(wId, wArray),
            new PdfIndirectObject(innerWidthId, new PdfInteger(800)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));
        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
