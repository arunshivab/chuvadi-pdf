// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for the v3.19 Reader-delegation pack: PDF-04 arrows, PDF-05 multi-stamp
// with opacity, PDF-06 imposition parity, PDF-11 header/footer resolution.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Operations.Tests;

public sealed class ReaderDelegationTests
{
    private static readonly int[] FirstPageOnly = [0];

    // ── PDF-04: arrows ────────────────────────────────────────────────────

    [Fact]
    public void DrawArrow_RendersShaftAndWiderHead()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(new PageSize(200, 200)).DrawArrow(20, 100, 180, 100, new Color(255, 0, 0), width: 4);
        PixelBuffer pixels = Rasterize(builder.ToByteArray(), 0);

        IsRed(pixels, 100, 100).Should().BeTrue("the shaft is drawn");
        IsWhite(pixels, 100, 95).Should().BeTrue("the 4pt shaft does not reach 5pt off-axis");
        IsRed(pixels, 166, 96).Should().BeTrue("near its base the 12pt-wide head covers 4pt off-axis");
        IsWhite(pixels, 186, 100).Should().BeTrue("nothing is drawn past the tip");
    }

    [Fact]
    public void DrawArrow_BothEnds_DrawsAHeadAtEachEnd()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(new PageSize(200, 200))
            .DrawArrow(20, 100, 180, 100, new Color(255, 0, 0), width: 4, ends: ArrowEnds.Both);
        PixelBuffer pixels = Rasterize(builder.ToByteArray(), 0);

        IsRed(pixels, 34, 96).Should().BeTrue("the start carries a head");
        IsRed(pixels, 166, 96).Should().BeTrue("the end carries a head");
    }

    [Fact]
    public void DrawArrow_ZeroLength_DrawsNothing()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(new PageSize(100, 100)).DrawArrow(50, 50, 50, 50, new Color(255, 0, 0), width: 4);
        PixelBuffer pixels = Rasterize(builder.ToByteArray(), 0);

        IsWhite(pixels, 50, 50).Should().BeTrue();
    }

    // ── PDF-05: multi-stamp in one pass, with opacity ─────────────────────

    [Fact]
    public void PlaceMany_StampsSeveralPagesInOnePass_WithOpacity()
    {
        using PdfDocument target = Open(BlankPages(2, 200, 200));
        using PdfDocument stamp = Open(SolidSquare(100, new Color(255, 0, 0)));
        using MemoryStream output = new MemoryStream();

        PageStamper.PlaceMany(output, target, new List<StampRequest>
        {
            new StampRequest(0, stamp, 0, Transform.Identity) { Opacity = 0.5 },
            new StampRequest(0, stamp, 0, Transform.CreateTranslation(100, 100)),
            new StampRequest(1, stamp, 0, Transform.Identity),
        });

        output.Position = 0;
        using PdfDocument result = PdfDocument.Open(output, leaveOpen: true);
        PixelBuffer page0 = Rasterize(result, 0, gammaCorrect: false);
        PixelBuffer page1 = Rasterize(result, 1);

        // Raster rows are top-down; the half-opacity stamp covers y 100..200
        // top-down. Gamma-correct blending is off so 50% reads as sRGB 128.
        (byte b, byte g, byte r, byte _) = page0.GetPixelBgra(50, 150);
        r.Should().BeGreaterThan(240);
        g.Should().BeInRange(100, 156, "red at 50% over white is about (255, 128, 128)");
        b.Should().BeInRange(100, 156);

        IsRed(page0, 150, 50).Should().BeTrue("the second, opaque stamp on the same page");
        IsRed(page1, 50, 150).Should().BeTrue("the stamp on the other page");
    }

    [Fact]
    public void PlaceMany_TranslucentStamp_AddsCollisionFreeExtGState()
    {
        using PdfDocument target = Open(BlankPages(1, 200, 200));
        using PdfDocument stamp = Open(SolidSquare(100, new Color(0, 0, 255)));
        using MemoryStream first = new MemoryStream();
        PageStamper.PlaceMany(first, target, new List<StampRequest>
        {
            new StampRequest(0, stamp, 0, Transform.Identity) { Opacity = 0.25 },
        });

        first.Position = 0;
        using PdfDocument once = PdfDocument.Open(first, leaveOpen: true);
        using MemoryStream second = new MemoryStream();
        PageStamper.PlaceMany(second, once, new List<StampRequest>
        {
            new StampRequest(0, stamp, 0, Transform.Identity) { Opacity = 0.75 },
        });

        second.Position = 0;
        using PdfDocument twice = PdfDocument.Open(second, leaveOpen: true);
        PdfDictionary resources = twice.Pages[0].Resources!;
        PdfDictionary gs = twice.Objects.ResolveAs<PdfDictionary>(resources[PdfName.Intern("ExtGState")])!;
        gs.ContainsKey(PdfName.Intern("CvStampGs0")).Should().BeTrue("the first pass's state is kept");
        gs.ContainsKey(PdfName.Intern("CvStampGs1")).Should().BeTrue("the second pass adds a new name");
    }

    [Fact]
    public void PlaceMany_InvalidRequest_ThrowsBeforeWriting()
    {
        using PdfDocument target = Open(BlankPages(1, 100, 100));
        using PdfDocument stamp = Open(SolidSquare(50, new Color(0, 0, 0)));
        using MemoryStream output = new MemoryStream();

        Action badOpacity = () => PageStamper.PlaceMany(output, target, new List<StampRequest>
        {
            new StampRequest(0, stamp, 0, Transform.Identity),
            new StampRequest(0, stamp, 0, Transform.Identity) { Opacity = 1.5 },
        });
        Action badPage = () => PageStamper.PlaceMany(output, target, new List<StampRequest>
        {
            new StampRequest(3, stamp, 0, Transform.Identity),
        });

        badOpacity.Should().Throw<ArgumentOutOfRangeException>();
        badPage.Should().Throw<ArgumentOutOfRangeException>();
        output.Length.Should().Be(0, "validation happens before anything is written");
    }

    // ── PDF-11: header/footer resolution ──────────────────────────────────

    [Fact]
    public void ResolveSegments_MatchesWhatApplyDraws()
    {
        using PdfDocument document = Open(BlankPages(3, 300, 400));
        HeaderFooterOptions options = new HeaderFooterOptions
        {
            Footer = new BandText(center: "Page {page} of {total}", right: "{filename}"),
            FilePath = "/tmp/report.pdf",
            Fit = PageContentFit.Overlay,
        };

        IReadOnlyList<HeaderFooterSegment> segments = HeaderFooter.ResolveSegments(document, 1, options);

        segments.Should().HaveCount(2);
        segments[0].Band.Should().Be(HeaderFooterBand.Footer);
        segments[0].Alignment.Should().Be(HeaderFooterAlignment.Center);
        segments[0].Text.Should().Be("Page 2 of 3");
        segments[0].BaselineY.Should().Be(18.0);
        (segments[0].X + (segments[0].Width / 2)).Should().BeApproximately(150, 1e-6, "centred on the page");
        segments[1].Text.Should().Be("report.pdf");
        (segments[1].X + segments[1].Width).Should().BeApproximately(300 - 36, 1e-6);

        using MemoryStream output = new MemoryStream();
        HeaderFooter.Apply(output, document, options);
        output.Position = 0;
        using PdfDocument applied = PdfDocument.Open(output, leaveOpen: true);
        string content = PageContent(applied, 1);
        content.Should().Contain("Page 2 of 3");
        content.Should().Contain(Fmt(segments[0].X) + " " + Fmt(segments[0].BaselineY),
            "Apply places the text exactly where ResolveSegments says");
    }

    [Fact]
    public void ResolveSegments_UntargetedPage_IsEmpty()
    {
        using PdfDocument document = Open(BlankPages(2, 300, 400));
        HeaderFooterOptions options = new HeaderFooterOptions
        {
            Header = new BandText(left: "{page}"),
            PageIndices = FirstPageOnly,
        };

        HeaderFooter.ResolveSegments(document, 1, options).Should().BeEmpty();
        HeaderFooter.ResolveSegments(document, 0, options).Should().ContainSingle();
    }

    [Fact]
    public void ResolveText_UsesTheSameTokenEngine()
    {
        HeaderFooterOptions options = new HeaderFooterOptions
        {
            Timestamp = new DateTimeOffset(2026, 10, 14, 9, 30, 0, TimeSpan.Zero),
        };

        HeaderFooter.ResolveText("{page:roman}/{total} {date:dd-MM-yyyy}", 1, 9, options)
            .Should().Be("ii/9 14-10-2026");
    }

    // ── PDF-06: imposition parity ─────────────────────────────────────────

    [Fact]
    public void NUp_CropBoxWithOffsetOrigin_FitsTheVisibleAreaExactly()
    {
        // The visible area is the crop box (100,100)-(300,400), filled blue;
        // the rest of the media box is red. A 1x1 sheet of the crop box's size
        // must show only blue, edge to edge.
        byte[] source = RawPdf.SinglePage(
            "/MediaBox[0 0 600 800]/CropBox[100 100 300 400]",
            "1 0 0 rg 0 0 600 800 re f 0 0 1 rg 100 100 200 300 re f");
        using PdfDocument document = Open(source);
        using MemoryStream output = new MemoryStream();

        Imposition.NUp(output, document, new NUpOptions
        {
            Rows = 1,
            Columns = 1,
            SheetSize = new PageSize(200, 300),
        });

        output.Position = 0;
        using PdfDocument sheet = PdfDocument.Open(output, leaveOpen: true);
        PixelBuffer pixels = Rasterize(sheet, 0);
        IsBlue(pixels, 3, 3).Should().BeTrue();
        IsBlue(pixels, 196, 296).Should().BeTrue();
        IsBlue(pixels, 100, 150).Should().BeTrue();
    }

    [Fact]
    public void NUp_RotatedSourcePage_IsPlacedUprightLikeTheViewerShowsIt()
    {
        // Unrotated page 200x100 with red on its left half; /Rotate 90 turns it
        // clockwise, so viewers show a 100x200 page with red on the TOP half.
        byte[] source = RawPdf.SinglePage(
            "/MediaBox[0 0 200 100]/Rotate 90",
            "1 0 0 rg 0 0 100 100 re f");
        using PdfDocument document = Open(source);
        PageRasterizer upright = new PageRasterizer(
            document.Objects, new RenderOptions { Dpi = 72, ApplyPageRotation = true });
        PixelBuffer direct = upright.Rasterize(document.Pages[0]);

        using MemoryStream output = new MemoryStream();
        Imposition.NUp(output, document, new NUpOptions
        {
            Rows = 1,
            Columns = 1,
            SheetSize = new PageSize(100, 200),
        });
        output.Position = 0;
        using PdfDocument sheet = PdfDocument.Open(output, leaveOpen: true);
        PixelBuffer imposed = Rasterize(sheet, 0);

        imposed.Width.Should().Be(direct.Width);
        imposed.Height.Should().Be(direct.Height);
        IsRed(imposed, 50, 40).Should().BeTrue("the top half is red, as displayed");
        IsWhite(imposed, 50, 160).Should().BeTrue("the bottom half is blank");
        foreach ((int x, int y) in new[] { (10, 10), (90, 90), (50, 110), (10, 190) })
        {
            imposed.GetPixelBgra(x, y).Should().Be(direct.GetPixelBgra(x, y),
                "imposition matches the renderer's /Rotate display");
        }
    }

    // ── rotation: view rotation and opt-in page /Rotate ───────────────────

    [Fact]
    public void Raster_DefaultIgnoresPageRotate_OptInAppliesIt()
    {
        byte[] source = RawPdf.SinglePage(
            "/MediaBox[0 0 200 100]/Rotate 90",
            "1 0 0 rg 0 0 100 100 re f");
        using PdfDocument document = Open(source);

        PixelBuffer legacy = Rasterize(document, 0);
        legacy.Width.Should().Be(200, "by default the unrotated user space renders, as before");

        PageRasterizer upright = new PageRasterizer(
            document.Objects, new RenderOptions { Dpi = 72, ApplyPageRotation = true });
        PixelBuffer rotated = upright.Rasterize(document.Pages[0]);
        rotated.Width.Should().Be(100);
        rotated.Height.Should().Be(200);
        IsRed(rotated, 50, 40).Should().BeTrue("the page's left half shows on top after a clockwise turn");
        IsWhite(rotated, 50, 160).Should().BeTrue();
    }

    [Fact]
    public void Raster_ViewRotation_TurnsTheBufferClockwise()
    {
        // Red square in the top-left corner (PDF y-up: top is y 50..100).
        byte[] source = RawPdf.SinglePage("/MediaBox[0 0 200 100]", "1 0 0 rg 0 50 50 50 re f");
        using PdfDocument document = Open(source);

        PageRasterizer r90 = new PageRasterizer(document.Objects, new RenderOptions { Dpi = 72, Rotation = 90 });
        PixelBuffer turned = r90.Rasterize(document.Pages[0]);

        turned.Width.Should().Be(100);
        turned.Height.Should().Be(200);
        IsRed(turned, 75, 25).Should().BeTrue("top-left moves to top-right under a clockwise turn");
        IsWhite(turned, 25, 25).Should().BeTrue();
    }

    [Fact]
    public void RenderOptions_RotationMustBeAMultipleOf90()
    {
        Action act = () => _ = new RenderOptions { Rotation = 45 };
        act.Should().Throw<ArgumentOutOfRangeException>();
        new RenderOptions { Rotation = -90 }.Rotation.Should().Be(270);
    }

    [Fact]
    public void PlanNUp_DescribesExactlyTheSheetsNUpWrites()
    {
        using PdfDocument document = Open(BlankPages(5, 300, 400));
        NUpOptions options = new NUpOptions { Rows = 2, Columns = 2, Margin = 10, Gutter = 6 };

        IReadOnlyList<ImpositionSheet> plan = Imposition.PlanNUp(document, options);
        using MemoryStream output = new MemoryStream();
        Imposition.NUp(output, document, options);
        output.Position = 0;
        using PdfDocument written = PdfDocument.Open(output, leaveOpen: true);

        plan.Should().HaveCount(written.PageCount);
        plan[0].Placements.Should().HaveCount(4);
        plan[1].Placements.Should().ContainSingle();
        plan[1].Placements[0].SourcePageIndex.Should().Be(4);
        plan[0].Placements[1].Cell.X.Should().BeGreaterThan(plan[0].Placements[0].Cell.Right,
            "row-major: the second cell is to the right of the first, past the gutter");
    }

    [Fact]
    public void PlanBooklet_UsesSaddleStitchOrder()
    {
        using PdfDocument document = Open(BlankPages(4, 300, 400));
        IReadOnlyList<ImpositionSheet> plan = Imposition.PlanBooklet(document, new BookletOptions());

        plan.Should().HaveCount(2);
        plan[0].Placements[0].SourcePageIndex.Should().Be(3, "front: page 4 left");
        plan[0].Placements[1].SourcePageIndex.Should().Be(0, "front: page 1 right");
        plan[1].Placements[0].SourcePageIndex.Should().Be(1, "back: page 2 left");
        plan[1].Placements[1].SourcePageIndex.Should().Be(2, "back: page 3 right");
    }

    [Fact]
    public void Imposition_InvalidGeometry_IsRejected()
    {
        using PdfDocument document = Open(BlankPages(1, 300, 400));

        Action tooWide = () => Imposition.PlanNUp(document, new NUpOptions { Margin = 400 });
        Action negative = () => Imposition.PlanNUp(document, new NUpOptions { Gutter = -1 });
        Action booklet = () => Imposition.PlanBooklet(document, new BookletOptions { Margin = 500 });

        tooWide.Should().Throw<ArgumentException>();
        negative.Should().Throw<ArgumentException>();
        booklet.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Imposition_EmptySource_IsRejected()
    {
        byte[] empty = RawPdf.Build(new List<byte[]>
        {
            RawPdf.Obj("<</Type/Catalog/Pages 2 0 R>>"),
            RawPdf.Obj("<</Type/Pages/Kids[]/Count 0>>"),
        });
        using PdfDocument document = Open(empty);

        Action act = () => Imposition.PlanNUp(document, new NUpOptions());
        act.Should().Throw<ArgumentException>();
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static PdfDocument Open(byte[] bytes) => PdfDocument.Open(new MemoryStream(bytes), leaveOpen: false);

    private static byte[] BlankPages(int count, double width, double height)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        for (int i = 0; i < count; i++)
        {
            builder.AddPage(new PageSize(width, height));
        }

        return builder.ToByteArray();
    }

    private static byte[] SolidSquare(double size, Color color)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        builder.AddPage(new PageSize(size, size)).DrawRectangle(0, 0, size, size, fill: color);
        return builder.ToByteArray();
    }

    private static PixelBuffer Rasterize(byte[] pdf, int page)
    {
        using PdfDocument document = Open(pdf);
        return Rasterize(document, page);
    }

    private static PixelBuffer Rasterize(PdfDocument document, int page, bool gammaCorrect = true)
    {
        PageRasterizer rasterizer = new PageRasterizer(
            document.Objects, new RenderOptions { Dpi = 72, GammaCorrect = gammaCorrect });
        return rasterizer.Rasterize(document.Pages[page]);
    }

    private static string PageContent(PdfDocument document, int page)
    {
        FilterPipeline pipeline = new FilterPipeline();
        StringBuilder sb = new StringBuilder();
        PdfPrimitive? contents = document.Pages[page].Contents;
        List<PdfPrimitive> parts = new List<PdfPrimitive>();
        if (contents is not null && document.Objects.Resolve(contents) is PdfArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                parts.Add(array[i]);
            }
        }
        else if (contents is not null)
        {
            parts.Add(contents);
        }

        foreach (PdfPrimitive part in parts)
        {
            if (document.Objects.Resolve(part) is PdfStream stream)
            {
                byte[] bytes = stream.Filter is PdfName filter
                    ? pipeline.Decode(filter.Value, stream.RawBytes)
                    : stream.RawBytes;
                sb.Append(Encoding.Latin1.GetString(bytes)).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string Fmt(double value) =>
        value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsRed(PixelBuffer p, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = p.GetPixelBgra(x, y);
        return r > 200 && g < 80 && b < 80;
    }

    private static bool IsBlue(PixelBuffer p, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = p.GetPixelBgra(x, y);
        return b > 200 && g < 80 && r < 80;
    }

    private static bool IsWhite(PixelBuffer p, int x, int y)
    {
        (byte b, byte g, byte r, byte _) = p.GetPixelBgra(x, y);
        return r > 240 && g > 240 && b > 240;
    }
}
