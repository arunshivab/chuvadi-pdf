// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8 — Graphics model
// PHASE: v2.0.0 R1 D3a — PageRasterizer behavioral regression tests
//
// These tests pin down the visual output of the current monolithic
// PageRasterizer so that the v2.0.0 D3c refactor (which splits the
// rasterizer into DisplayListBuilder + display-list painter) cannot
// silently regress behavior. They are intentionally structural rather
// than pixel-golden: a robust assertion is "the rect is dark in the
// expected band" rather than "row 27 col 38 is BGRA(0,0,0,255)".

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

/// <summary>
/// Behavioral regression tests for <see cref="PageRasterizer"/>.
/// </summary>
/// <remarks>
/// <para>
/// All tests render at 72 DPI so that 1 PDF point equals 1 pixel, and use
/// 100×100-point pages so that pixel coordinates can be reasoned about
/// directly. PDF user space has its origin at the bottom-left (Y increases
/// upward); the pixel buffer is top-left origin (Y increases downward).
/// The <see cref="PdfYToPixelRow"/> helper converts between them.
/// </para>
/// <para>
/// The PDF builder helper is intentionally kept private to this class to
/// avoid collision with the equivalent helper in <c>RenderingTests.cs</c>.
/// </para>
/// </remarks>
public sealed class PageRasterizerRegressionTests
{
    private const int PageW = 100;
    private const int PageH = 100;

    /// <summary>Converts a PDF Y coordinate (bottom-up) to a pixel row (top-down).</summary>
    private static int PdfYToPixelRow(int pdfY) => PageH - pdfY;

    // ── Path filling: position and color ──────────────────────────────────

    [Fact]
    public void Fill_BlackRectangle_FillsExpectedRegion()
    {
        // PDF rect at user-space (25,25)-(75,75). 0 g = black nonstroke color.
        PixelBuffer buf = RenderAt72Dpi("0 g 25 25 50 50 re f");

        // Center of the rect should be black.
        AssertBlack(buf, 50, 50);

        // Two points outside the rect should still be white.
        AssertWhite(buf, 10, 10);
        AssertWhite(buf, 90, 90);
    }

    [Fact]
    public void Fill_Rg_AppliesRedColor()
    {
        // 1 0 0 rg = pure red nonstroke color in DeviceRGB.
        PixelBuffer buf = RenderAt72Dpi("1 0 0 rg 25 25 50 50 re f");

        (byte b, byte g, byte r, byte _) = buf.GetPixelBgra(50, 50);
        r.Should().BeGreaterThan(200, "red channel should dominate at rect center");
        g.Should().BeLessThan(60, "green channel should be near zero at rect center");
        b.Should().BeLessThan(60, "blue channel should be near zero at rect center");
    }

    [Fact]
    public void Fill_G_AppliesMidGray()
    {
        // 0.5 g = ~50% gray nonstroke color.
        PixelBuffer buf = RenderAt72Dpi("0.5 g 25 25 50 50 re f");

        (byte b, byte g, byte r, byte _) = buf.GetPixelBgra(50, 50);
        r.Should().BeInRange(100, 160, "0.5 gray should fall in the mid-gray band");
        g.Should().BeInRange(100, 160, "0.5 gray should fall in the mid-gray band");
        b.Should().BeInRange(100, 160, "0.5 gray should fall in the mid-gray band");
    }

    [Fact]
    public void Fill_TwoSubpathsInOnePath_BothRegionsFilled()
    {
        // Two separate rectangles in a single path, then a single f operator.
        // Tests that the rasterizer correctly handles multi-subpath fills.
        PixelBuffer buf = RenderAt72Dpi(
            "0 g 10 10 20 20 re 60 60 20 20 re f");

        // First square is at PDF (10,10)-(30,30); check its center.
        AssertBlack(buf, 20, PdfYToPixelRow(20));

        // Second square is at PDF (60,60)-(80,80); check its center.
        AssertBlack(buf, 70, PdfYToPixelRow(70));

        // Gap between them should be white.
        AssertWhite(buf, 50, PdfYToPixelRow(50));
    }

    // ── Stroke ────────────────────────────────────────────────────────────

