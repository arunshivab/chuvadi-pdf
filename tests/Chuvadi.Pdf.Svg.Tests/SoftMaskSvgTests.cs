// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.6.5.2 — Soft-mask dictionaries (ExtGState /SMask)
// PHASE: Phase 2 — item 12, ExtGState soft masks (SVG path)
//
// A fill painted under a luminosity /SMask must be wrapped in a group that
// references an emitted SVG <mask> built from the masking group. This is a
// structural check (the mask def and reference are present); visual fidelity of
// the masking is verified separately.

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

public sealed class SoftMaskSvgTests
{
    [Fact]
    public void RenderPage_FillUnderLuminosityMask_EmitsMaskDefAndReference()
    {
        byte[] pdf = BuildSoftMaskPdf(luminosity: true);

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("<mask id=\"smask0\"", "a <mask> def is emitted for the soft mask");
        svg.Should().Contain("maskUnits=\"userSpaceOnUse\"");
        svg.Should().Contain("mask=\"url(#smask0)\"", "the masked fill references the mask");
        svg.Should().NotContain("mask-type:alpha", "a luminosity mask uses the default luminance");
    }

    [Fact]
    public void RenderPage_AlphaMask_SetsMaskTypeAlpha()
    {
        byte[] pdf = BuildSoftMaskPdf(luminosity: false);

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("mask-type:alpha", "an alpha mask sets mask-type:alpha");
        svg.Should().Contain("mask=\"url(#smask0)\"");
    }

    private static byte[] BuildSoftMaskPdf(bool luminosity)
    {
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId contentId = new PdfObjectId(4, 0);
        PdfObjectId groupId = new PdfObjectId(5, 0);

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(pageId) }));
        pages.Set(PdfName.Count, 1);
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(100),
        }));

        byte[] groupContent = Encoding.ASCII.GetBytes("1 1 1 rg 0 0 100 100 re f");
        PdfDictionary groupDict = new PdfDictionary();
        groupDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        groupDict.Set(PdfName.Subtype, PdfName.Intern("Form"));
        groupDict.Set(PdfName.Intern("BBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(100),
        }));
        groupDict.Set(PdfName.Length, groupContent.Length);
        PdfStream groupStream = new PdfStream(groupDict, groupContent);

        PdfDictionary smask = new PdfDictionary();
        smask.Set(PdfName.Intern("S"), PdfName.Intern(luminosity ? "Luminosity" : "Alpha"));
        smask.Set(PdfName.Intern("G"), new PdfReference(groupId));
        PdfDictionary gsDict = new PdfDictionary();
        gsDict.Set(PdfName.Intern("SMask"), smask);
        PdfDictionary extGStates = new PdfDictionary();
        extGStates.Set(PdfName.Intern("GS0"), gsDict);
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("ExtGState"), extGStates);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.Contents, new PdfReference(contentId));
        page.Set(PdfName.Intern("Resources"), resources);

        byte[] content = Encoding.ASCII.GetBytes("/GS0 gs 1 0 0 rg 0 0 200 100 re f\n");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
            new PdfIndirectObject(groupId, groupStream),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
