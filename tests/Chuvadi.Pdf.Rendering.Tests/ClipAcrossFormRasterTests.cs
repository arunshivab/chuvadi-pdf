// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.4 — Clipping path operators (W, W*); §8.10 — Form XObjects
//
// Regression coverage: a clip set on the page (q ... re W n) must constrain
// the content painted by a subsequently invoked form XObject (Do). Previously
// the rasteriser honoured the clip only on direct page content and ignored it
// across the form Do, so form content bled outside the clip rectangle. This is
// what made PageComposer's DestinationClip render incorrectly in-house while
// poppler rendered it correctly.

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

public sealed class ClipAcrossFormRasterTests
{
    // One-page (100x100) PDF whose red page-filling content lives inside a form
    // XObject. When clipLeftHalf is true the page clips to its left half
    // (0 0 50 100 re W n) before invoking the form, so only the left half may
    // be painted; the form itself always tries to fill the whole 100x100 page.
    private static MemoryStream BuildClipAcrossFormPdf(bool clipLeftHalf)
    {
        PdfDictionary catalog = new PdfDictionary();
        catalog.Set(PdfName.Type, PdfName.Intern("Catalog"));
        catalog.Set(PdfName.Intern("Pages"), new PdfReference(2, 0));

        PdfDictionary pages = new PdfDictionary();
        pages.Set(PdfName.Type, PdfName.Intern("Pages"));
        pages.Set(PdfName.Intern("Kids"), new PdfArray(new PdfPrimitive[] { new PdfReference(3, 0) }));
        pages.Set(PdfName.Intern("Count"), 1);

        PdfDictionary page = new PdfDictionary();
        page.Set(PdfName.Type, PdfName.Intern("Page"));
        page.Set(PdfName.Intern("Parent"), new PdfReference(2, 0));
        page.Set(PdfName.Intern("MediaBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100),
        }));
        page.Set(PdfName.Intern("Contents"), new PdfReference(4, 0));

        PdfDictionary resources = new PdfDictionary();
        PdfDictionary xobjects = new PdfDictionary();
        xobjects.Set(PdfName.Intern("Fm"), new PdfReference(5, 0));
        resources.Set(PdfName.Intern("XObject"), xobjects);
        page.Set(PdfName.Intern("Resources"), resources);

        // Clip to the left half, then invoke the whole-page form.
        string pageDrawing = clipLeftHalf ? "q 0 0 50 100 re W n /Fm Do Q" : "q /Fm Do Q";
        byte[] pageContent = Encoding.ASCII.GetBytes(pageDrawing);
        PdfDictionary pageContentDict = new PdfDictionary();
        pageContentDict.Set(PdfName.Length, pageContent.Length);

        byte[] formContent = Encoding.ASCII.GetBytes("1 0 0 rg 0 0 100 100 re f");
        PdfDictionary formDict = new PdfDictionary();
        formDict.Set(PdfName.Type, PdfName.Intern("XObject"));
        formDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Form"));
        formDict.Set(PdfName.Intern("FormType"), 1);
        formDict.Set(PdfName.Intern("BBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100),
        }));
        formDict.Set(PdfName.Length, formContent.Length);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>
        {
            new PdfIndirectObject(new PdfObjectId(1, 0), catalog),
            new PdfIndirectObject(new PdfObjectId(2, 0), pages),
            new PdfIndirectObject(new PdfObjectId(3, 0), page),
            new PdfIndirectObject(new PdfObjectId(4, 0), new PdfStream(pageContentDict, pageContent)),
            new PdfIndirectObject(new PdfObjectId(5, 0), new PdfStream(formDict, formContent)),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(1, 0));

        MemoryStream output = new MemoryStream();
        PdfWriter.Write(output, objects, trailer);
        output.Position = 0;
        return output;
    }

    // Returns the red channel at the given fractional X (0..1) and mid-height.
    private static byte RedAt(bool clipLeftHalf, double fracX)
    {
        using MemoryStream pdf = BuildClipAcrossFormPdf(clipLeftHalf);
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 });
        PixelBuffer buffer = rasterizer.Rasterize(doc.Pages[0]);
        int x = (int)(buffer.Width * fracX);
        (byte _, byte _, byte r, byte _) = buffer.GetPixelBgra(x, buffer.Height / 2);
        return r;
    }

    [Fact]
    public void Unclipped_Form_FillsWholePage()
    {
        // Control: with no clip, the form paints both halves red.
        RedAt(clipLeftHalf: false, fracX: 0.25).Should().BeGreaterThan(200);
        RedAt(clipLeftHalf: false, fracX: 0.75).Should().BeGreaterThan(200);
    }

    [Fact]
    public void PageClip_ConfinesFormContent_ToClipRect()
    {
        // Left half is inside the clip → the form's red fill shows.
        RedAt(clipLeftHalf: true, fracX: 0.25).Should().BeGreaterThan(200);

        // Right half is outside the clip → it must stay the white page sheet,
        // not the form's red. This is the regression: previously the form
        // ignored the page clip and painted the right half red too.
        byte rightRed = RedAt(clipLeftHalf: true, fracX: 0.75);
        byte rightGreen = GreenAt(clipLeftHalf: true, fracX: 0.75);
        rightRed.Should().BeGreaterThan(230);
        rightGreen.Should().BeGreaterThan(230); // white (R≈G≈B high), not red (G low)
    }

    private static byte GreenAt(bool clipLeftHalf, double fracX)
    {
        using MemoryStream pdf = BuildClipAcrossFormPdf(clipLeftHalf);
        using PdfDocument doc = PdfDocument.Open(pdf, leaveOpen: true);
        PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72 });
        PixelBuffer buffer = rasterizer.Rasterize(doc.Pages[0]);
        int x = (int)(buffer.Width * fracX);
        (byte _, byte g, byte _, byte _) = buffer.GetPixelBgra(x, buffer.Height / 2);
        return g;
    }

    [Fact]
    public void ClipRegion_Combine_IsTheIntersectionOfBothRegions()
    {
        ClipRegion? left = ClipRegion.Build(
            new List<List<List<PointF>>> { Rect(0, 0, 50, 100) },
            new List<FillRule> { FillRule.NonZeroWinding });
        ClipRegion? bottom = ClipRegion.Build(
            new List<List<List<PointF>>> { Rect(0, 0, 100, 50) },
            new List<FillRule> { FillRule.NonZeroWinding });

        left.Should().NotBeNull();
        bottom.Should().NotBeNull();

        ClipRegion combined = left!.Combine(bottom!);

        // At y=25 both allow it: left → x[0,50], bottom → x[0,100]; intersection x[0,50].
        List<(double Start, double End)> lower = combined.AllowedIntervals(25);
        lower.Should().ContainSingle();
        lower[0].Start.Should().BeApproximately(0, 0.001);
        lower[0].End.Should().BeApproximately(50, 0.001);

        // At y=75 the bottom region excludes everything → empty.
        combined.AllowedIntervals(75).Should().BeEmpty();
    }

    private static List<List<PointF>> Rect(double x, double y, double w, double h)
    {
        return new List<List<PointF>>
        {
            new List<PointF>
            {
                new PointF(x, y),
                new PointF(x + w, y),
                new PointF(x + w, y + h),
                new PointF(x, y + h),
                new PointF(x, y),
            },
        };
    }
}