    [Fact]
    public void Stroke_Rectangle_OutlinesBorderNotInterior()
    {
        // 0 G = black stroke color. 2 w = 2-point line width. S = stroke without fill.
        PixelBuffer buf = RenderAt72Dpi("0 G 2 w 25 25 50 50 re S");

        // The rect interior should remain white.
        AssertWhite(buf, 50, 50);

        // The top edge (PDF y=75, pixel row 25) should contain dark pixels somewhere
        // between PDF x=25 and x=75.
        bool foundDarkOnEdge = false;

        for (int x = 24; x <= 76; x++)
        {
            (_, _, byte r, _) = buf.GetPixelBgra(x, 25);

            if (r < 200)
            {
                foundDarkOnEdge = true;
                break;
            }
        }

        foundDarkOnEdge.Should().BeTrue("stroked top edge should contain dark pixels");
    }

    [Fact]
    public void Stroke_WidthIncrease_HitsMorePixels()
    {
        // Same vertical line drawn at width 1 and width 8.
        PixelBuffer thin = RenderAt72Dpi("0 G 1 w 50 0 m 50 100 l S");
        PixelBuffer thick = RenderAt72Dpi("0 G 8 w 50 0 m 50 100 l S");

        int thinDark = CountDarkInRow(thin, 50);
        int thickDark = CountDarkInRow(thick, 50);

        thickDark.Should().BeGreaterThan(thinDark, "wider stroke should hit more pixels per row");
    }

    // ── CTM and state stack ───────────────────────────────────────────────

    [Fact]
    public void Cm_Translate_MovesSubsequentDrawing()
    {
        // Apply translation (1 0 0 1 40 40 cm) then draw a 20x20 rect.
        // The rect at source coords (10,10)-(30,30) should appear shifted to
        // PDF (50,50)-(70,70).
        PixelBuffer buf = RenderAt72Dpi(
            "1 0 0 1 40 40 cm 0 g 10 10 20 20 re f");

        // New position should be black.
        AssertBlack(buf, 60, PdfYToPixelRow(60));

        // Source (un-translated) position should remain white.
        AssertWhite(buf, 20, PdfYToPixelRow(20));
    }

    [Fact]
    public void Q_RestoresPriorCtm()
    {
        // Inside q...Q: translate then draw. After Q: draw again with NO translate.
        PixelBuffer buf = RenderAt72Dpi(
            "q 1 0 0 1 40 40 cm 0 g 10 10 20 20 re f Q " +
            "0 g 10 10 20 20 re f");

        // First rect was translated to PDF (50,50)-(70,70).
        AssertBlack(buf, 60, PdfYToPixelRow(60));

        // Second rect is NOT translated; it is at PDF (10,10)-(30,30).
        AssertBlack(buf, 20, PdfYToPixelRow(20));
    }

    [Fact]
    public void Cm_Scale_EnlargesFootprint()
    {
        // The same 20x20 source rect scales to 40x40 with a 2x CTM.
        PixelBuffer small = RenderAt72Dpi("0 g 10 10 20 20 re f");
        PixelBuffer large = RenderAt72Dpi("2 0 0 2 0 0 cm 0 g 10 10 20 20 re f");

        int smallDark = CountDarkPixels(small);
        int largeDark = CountDarkPixels(large);

        largeDark.Should().BeGreaterThan(smallDark * 3,
            "a 2x scale should roughly quadruple the filled pixel area");
    }

    // ── Curves ────────────────────────────────────────────────────────────

    [Fact]
    public void Curve_c_FillsCurvedRegion()
    {
        // A closed shape with a cubic bezier sweeping up: line from (20,50) to
        // (80,50), then a curve back to (20,50) via control points (80,90) and
        // (50,90). The bump apex sits around PDF (50,~80).
        string content =
            "0 g " +
            "20 50 m " +
            "80 50 l " +
            "80 90 50 90 20 50 c " +
            "h f";

        PixelBuffer buf = RenderAt72Dpi(content);

        // Inside the bump (above the baseline) should be black.
        AssertBlack(buf, 50, PdfYToPixelRow(70));

        // Below the baseline should remain white (the shape lives above y=50).
        AssertWhite(buf, 50, PdfYToPixelRow(30));
    }

