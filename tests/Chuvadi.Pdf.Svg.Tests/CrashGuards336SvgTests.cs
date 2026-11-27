// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.5 (Type 3 fonts); §11.6.5.2 (soft masks)
// PHASE: 3.36.0 (D-410 B) — the display-list builder caps recursion so a
//        Type 3 glyph that draws itself (c09) and a soft mask whose group
//        sets that same soft mask (c08) render instead of overflowing the
//        stack. These tests render the cyclic page; without the caps they do
//        not return.

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

public sealed class CrashGuards336SvgTests
{
    [Fact]
    public void SelfDrawingType3Glyph_RendersWithoutOverflow()
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(BuildSelfType3()), leaveOpen: false);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("<svg");
    }

    [Fact]
    public void CyclicSoftMask_RendersWithoutOverflow()
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(BuildCyclicSoftMask()), leaveOpen: false);

        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("<svg");
    }

    // A Type 3 font whose glyph 'a' procedure draws the same glyph again; the
    // font lists itself in its own /Resources so the procedure can reference it.
    private static byte[] BuildSelfType3()
    {
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId fontId = new PdfObjectId(5, 0);
        PdfObjectId procId = new PdfObjectId(10, 0);

        PdfDictionary charProcs = new PdfDictionary();
        charProcs.Set(PdfName.Intern("a"), new PdfReference(procId));

        PdfDictionary encoding = new PdfDictionary();
        encoding.Set(PdfName.Type, PdfName.Intern("Encoding"));
        encoding.Set(PdfName.Intern("Differences"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(65), PdfName.Intern("a"),
        }));

        PdfDictionary fontResFont = new PdfDictionary();
        fontResFont.Set(PdfName.Intern("T3"), new PdfReference(fontId));
        PdfDictionary fontResources = new PdfDictionary();
        fontResources.Set(PdfName.Intern("Font"), fontResFont);

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
        font.Set(PdfName.Intern("LastChar"), 65);
        font.Set(PdfName.Intern("Widths"), new PdfArray(new PdfPrimitive[] { new PdfInteger(100) }));
        font.Set(PdfName.Intern("Resources"), fontResources);

        byte[] proc = Encoding.ASCII.GetBytes("100 0 d1\nBT /T3 24 Tf (A) Tj ET\n");

        PdfDictionary fontResource = new PdfDictionary();
        fontResource.Set(PdfName.Intern("T3"), new PdfReference(fontId));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("Font"), fontResource);

        PdfDictionary pageDict = NewPage(pagesId, contentId, resources);
        string content = "BT /T3 24 Tf 50 50 Td (A) Tj ET\n";

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), NewCatalog(pagesId)),
            new PdfIndirectObject(pagesId, NewPages(pageId)),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentId, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content))),
            new PdfIndirectObject(fontId, font),
            new PdfIndirectObject(procId, new PdfStream(new PdfDictionary(), proc)),
        };

        return Serialize(objects);
    }

    // An ExtGState whose /SMask group is a form that sets that same ExtGState,
    // so building the mask group would recurse without the depth cap.
    private static byte[] BuildCyclicSoftMask()
    {
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId gsId = new PdfObjectId(5, 0);
        PdfObjectId formId = new PdfObjectId(6, 0);

        PdfDictionary mask = new PdfDictionary();
        mask.Set(PdfName.Type, PdfName.Intern("Mask"));
        mask.Set(PdfName.Intern("S"), PdfName.Intern("Luminosity"));
        mask.Set(PdfName.Intern("G"), new PdfReference(formId));

        PdfDictionary gs = new PdfDictionary();
        gs.Set(PdfName.Type, PdfName.Intern("ExtGState"));
        gs.Set(PdfName.Intern("SMask"), mask);

        PdfDictionary formGsDict = new PdfDictionary();
        formGsDict.Set(PdfName.Intern("GS"), new PdfReference(gsId));
        PdfDictionary formResources = new PdfDictionary();
        formResources.Set(PdfName.Intern("ExtGState"), formGsDict);

        PdfDictionary formDict = new PdfDictionary();
        formDict.Set(PdfName.Subtype, PdfName.Intern("Form"));
        formDict.Set(PdfName.Intern("BBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100),
        }));
        formDict.Set(PdfName.Intern("Resources"), formResources);
        byte[] formContent = Encoding.ASCII.GetBytes("/GS gs 1 g 0 0 100 100 re f\n");

        PdfDictionary pageGsDict = new PdfDictionary();
        pageGsDict.Set(PdfName.Intern("GS"), new PdfReference(gsId));
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("ExtGState"), pageGsDict);

        PdfDictionary pageDict = NewPage(pagesId, contentId, resources);
        string content = "/GS gs 0 g 0 0 200 200 re f\n";

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), NewCatalog(pagesId)),
            new PdfIndirectObject(pagesId, NewPages(pageId)),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentId, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content))),
            new PdfIndirectObject(gsId, gs),
            new PdfIndirectObject(formId, new PdfStream(formDict, formContent)),
        };

        return Serialize(objects);
    }

    private static PdfDictionary NewCatalog(PdfObjectId pagesId)
    {
        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));
        return catalog;
    }

    private static PdfDictionary NewPages(PdfObjectId pageId)
    {
        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);
        return pages;
    }

    private static PdfDictionary NewPage(PdfObjectId pagesId, PdfObjectId contentId, PdfDictionary resources)
    {
        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Intern("Contents"), new PdfReference(contentId));
        pageDict.Set(PdfName.Intern("Resources"), resources);
        pageDict.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200),
        }));
        return pageDict;
    }

    private static byte[] Serialize(List<PdfIndirectObject> objects)
    {
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(new PdfObjectId(1, 0)));
        using MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
