// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.6.6.4 (Separation), §8.6.3 (CIE-based)
// PHASE: Phase 2 — item 14, SVG honours non-device colour spaces
//
// Before this change the SVG display-list builder ignored cs / scn, so any fill
// painted in a Separation, DeviceN, Indexed, or ICCBased space was dropped or
// fell back to black. The builder now resolves the space against the page
// resources and converts the tint through the shared colour-space model, so the
// painted colour appears in the SVG output.

using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Svg.Tests;

public sealed class NonDeviceColorSpaceSvgTests
{
    [Fact]
    public void RenderPage_SeparationFill_EmitsTintTransformedColour()
    {
        // Separation -> DeviceRGB tint: white at 0 ink, red at full ink.
        PdfDictionary tint = new PdfDictionary();
        tint.Set(PdfName.Intern("FunctionType"), 2);
        tint.Set(PdfName.Intern("Domain"), Nums(0, 1));
        tint.Set(PdfName.Intern("C0"), Nums(1, 1, 1));
        tint.Set(PdfName.Intern("C1"), Nums(1, 0, 0));
        tint.Set(PdfName.Intern("N"), 1);

        PdfArray separation = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("Separation"),
            PdfName.Intern("Spot"),
            PdfName.Intern("DeviceRGB"),
            tint,
        });

        byte[] pdf = BuildPdfWithColorSpaceFill(
            "CS0", separation, "/CS0 cs 1 scn 100 600 200 100 re f");

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("rgb(255,0,0)", "full Separation ink resolves to red via the tint transform");
    }

    [Fact]
    public void RenderPage_IndexedFill_EmitsPaletteColour()
    {
        // Indexed over DeviceRGB; index 1 is the blue palette entry.
        PdfArray indexed = new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("Indexed"),
            PdfName.Intern("DeviceRGB"),
            new PdfInteger(1),
            new PdfString(new byte[] { 255, 0, 0, 0, 0, 255 }),
        });

        byte[] pdf = BuildPdfWithColorSpaceFill(
            "CS0", indexed, "/CS0 cs 1 scn 100 600 200 100 re f");

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        string svg = new SvgRenderer().RenderPage(doc, 0);

        svg.Should().Contain("rgb(0,0,255)", "palette index 1 is the blue entry");
    }

    private static byte[] BuildPdfWithColorSpaceFill(
        string csName, PdfArray colorSpace, string body)
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

        PdfDictionary colorSpaces = new PdfDictionary();
        colorSpaces.Set(PdfName.Intern(csName), colorSpace);
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("ColorSpace"), colorSpaces);

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

    private static PdfArray Nums(params double[] values)
    {
        PdfPrimitive[] items = new PdfPrimitive[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            items[i] = new PdfReal(values[i]);
        }

        return new PdfArray(items);
    }
}
