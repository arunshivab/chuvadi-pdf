// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8 — Graphics model
// PHASE: Phase 2 — Chuvadi.Pdf.Rendering tests

using System;
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

// ── RenderingException ────────────────────────────────────────────────────

public sealed class RenderingExceptionTests
{
    [Fact]
    public void DefaultConstructor_HasMessage()
    {
        RenderingException ex = new RenderingException();
        ex.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void MessageConstructor_PreservesMessage()
    {
        RenderingException ex = new RenderingException("fail");
        ex.Message.Should().Be("fail");
    }

    [Fact]
    public void InnerExceptionConstructor_PreservesInner()
    {
        InvalidOperationException inner = new InvalidOperationException("inner");
        RenderingException ex = new RenderingException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}

// ── RenderOptions ─────────────────────────────────────────────────────────

public sealed class RenderOptionsTests
{
    [Fact]
    public void Default_HasReasonableDpi()
    {
        RenderOptions.Default.Dpi.Should().Be(150);
    }

    [Fact]
    public void Default_BackgroundIsWhite()
    {
        RenderOptions.Default.Background.Should().Be(ColorF.White);
    }

    [Fact]
    public void PixelSize_StandardA4At96Dpi_IsReasonable()
    {
        // A4 at 96 DPI: 595pt × 842pt → (595/72*96) × (842/72*96)
        (int w, int h) = new RenderOptions { Dpi = 96 }.PixelSize(595, 842);
        w.Should().BeInRange(790, 800);
        h.Should().BeInRange(1120, 1130);
    }

    [Fact]
    public void PixelSize_NegativeWidth_Throws()
    {
        Action act = () => RenderOptions.Default.PixelSize(-1, 100);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Scale_At72Dpi_IsOne()
    {
        RenderOptions opts = new RenderOptions { Dpi = 72 };
        opts.Scale.Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void Scale_At144Dpi_IsTwo()
    {
        RenderOptions opts = new RenderOptions { Dpi = 144 };
        opts.Scale.Should().BeApproximately(2.0, 1e-10);
    }
}

// ── ScanlineRasterizer ────────────────────────────────────────────────────

public sealed class ScanlineRasterizerTests
{
    [Fact]
    public void Fill_NullBuffer_Throws()
    {
        ScanlineRasterizer r = new ScanlineRasterizer();
        Action act = () => r.Fill(null!, new List<List<PointF>>(), ColorF.Black, FillRule.NonZeroWinding);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Fill_NullSubPaths_Throws()
    {
        ScanlineRasterizer r = new ScanlineRasterizer();
        PixelBuffer buf = new PixelBuffer(10, 10);
        Action act = () => r.Fill(buf, null!, ColorF.Black, FillRule.NonZeroWinding);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Fill_EmptySubPaths_LeavesBufferUnchanged()
    {
        ScanlineRasterizer r = new ScanlineRasterizer();
        PixelBuffer buf = new PixelBuffer(10, 10);
        buf.ClearWhite();
        r.Fill(buf, new List<List<PointF>>(), ColorF.Black, FillRule.NonZeroWinding);
        (byte b, byte g, byte rv, byte a) = buf.GetPixelBgra(5, 5);
        rv.Should().Be(255); // Still white
    }

    [Fact]
    public void Fill_Rectangle_FillsInterior()
    {
        ScanlineRasterizer r = new ScanlineRasterizer();
        PixelBuffer buf = new PixelBuffer(20, 20);
        buf.ClearWhite();

        // A 10×10 rectangle in the middle
        List<PointF> rect = [
            new PointF(5, 5),
            new PointF(15, 5),
            new PointF(15, 15),
            new PointF(5, 15),
            new PointF(5, 5),
        ];

        r.Fill(buf, new List<List<PointF>> { rect }, ColorF.Black, FillRule.NonZeroWinding);

        // Interior should be black
        (byte b, byte g, byte rv, byte a) = buf.GetPixelBgra(10, 10);
        rv.Should().Be(0);
        g.Should().Be(0);
        b.Should().Be(0);

        // Exterior should still be white
        (byte b2, byte g2, byte r2, byte a2) = buf.GetPixelBgra(2, 2);
        r2.Should().Be(255);
    }

    [Fact]
    public void Fill_EvenOdd_SameResult_SimpleRect()
    {
        ScanlineRasterizer r = new ScanlineRasterizer();
        PixelBuffer buf1 = new PixelBuffer(20, 20);
        PixelBuffer buf2 = new PixelBuffer(20, 20);
        buf1.ClearWhite();
        buf2.ClearWhite();

        List<PointF> rect = [
            new PointF(5, 5),
            new PointF(15, 5),
            new PointF(15, 15),
            new PointF(5, 15),
            new PointF(5, 5),
        ];

        r.Fill(buf1, new List<List<PointF>> { rect }, ColorF.Black, FillRule.NonZeroWinding);
        r.Fill(buf2, new List<List<PointF>> { rect }, ColorF.Black, FillRule.EvenOdd);

        // For a simple non-self-intersecting polygon, both rules give same result
        (byte b1, byte g1, byte rv1, byte a1) = buf1.GetPixelBgra(10, 10);
        (byte b2, byte g2, byte rv2, byte a2) = buf2.GetPixelBgra(10, 10);
        rv1.Should().Be(rv2);
    }
}

// ── StrokeExpander ────────────────────────────────────────────────────────

public sealed class StrokeExpanderTests
{
    [Fact]
    public void Expand_NullSubPaths_Throws()
    {
        StrokeExpander ex = new StrokeExpander();
        Action act = () => ex.Expand(null!, StrokeStyle.Default);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Expand_NullStyle_Throws()
    {
        StrokeExpander ex = new StrokeExpander();
        Action act = () => ex.Expand(new List<List<PointF>>(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Expand_EmptySubPaths_ReturnsEmpty()
    {
        StrokeExpander ex = new StrokeExpander();
        List<List<PointF>> result = ex.Expand(new List<List<PointF>>(), StrokeStyle.Default);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Expand_ZeroWidth_ReturnsEmpty()
    {
        StrokeExpander ex = new StrokeExpander();
        StrokeStyle zero = StrokeStyle.Default.WithWidth(0);
        List<PointF> line = [new PointF(0, 0), new PointF(10, 0)];
        List<List<PointF>> result = ex.Expand(new List<List<PointF>> { line }, zero);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Expand_HorizontalLine_ProducesRectangle()
    {
        StrokeExpander ex = new StrokeExpander();
        StrokeStyle style = StrokeStyle.Default.WithWidth(2);
        List<PointF> line = [new PointF(0, 10), new PointF(20, 10)];
        List<List<PointF>> result = ex.Expand(new List<List<PointF>> { line }, style);
        result.Should().HaveCount(1);
        result[0].Count.Should().BeGreaterThan(2);
    }
}

// ── PageRasterizer ────────────────────────────────────────────────────────

public sealed class PageRasterizerTests
{
    private static PdfObjectStore MakeStore()
    {
        return new PdfObjectStore(_ => null);
    }

    [Fact]
    public void Constructor_NullObjects_Throws()
    {
        Action act = () => new PageRasterizer(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Rasterize_NullPage_Throws()
    {
        PageRasterizer r = new PageRasterizer(MakeStore());
        Action act = () => r.Rasterize(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Rasterize_BlankPage_ProducesWhiteBuffer()
    {
        using (MemoryStream pdfStream = BuildBlankPdf())
        using (PdfDocument doc = PdfDocument.Open(pdfStream, leaveOpen: true))
        {
            PageRasterizer r = new PageRasterizer(doc.Objects);
            PixelBuffer buf = r.Rasterize(doc.Pages[0]);

            buf.Width.Should().BeGreaterThan(0);
            buf.Height.Should().BeGreaterThan(0);

            // Background should be white
            (byte b, byte g, byte rv, byte a) = buf.GetPixelBgra(buf.Width / 2, buf.Height / 2);
            rv.Should().Be(255);
            g.Should().Be(255);
            b.Should().Be(255);
        }
    }

    [Fact]
    public void Rasterize_PageWithRect_ProducesNonWhitePixels()
    {
        using (MemoryStream pdfStream = BuildRectPdf())
        using (PdfDocument doc = PdfDocument.Open(pdfStream, leaveOpen: true))
        {
            PageRasterizer r = new PageRasterizer(doc.Objects,
                new RenderOptions { Dpi = 72 }); // 1pt = 1px at 72 DPI
            PixelBuffer buf = r.Rasterize(doc.Pages[0]);

            // The filled rectangle covers most of the page
            // Find at least one non-white pixel
            bool foundNonWhite = false;

            for (int y = 0; y < buf.Height && !foundNonWhite; y++)
            {
                for (int x = 0; x < buf.Width && !foundNonWhite; x++)
                {
                    (byte b, byte g, byte rv, byte a) = buf.GetPixelBgra(x, y);

                    if (rv < 200)
                    {
                        foundNonWhite = true;
                    }
                }
            }

            foundNonWhite.Should().BeTrue("the page has a filled rectangle");
        }
    }

    [Fact]
    public void RasterizeToPng_BlankPage_ProducesPngBytes()
    {
        using (MemoryStream pdfStream = BuildBlankPdf())
        using (PdfDocument doc = PdfDocument.Open(pdfStream, leaveOpen: true))
        {
            PageRasterizer r = new PageRasterizer(doc.Objects);
            byte[] png = r.RasterizeToPng(doc.Pages[0]);
            png.Should().NotBeEmpty();
            // Check PNG signature
            png[0].Should().Be(137);
            png[1].Should().Be(80);
        }
    }

    // ── PDF builder helpers ────────────────────────────────────────────────

    private static MemoryStream BuildBlankPdf()
    {
        return BuildPdfWithContent("% blank");
    }

    private static MemoryStream BuildRectPdf()
    {
        // Content stream: draw a filled black rectangle 50x50 at (25,25)
        return BuildPdfWithContent("0 g 25 25 50 50 re f");
    }

    private static MemoryStream BuildPdfWithContent(string content)
    {
        byte[] contentBytes = System.Text.Encoding.Latin1.GetBytes(content);
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId streamId = new PdfObjectId(4, 0);

        // Content stream
        PdfDictionary streamDict = new PdfDictionary();
        streamDict.Set(PdfName.Length, contentBytes.Length);
        PdfStream contentStream = new PdfStream(streamDict, contentBytes);

        // Page
        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(streamId));

        // MediaBox [0 0 100 100]
        PdfArray mediaBox = new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(100), new PdfInteger(100),
        ]);
        pageDict.Set(PdfName.MediaBox, mediaBox);

        // Pages
        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);

        // Catalog
        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = [
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
            new PdfIndirectObject(streamId, contentStream),
        ];

        PdfDictionary trailer = new PdfDictionary();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new MemoryStream();
        PdfWriter.Write(ms, objects, trailer);
        return ms;
    }
}
