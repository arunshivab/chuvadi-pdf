// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-240 R2) — the rasterizer's internal option copies keep
//        AnnotationMode (and the 3.32.0 layer ids) on every path.

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.DisplayList;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class OptionsCopies3320Tests
{
    private static readonly Rect Region = new Rect(0, 0, 200, 200);

    [Fact]
    public void SuperSample_KeepsPrintMode()
    {
        using PdfDocument doc = Open(Fixture.Build(withAnnotation: true, withLayer: false));
        RenderOptions options = new RenderOptions { Dpi = 72, SuperSample = 2, AnnotationMode = AnnotationMode.Print };

        PixelBuffer buffer = new PageRasterizer(doc.Objects, options).Rasterize(doc.Pages[0]);

        Fixture.IsWhite(buffer.GetPixelBgra(100, 100)).Should().BeTrue("the annotation has no Print flag");
    }

    [Fact]
    public void SuperSample_ViewMode_StillDrawsTheAnnotation()
    {
        using PdfDocument doc = Open(Fixture.Build(withAnnotation: true, withLayer: false));
        RenderOptions options = new RenderOptions { Dpi = 72, SuperSample = 2 };

        PixelBuffer buffer = new PageRasterizer(doc.Objects, options).Rasterize(doc.Pages[0]);

        Fixture.IsRed(buffer.GetPixelBgra(100, 100)).Should().BeTrue();
    }

    [Fact]
    public void RenderRegion_KeepsPrintMode()
    {
        using PdfDocument doc = Open(Fixture.Build(withAnnotation: true, withLayer: false));
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { AnnotationMode = AnnotationMode.Print });

        PixelBuffer print = rasterizer.RenderRegion(doc.Pages[0], Region, 72);
        PixelBuffer view = new PageRasterizer(doc.Objects, new RenderOptions()).RenderRegion(doc.Pages[0], Region, 72);

        Fixture.IsWhite(print.GetPixelBgra(100, 100)).Should().BeTrue();
        Fixture.IsRed(view.GetPixelBgra(100, 100)).Should().BeTrue();
    }

    [Fact]
    public void RenderRegion_KeepsLayerOverridesById()
    {
        using PdfDocument doc = Open(Fixture.Build(withAnnotation: false, withLayer: true));
        RenderOptions options = new RenderOptions { LayerVisibilityById = new Dictionary<int, bool> { [Fixture.LayerObject] = false } };

        PixelBuffer hidden = new PageRasterizer(doc.Objects, options).RenderRegion(doc.Pages[0], Region, 72);
        PixelBuffer shown = new PageRasterizer(doc.Objects, new RenderOptions()).RenderRegion(doc.Pages[0], Region, 72);

        Fixture.IsWhite(hidden.GetPixelBgra(25, 125)).Should().BeTrue("the layer is switched off by its id");
        Fixture.IsGreen(shown.GetPixelBgra(25, 125)).Should().BeTrue();
    }

    private static PdfDocument Open(byte[] pdf) => PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
}

/// <summary>
/// A 200 by 200 page with a small blue square at the origin, optionally a Stamp
/// with no flags drawing red over 50..150, and optionally a layer (object 5)
/// drawing green over x 0..50, y 50..100 (y from the bottom).
/// </summary>
internal static class Fixture
{
    internal const int LayerObject = 5;

    internal static bool IsRed((byte B, byte G, byte R, byte A) p) => p.R > 200 && p.G < 60 && p.B < 60;

    internal static bool IsGreen((byte B, byte G, byte R, byte A) p) => p.G > 100 && p.R < 60 && p.B < 60;

    internal static bool IsWhite((byte B, byte G, byte R, byte A) p) => p.R > 240 && p.G > 240 && p.B > 240;

    internal static byte[] Build(bool withAnnotation, bool withLayer)
    {
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
        StringBuilder content = new StringBuilder("0 0 1 rg 0 0 10 10 re f\n");
        PdfDictionary resources = new PdfDictionary();
        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(2, 0));

        if (withLayer)
        {
            PdfDictionary ocg = new PdfDictionary();
            ocg.Set(PdfName.Type, PdfName.Intern("OCG"));
            ocg.Set(PdfName.Intern("Name"), new PdfString("Grid"));
            objects.Add(new PdfIndirectObject(new PdfObjectId(LayerObject, 0), ocg));

            PdfDictionary properties = new PdfDictionary();
            properties.Set(PdfName.Intern("L1"), new PdfReference(LayerObject, 0));
            resources.Set(PdfName.Intern("Properties"), properties);
            content.Append("/OC /L1 BDC 0 0.6 0 rg 0 50 50 50 re f EMC\n");

            PdfDictionary d = new PdfDictionary();
            d.Set(PdfName.Intern("ON"), new PdfArray(new PdfPrimitive[] { new PdfReference(LayerObject, 0) }));
            PdfDictionary ocProperties = new PdfDictionary();
            ocProperties.Set(PdfName.Intern("OCGs"), new PdfArray(new PdfPrimitive[] { new PdfReference(LayerObject, 0) }));
            ocProperties.Set(PdfName.Intern("D"), d);
            catalog.Set(PdfName.Intern("OCProperties"), ocProperties);
        }

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Intern("Parent"), new PdfReference(2, 0));
        page.Set(PdfName.MediaBox, Box(0, 0, 200, 200));
        page.Set(PdfName.Resources, resources);
        page.Set(PdfName.Contents, new PdfReference(4, 0));

        if (withAnnotation)
        {
            byte[] ap = Encoding.ASCII.GetBytes("1 0 0 rg 0 0 100 100 re f");
            PdfDictionary apDict = new PdfDictionary();
            apDict.Set(PdfName.Type, PdfName.XObject);
            apDict.Set(PdfName.Subtype, PdfName.Intern("Form"));
            apDict.Set(PdfName.Intern("BBox"), Box(0, 0, 100, 100));
            apDict.Set(PdfName.Length, ap.Length);
            objects.Add(new PdfIndirectObject(new PdfObjectId(6, 0), new PdfStream(apDict, ap)));

            PdfDictionary n = new PdfDictionary();
            n.Set(PdfName.Intern("N"), new PdfReference(6, 0));
            PdfDictionary annot = new PdfDictionary();
            annot.Set(PdfName.Type, PdfName.Intern("Annot"));
            annot.Set(PdfName.Subtype, PdfName.Intern("Stamp"));
            annot.Set(PdfName.Intern("Rect"), Box(50, 50, 150, 150));
            annot.Set(PdfName.Intern("AP"), n);
            objects.Add(new PdfIndirectObject(new PdfObjectId(7, 0), annot));
            page.Set(PdfName.Intern("Annots"), new PdfArray(new PdfPrimitive[] { new PdfReference(7, 0) }));
        }

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(3, 0) }));
        pages.Set(PdfName.Count, 1);

        byte[] bytes = Encoding.ASCII.GetBytes(content.ToString());
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, bytes.Length);

        objects.Add(new PdfIndirectObject(new PdfObjectId(1, 0), catalog));
        objects.Add(new PdfIndirectObject(new PdfObjectId(2, 0), pages));
        objects.Add(new PdfIndirectObject(new PdfObjectId(3, 0), page));
        objects.Add(new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(contentDict, bytes)));

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        return output.ToArray();
    }

    private static PdfArray Box(int x1, int y1, int x2, int y2) =>
        new PdfArray(new PdfPrimitive[] { new PdfInteger(x1), new PdfInteger(y1), new PdfInteger(x2), new PdfInteger(y2) });
}
