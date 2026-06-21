// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.4.5 — Axial (type 2) shadings, sh operator
// PHASE: Phase 2 — item 11, raster shading support
//
// The raster pipeline previously dropped the sh operator entirely. The builder
// now emits a ShadeOp carrying page-space gradient geometry, and the rasterizer
// paints the axial/radial gradient per pixel. These tests prove both halves:
// the builder geometry, and the actual painted colours end-to-end.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.Raster;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class ShadingRasterTests
{
    [Fact]
    public void Sh_AxialShading_EmitsShadeOpWithPageSpaceGeometry()
    {
        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("Shading"), ShadingDict(
            coords: new double[] { 0, 0, 200, 0 },
            c0: new double[] { 1, 0, 0 },
            c1: new double[] { 0, 0, 1 }));

        byte[] content = Encoding.ASCII.GetBytes("/Sh0 sh");
        PdfObjectStore store = new PdfObjectStore();
        PageDisplayList list = DisplayListBuilder.Build(content, resources, store, 200, 100);

        ShadeOp op = list.Ops.OfType<ShadeOp>().Single();
        op.IsRadial.Should().BeFalse();
        op.X0.Should().BeApproximately(0, 0.001);
        op.Y0.Should().BeApproximately(0, 0.001);
        op.X1.Should().BeApproximately(200, 0.001);
        op.Y1.Should().BeApproximately(0, 0.001);
        op.Stops.Should().HaveCount(17);
        op.Stops[0].Color.R.Should().BeApproximately(1f, 0.01f);
        op.Stops[0].Color.B.Should().BeApproximately(0f, 0.01f);
        op.Stops[16].Color.B.Should().BeApproximately(1f, 0.01f);
        op.Stops[16].Color.R.Should().BeApproximately(0f, 0.01f);
    }

    [Fact]
    public void Rasterize_AxialGradient_PaintsRedToBlueAcrossX()
    {
        byte[] pdf = BuildShadingPdf(
            coords: new double[] { 0, 0, 200, 0 },
            c0: new double[] { 1, 0, 0 },
            c1: new double[] { 0, 0, 1 });

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 });
        PixelBuffer buffer = rasterizer.Rasterize(doc.Pages[0]);

        int midY = buffer.Height / 2;
        (byte _, byte _, byte leftR, byte _) = buffer.GetPixelBgra(buffer.Width / 5, midY);
        (byte leftB, byte _, byte _, byte _) = buffer.GetPixelBgra(buffer.Width / 5, midY);
        (byte _, byte _, byte rightR, byte _) = buffer.GetPixelBgra(buffer.Width * 4 / 5, midY);
        (byte rightB, byte _, byte _, byte _) = buffer.GetPixelBgra(buffer.Width * 4 / 5, midY);

        // Left of the axis is near the C0 (red) end; right is near C1 (blue).
        leftR.Should().BeGreaterThan(180, "left edge resolves toward the red stop");
        leftB.Should().BeLessThan(80);
        rightB.Should().BeGreaterThan(180, "right edge resolves toward the blue stop");
        rightR.Should().BeLessThan(80);
    }

    private static PdfDictionary ShadingDict(double[] coords, double[] c0, double[] c1)
    {
        PdfDictionary fn = new PdfDictionary();
        fn.Set(PdfName.Intern("FunctionType"), 2);
        fn.Set(PdfName.Intern("Domain"), Nums(0, 1));
        fn.Set(PdfName.Intern("C0"), Nums(c0));
        fn.Set(PdfName.Intern("C1"), Nums(c1));
        fn.Set(PdfName.Intern("N"), 1);

        PdfDictionary shading = new PdfDictionary();
        shading.Set(PdfName.Intern("ShadingType"), 2);
        shading.Set(PdfName.Intern("ColorSpace"), PdfName.Intern("DeviceRGB"));
        shading.Set(PdfName.Intern("Coords"), Nums(coords));
        shading.Set(PdfName.Intern("Function"), fn);
        shading.Set(PdfName.Intern("Extend"), new PdfArray(new PdfPrimitive[]
        {
            PdfBoolean.True, PdfBoolean.True,
        }));

        PdfDictionary shadings = new PdfDictionary();
        shadings.Set(PdfName.Intern("Sh0"), shading);
        return shadings;
    }

    private static byte[] BuildShadingPdf(double[] coords, double[] c0, double[] c1)
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

        PdfDictionary resources = new PdfDictionary();
        resources.Set(PdfName.Intern("Shading"), ShadingDict(coords, c0, c1));

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Parent, new PdfReference(pagesId));
        page.Set(PdfName.Contents, new PdfReference(contentId));
        page.Set(PdfName.Intern("Resources"), resources);

        byte[] content = Encoding.ASCII.GetBytes("/Sh0 sh\n");
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
