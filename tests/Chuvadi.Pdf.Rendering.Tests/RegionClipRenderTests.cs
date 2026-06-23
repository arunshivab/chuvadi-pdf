// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3.4 (CTM / device space)
//
// Verifies PageRasterizer.RenderRegion / RenderClipped: a region render equals
// the matching crop of a full-page render, pixel dimensions follow DPI, the
// clipped render is sized to the clip's bounding box with out-of-clip pixels
// left transparent, and the PNG overloads emit valid PNG data.

using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.DisplayList;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class RegionClipRenderTests
{
    [Fact]
    public void RenderRegion_MatchesFullPageCrop_PixelForPixel()
    {
        using PdfDocument doc = BuildRectPdf();
        PdfPage page = doc.Pages[0];
        PageRasterizer rast = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72, AntiAlias = false });

        PixelBuffer full = rast.Rasterize(page);
        Rect region = new Rect(40, 60, 100, 80);
        PixelBuffer reg = rast.RenderRegion(page, region, 72);

        reg.Width.Should().Be(100);
        reg.Height.Should().Be(80);

        int offX = (int)System.Math.Round(region.X);
        int offY = (int)System.Math.Round(page.Height - region.Y - region.Height);

        int maxDiff = 0;
        for (int y = 0; y < reg.Height; y++)
        {
            for (int x = 0; x < reg.Width; x++)
            {
                (byte b0, byte g0, byte r0, byte _) = reg.GetPixelBgra(x, y);
                (byte b1, byte g1, byte r1, byte _) = full.GetPixelBgra(offX + x, offY + y);
                int d = System.Math.Max(System.Math.Max(System.Math.Abs(b0 - b1), System.Math.Abs(g0 - g1)), System.Math.Abs(r0 - r1));
                if (d > maxDiff) { maxDiff = d; }
            }
        }

        maxDiff.Should().Be(0);
    }

    [Fact]
    public void RenderRegion_PixelSizeFollowsDpi()
    {
        using PdfDocument doc = BuildRectPdf();
        PageRasterizer rast = new PageRasterizer(doc.Objects);

        PixelBuffer buffer = rast.RenderRegion(doc.Pages[0], new Rect(0, 0, 100, 50), 144);

        buffer.Width.Should().Be(200);  // 100 pt * 144/72
        buffer.Height.Should().Be(100); // 50 pt * 144/72
    }

    [Fact]
    public void RenderRegion_RejectsInvalidArguments()
    {
        using PdfDocument doc = BuildRectPdf();
        PdfPage page = doc.Pages[0];
        PageRasterizer rast = new PageRasterizer(doc.Objects);

        rast.Invoking(r => r.RenderRegion(page, new Rect(0, 0, 100, 100), 0))
            .Should().Throw<System.ArgumentOutOfRangeException>();
        rast.Invoking(r => r.RenderRegion(page, new Rect(0, 0, 0, 100), 72))
            .Should().Throw<System.ArgumentOutOfRangeException>();
        rast.Invoking(r => r.RenderRegion(null!, new Rect(0, 0, 100, 100), 72))
            .Should().Throw<System.ArgumentNullException>();
    }

    [Fact]
    public void RenderClipped_SizedToClipBounds_OutsideIsTransparent()
    {
        using PdfDocument doc = BuildRectPdf();
        PageRasterizer rast = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 72, AntiAlias = false });

        // Triangle: apex (50,100) page-space -> device top; base (0,0)-(100,0) -> device bottom.
        PathGeometry tri = new PathGeometry().MoveTo(0, 0).LineTo(100, 0).LineTo(50, 100).Close();
        PixelBuffer c = rast.RenderClipped(doc.Pages[0], tri, 72);

        c.Width.Should().Be(100);
        c.Height.Should().Be(100);

        // Top corners (near apex) are outside the triangle -> transparent.
        c.GetPixelBgra(2, 2).A.Should().Be(0);
        c.GetPixelBgra(c.Width - 2, 2).A.Should().Be(0);
        // Bottom (wide base) and centroid are inside -> opaque.
        c.GetPixelBgra(2, c.Height - 2).A.Should().Be(255);
        c.GetPixelBgra(c.Width / 2, c.Height / 2).A.Should().Be(255);

        // A triangle fills half of its bounding box.
        int opaque = 0;
        for (int y = 0; y < c.Height; y++)
        {
            for (int x = 0; x < c.Width; x++)
            {
                if (c.GetPixelBgra(x, y).A != 0) { opaque++; }
            }
        }
        double fraction = (double)opaque / (c.Width * c.Height);
        fraction.Should().BeApproximately(0.5, 0.05);
    }

    [Fact]
    public void RenderRegionToPng_And_RenderClippedToPng_EmitValidPng()
    {
        using PdfDocument doc = BuildRectPdf();
        PdfPage page = doc.Pages[0];
        PageRasterizer rast = new PageRasterizer(doc.Objects);

        byte[] regionPng = rast.RenderRegionToPng(page, new Rect(0, 0, 100, 100), 96);
        PathGeometry tri = new PathGeometry().MoveTo(0, 0).LineTo(100, 0).LineTo(50, 100).Close();
        byte[] clippedPng = rast.RenderClippedToPng(page, tri, 96);

        AssertPngSignature(regionPng);
        AssertPngSignature(clippedPng);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void AssertPngSignature(byte[] png)
    {
        png.Length.Should().BeGreaterThan(8);
        png[0].Should().Be(0x89);
        png[1].Should().Be(0x50); // P
        png[2].Should().Be(0x4E); // N
        png[3].Should().Be(0x47); // G
    }

    // One-page 200x200 PDF with a filled rectangle so regions have content.
    private static PdfDocument BuildRectPdf()
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
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(200),
        }));

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(contentId));
        pageDict.Set(PdfName.Intern("Resources"), new PdfDictionary());

        byte[] content = System.Text.Encoding.ASCII.GetBytes("0 0 1 rg\n30 30 140 140 re\nf");
        PdfDictionary contentDict = new PdfDictionary();
        contentDict.Set(PdfName.Length, content.Length);
        PdfStream contentStream = new PdfStream(contentDict, content);

        PdfIndirectObject[] objects =
        {
            new PdfIndirectObject(catalogId, catalog),
            new PdfIndirectObject(pagesId, pages),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(contentId, contentStream),
        };

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }
}
