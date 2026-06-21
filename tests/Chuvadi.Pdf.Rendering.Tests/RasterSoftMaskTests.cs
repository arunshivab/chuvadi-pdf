// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.6.5.2 — Soft-mask dictionaries (ExtGState /SMask)
// PHASE: Phase 2 — item 12, ExtGState soft masks (raster path)
//
// A luminosity soft mask gates subsequent painting by the luminosity of a
// masking group rendered over a black backdrop. Here the group paints the left
// half white (luminosity 1) and leaves the right half black (luminosity 0), so a
// red fill over the whole page shows only on the left; the right stays the white
// page background.

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

public sealed class RasterSoftMaskTests
{
    [Fact]
    public void LuminosityMask_GatesPaintingByGroupLuminosity()
    {
        byte[] pdf = BuildSoftMaskPdf();

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 });
        PixelBuffer buffer = rasterizer.Rasterize(doc.Pages[0]);

        int midY = buffer.Height / 2;
        (byte lb, byte lg, byte lr, byte _) = buffer.GetPixelBgra(buffer.Width / 4, midY);
        (byte rb, byte rg, byte rr, byte _) = buffer.GetPixelBgra(buffer.Width * 3 / 4, midY);

        // Left: mask luminosity ~1 -> red shows through.
        lr.Should().BeGreaterThan(200);
        lg.Should().BeLessThan(70);
        lb.Should().BeLessThan(70);

        // Right: mask luminosity ~0 -> paint suppressed, white page shows.
        rr.Should().BeGreaterThan(220);
        rg.Should().BeGreaterThan(220);
        rb.Should().BeGreaterThan(220);
    }

    private static byte[] BuildSoftMaskPdf()
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

        // Masking group: fill the left half white over a (black) backdrop.
        byte[] groupContent = Encoding.ASCII.GetBytes("1 1 1 rg 0 0 100 100 re f");
        PdfDictionary groupDict = new PdfDictionary();
        groupDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        groupDict.Set(PdfName.Subtype, PdfName.Intern("Form"));
        groupDict.Set(PdfName.Intern("BBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(100),
        }));
        PdfDictionary groupAttrs = new PdfDictionary();
        groupAttrs.Set(PdfName.Intern("S"), PdfName.Intern("Transparency"));
        groupDict.Set(PdfName.Intern("Group"), groupAttrs);
        groupDict.Set(PdfName.Length, groupContent.Length);
        PdfStream groupStream = new PdfStream(groupDict, groupContent);

        PdfDictionary smask = new PdfDictionary();
        smask.Set(PdfName.Intern("S"), PdfName.Intern("Luminosity"));
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
