// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.3.5 — Blend modes (ExtGState /BM)
// PHASE: Phase 2 — item 12, ExtGState blend modes (raster path)
//
// A fill painted under a /BM blend mode must composite against the existing
// backdrop with the separable blend function, not plain source-over. Multiply
// of red over mid-grey yields (grey, 0, 0); Screen of red over mid-grey lightens
// the red channel to full while leaving the others at grey.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class RasterBlendModeTests
{
    [Fact]
    public void Multiply_RedOverMidGrey_YieldsGreyRedChannelOnly()
    {
        // Backdrop grey 0.5, then red multiplied over it.
        byte[] pdf = BuildPdf(
            "Multiply",
            "0.5 g 0 0 200 100 re f /GS0 gs 1 0 0 rg 0 0 200 100 re f");

        (byte b, byte g, byte r) = CenterPixel(pdf);

        r.Should().BeInRange(118, 138, "Multiply(0.5, 1.0) ~= 0.5 -> ~128");
        g.Should().BeLessThan(12, "Multiply(0.5, 0.0) = 0");
        b.Should().BeLessThan(12, "Multiply(0.5, 0.0) = 0");
    }

    [Fact]
    public void Screen_RedOverMidGrey_LightensRedChannelToFull()
    {
        byte[] pdf = BuildPdf(
            "Screen",
            "0.5 g 0 0 200 100 re f /GS0 gs 1 0 0 rg 0 0 200 100 re f");

        (byte b, byte g, byte r) = CenterPixel(pdf);

        r.Should().BeGreaterThan(243, "Screen(0.5, 1.0) = 1.0 -> 255");
        g.Should().BeInRange(118, 138, "Screen(0.5, 0.0) = 0.5 -> ~128");
        b.Should().BeInRange(118, 138, "Screen(0.5, 0.0) = 0.5 -> ~128");
    }

    private static (byte B, byte G, byte R) CenterPixel(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 });
        PixelBuffer buffer = rasterizer.Rasterize(doc.Pages[0]);
        (byte b, byte g, byte r, byte _) = buffer.GetPixelBgra(buffer.Width / 2, buffer.Height / 2);
        return (b, g, r);
    }

    private static byte[] BuildPdf(string blendName, string body)
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
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(100),
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
