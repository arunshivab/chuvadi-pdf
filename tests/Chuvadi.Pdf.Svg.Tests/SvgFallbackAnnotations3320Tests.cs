// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 (D-240 R2) — the SVG raster fallback keeps AnnotationMode.

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

public sealed class SvgFallbackAnnotations3320Tests
{
    [Fact]
    public void RasterFallback_PrintMode_LeavesOutANonPrintingAnnotation()
    {
        string withAnnotation = Render(withAnnotation: true, AnnotationMode.Print);
        string without = Render(withAnnotation: false, AnnotationMode.Print);

        withAnnotation.Should().Contain("data:image/png;base64,", "the page is over the op budget and is rasterized");
        withAnnotation.Should().Be(without, "the annotation has no Print flag");
    }

    [Fact]
    public void RasterFallback_ViewMode_StillDrawsTheAnnotation()
    {
        string withAnnotation = Render(withAnnotation: true, AnnotationMode.View);
        string without = Render(withAnnotation: false, AnnotationMode.View);

        withAnnotation.Should().NotBe(without);
    }

    private static string Render(bool withAnnotation, AnnotationMode mode)
    {
        SvgExportOptions options = new SvgExportOptions
        {
            MaxVectorOps = 1,
            DensityFallback = SvgDensityFallback.Rasterize,
            RasterFallbackDpi = 72,
            AnnotationMode = mode,
        };

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(Build(withAnnotation)), leaveOpen: false);
        return new SvgRenderer(options).RenderPage(doc, 0);
    }

    // A 200 by 200 page with three small squares (over the 1-op budget) and,
    // optionally, a Stamp with no flags drawing red over 50..150.
    private static byte[] Build(bool withAnnotation)
    {
        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();
        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Page);
        page.Set(PdfName.Intern("Parent"), new PdfReference(2, 0));
        page.Set(PdfName.MediaBox, Box(0, 0, 200, 200));
        page.Set(PdfName.Resources, new PdfDictionary());
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

        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(2, 0));
        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray(new PdfPrimitive[] { new PdfReference(3, 0) }));
        pages.Set(PdfName.Count, 1);

        byte[] content = Encoding.ASCII.GetBytes("0 0 1 rg 0 0 10 10 re f 20 0 10 10 re f 40 0 10 10 re f");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);

        objects.Add(new PdfIndirectObject(new PdfObjectId(1, 0), catalog));
        objects.Add(new PdfIndirectObject(new PdfObjectId(2, 0), pages));
        objects.Add(new PdfIndirectObject(new PdfObjectId(3, 0), page));
        objects.Add(new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(contentDict, content)));
        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));
        using MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        return output.ToArray();
    }

    private static PdfArray Box(int x1, int y1, int x2, int y2) =>
        new PdfArray(new PdfPrimitive[] { new PdfInteger(x1), new PdfInteger(y1), new PdfInteger(x2), new PdfInteger(y2) });
}