    // ── Determinism ───────────────────────────────────────────────────────

    [Fact]
    public void Rasterize_SameContent_IsBitwiseDeterministic()
    {
        // Two renders of identical PDFs should produce identical pixel buffers.
        // This catches non-deterministic ordering bugs in the operator interpreter
        // or in path-merging during the refactor.
        const string content = "0 g 25 25 50 50 re f";
        PixelBuffer a = RenderAt72Dpi(content);
        PixelBuffer b = RenderAt72Dpi(content);

        a.Width.Should().Be(b.Width);
        a.Height.Should().Be(b.Height);

        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                (byte b1, byte g1, byte r1, byte a1) = a.GetPixelBgra(x, y);
                (byte b2, byte g2, byte r2, byte a2) = b.GetPixelBgra(x, y);

                if (b1 != b2 || g1 != g2 || r1 != r2 || a1 != a2)
                {
                    throw new Xunit.Sdk.XunitException(
                        $"Pixel mismatch at ({x},{y}): " +
                        $"BGRA ({b1},{g1},{r1},{a1}) vs ({b2},{g2},{r2},{a2})");
                }
            }
        }
    }

    // ── Currently-skipped operators must remain non-fatal ─────────────────

    // ── Clipping (W / W* honoured since the v2.1 clip work) ───────────────

    [Fact]
    public void Clip_W_n_RestrictsFillToClipRegion()
    {
        // Clip to the rectangle PDF (10,10)-(90,90) with non-zero winding,
        // then fill the entire page black. Pixels inside the clip must be
        // black; pixels outside it must remain white.
        string content =
            "q " +
            "10 10 80 80 re W n " +
            "0 g 0 0 100 100 re f " +
            "Q";

        PixelBuffer buf = RenderAt72Dpi(content);

        // Inside the clip region: filled black.
        AssertBlack(buf, 50, 50);

        // Outside the clip region (PDF (5,5) and (95,95)) but inside the page:
        // must stay white because the clip excludes them.
        AssertWhite(buf, 5, PdfYToPixelRow(5));
        AssertWhite(buf, 95, PdfYToPixelRow(95));
    }

    [Fact]
    public void Clip_WStar_n_RestrictsFillToClipRegion()
    {
        // Same as above but with the even-odd clip operator W*.
        string content =
            "q " +
            "10 10 80 80 re W* n " +
            "0 g 0 0 100 100 re f " +
            "Q";

        PixelBuffer buf = RenderAt72Dpi(content);

        AssertBlack(buf, 50, 50);
        AssertWhite(buf, 5, PdfYToPixelRow(5));
        AssertWhite(buf, 95, PdfYToPixelRow(95));
    }

    [Fact]
    public void Clip_NonRectangularPath_RestrictsFill()
    {
        // A triangular clip path (not axis-aligned), then fill the whole page.
        // A point near the wide base-centre of the triangle is inside; a point
        // in the top corner of the page is outside and must stay white.
        // Triangle vertices in PDF space: (50,80) apex, (20,20), (80,20).
        string content =
            "q " +
            "50 80 m 20 20 l 80 20 l h W n " +
            "0 g 0 0 100 100 re f " +
            "Q";

        PixelBuffer buf = RenderAt72Dpi(content);

        // Near the centroid of the triangle (PDF ~ (50,40)): inside → black.
        AssertBlack(buf, 50, PdfYToPixelRow(40));

        // Page top-left corner (PDF (5,95)): well outside the triangle → white.
        AssertWhite(buf, 5, PdfYToPixelRow(95));

        // Bottom-left page corner (PDF (5,5)): below-left of the triangle → white.
        AssertWhite(buf, 5, PdfYToPixelRow(5));
    }

    [Fact]
    public void Clip_OutsideRegion_StaysBackgroundEvenWithColoredFill()
    {
        // Regression guard: a coloured fill must also be clipped, not just black.
        string content =
            "q " +
            "40 40 20 20 re W n " +
            "1 0 0 rg 0 0 100 100 re f " +
            "Q";

        PixelBuffer buf = RenderAt72Dpi(content);

        // Inside the small clip: must be true RED, not white and not black.
        // Asserting only R > 200 is insufficient because white (255,255,255)
        // also satisfies it; we require G and B to be low so the pixel is
        // unambiguously red - this is what guards against a colour-under-clip
        // regression (the symptom originally suspected during clip bring-up).
        (byte b, byte g, byte r, byte _) = buf.GetPixelBgra(50, 50);
        r.Should().BeGreaterThan(200, "centre of the clip should be filled red (R high)");
        g.Should().BeLessThan(60, "centre of the clip should be filled red (G low)");
        b.Should().BeLessThan(60, "centre of the clip should be filled red (B low)");

        // Outside the clip: untouched white background.
        AssertWhite(buf, 10, PdfYToPixelRow(10));
    }

    [Fact]
    public void BT_ET_NoFont_DoesNotCrash()
    {
        // A text object with no Tf operator: the rasterizer should tolerate this
        // gracefully (today: no glyphs drawn; behavior preserved post-refactor).
        Action act = () => RenderAt72Dpi("BT (hello) Tj ET");
        act.Should().NotThrow();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static PixelBuffer RenderAt72Dpi(string content)
    {
        using MemoryStream pdfStream = BuildPdfWithContent(content);
        using PdfDocument doc = PdfDocument.Open(pdfStream, leaveOpen: true);
        PageRasterizer rasterizer = new PageRasterizer(
            doc.Objects,
            new RenderOptions { Dpi = 72 });
        return rasterizer.Rasterize(doc.Pages[0]);
    }

    private static void AssertBlack(PixelBuffer buf, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = buf.GetPixelBgra(x, y);
        r.Should().BeLessThan(60, $"pixel ({x},{y}) R channel should be dark");
        g.Should().BeLessThan(60, $"pixel ({x},{y}) G channel should be dark");
        b.Should().BeLessThan(60, $"pixel ({x},{y}) B channel should be dark");
    }

    private static void AssertWhite(PixelBuffer buf, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = buf.GetPixelBgra(x, y);
        r.Should().BeGreaterThan(200, $"pixel ({x},{y}) R channel should be light");
        g.Should().BeGreaterThan(200, $"pixel ({x},{y}) G channel should be light");
        b.Should().BeGreaterThan(200, $"pixel ({x},{y}) B channel should be light");
    }

    private static int CountDarkPixels(PixelBuffer buf)
    {
        int count = 0;

        for (int y = 0; y < buf.Height; y++)
        {
            for (int x = 0; x < buf.Width; x++)
            {
                (_, _, byte r, _) = buf.GetPixelBgra(x, y);

                if (r < 60)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountDarkInRow(PixelBuffer buf, int y)
    {
        int count = 0;

        for (int x = 0; x < buf.Width; x++)
        {
            (_, _, byte r, _) = buf.GetPixelBgra(x, y);

            if (r < 60)
            {
                count++;
            }
        }

        return count;
    }

    private static MemoryStream BuildPdfWithContent(string content)
    {
        byte[] contentBytes = System.Text.Encoding.Latin1.GetBytes(content);
        PdfObjectId catalogId = new PdfObjectId(1, 0);
        PdfObjectId pagesId = new PdfObjectId(2, 0);
        PdfObjectId pageId = new PdfObjectId(3, 0);
        PdfObjectId streamId = new PdfObjectId(4, 0);

        PdfDictionary streamDict = new PdfDictionary();
        streamDict.Set(PdfName.Length, contentBytes.Length);
        PdfStream contentStream = new PdfStream(streamDict, contentBytes);

        PdfDictionary pageDict = new PdfDictionary();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.Contents, new PdfReference(streamId));

        PdfArray mediaBox = new PdfArray(
        [
            new PdfInteger(0),
            new PdfInteger(0),
            new PdfInteger(PageW),
            new PdfInteger(PageH),
        ]);
        pageDict.Set(PdfName.MediaBox, mediaBox);

        PdfDictionary pagesDict = new PdfDictionary();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new PdfDictionary();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects =
        [
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
