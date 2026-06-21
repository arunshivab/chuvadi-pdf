// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.3.5 — Blend modes (ExtGState /BM)
// PHASE: Phase 2 — item 12, ExtGState blend modes (SVG path)
//
// A fill painted while the graphics state carries a /BM blend mode must be
// emitted inside a group whose CSS mix-blend-mode matches, so the SVG composites
// against its backdrop the way the PDF does.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class BlendModeSvgTests
{
    [Fact]
    public void RenderPage_FillUnderMultiplyBlendMode_EmitsMixBlendMode()
    {
        byte[] pdf = BuildBlendPdf("Multiply", "/GS0 gs 1 0 0 rg 100 600 200 100 re f");

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("mix-blend-mode:multiply");
    }

    [Fact]
    public void RenderPage_FillWithoutBlendMode_EmitsNoMixBlend()
    {
        byte[] pdf = BuildBlendPdf("Normal", "/GS0 gs 1 0 0 rg 100 600 200 100 re f");

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().NotContain("mix-blend-mode");
    }

    private static byte[] BuildBlendPdf(string blendName, string body)
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
        pages.Set(PdfName.MediaBox, new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792),
        }));

        PdfDictionary gsDict = new PdfDictionary();
        gsDict.Set(PdfName.Intern("BM"), PdfName.Intern(blendName));
        PdfDictionary extGStates = new PdfDictionary();
        extGStates.Set(PdfName.Intern("GS0"), gsDict);
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("ExtGState"), extGStates);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.Contents, new PdfReference(contentId));
        page.Set(PdfName.Intern("Resources"), resources);

        byte[] content = System.Text.Encoding.ASCII.GetBytes(body + "\n");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, page),
            new PdfIndirectObject(contentId, new PdfStream(contentDict, content)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
