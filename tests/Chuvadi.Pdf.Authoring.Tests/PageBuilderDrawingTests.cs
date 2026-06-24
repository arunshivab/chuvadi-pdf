// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5 (path painting), §8.4.5 (/ca constant alpha)
// Coverage for PageBuilder.DrawPath (lines / cubic Béziers, non-zero and
// even-odd filling, stroke) and the image-overlay opacity overloads of
// DrawImage (constant alpha via an /ExtGState resource).

using System;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;
using Path = Chuvadi.Pdf.Graphics.Path;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class PageBuilderDrawingTests
{
    private static readonly byte[] OnePixelPng = BuildSolidPng();

    [Fact]
    public void DrawPath_NonZeroFill_EmitsCloseAndFillOps()
    {
        byte[] pdf = Build(p => p.DrawPath(
            new Path().MoveTo(100, 100).LineTo(200, 100).LineTo(150, 180).ClosePath(),
            fill: new Color(0, 0, 1)));

        string[] ops = Ops(pdf);
        ops.Should().Contain("h");
        ops.Should().Contain("f");
        ops.Should().NotContain("f*");
        ops.Any(o => o.EndsWith(" m", StringComparison.Ordinal)).Should().BeTrue();
        ops.Any(o => o.EndsWith(" l", StringComparison.Ordinal)).Should().BeTrue();
    }

    [Fact]
    public void DrawPath_EvenOddFill_EmitsStarFillOp()
    {
        byte[] pdf = Build(p => p.DrawPath(
            new Path().Rectangle(300, 100, 120, 120).Rectangle(335, 135, 50, 50),
            fill: new Color(0, 0.6, 0),
            fillRule: FillRule.EvenOdd));

        Ops(pdf).Should().Contain("f*");
    }

    [Fact]
    public void DrawPath_FillAndStroke_NonZero_EmitsB()
    {
        byte[] pdf = Build(p => p.DrawPath(
            new Path().MoveTo(10, 10).LineTo(40, 10).LineTo(25, 40).ClosePath(),
            fill: new Color(0, 0, 1), stroke: new Color(0, 0, 0)));

        Ops(pdf).Should().Contain("B");
    }

    [Fact]
    public void DrawPath_FillAndStroke_EvenOdd_EmitsStarB()
    {
        byte[] pdf = Build(p => p.DrawPath(
            new Path().Rectangle(10, 10, 40, 40).Rectangle(20, 20, 10, 10),
            fill: new Color(0, 0, 1), stroke: new Color(0, 0, 0),
            fillRule: FillRule.EvenOdd));

        Ops(pdf).Should().Contain("B*");
    }

    [Fact]
    public void DrawPath_StrokeOnlyBezier_EmitsCurveAndStroke()
    {
        byte[] pdf = Build(p => p.DrawPath(
            new Path()
                .MoveTo(100, 300)
                .CubicBezierTo(new PointF(150, 220), new PointF(250, 380), new PointF(300, 300)),
            stroke: new Color(0, 0, 0), strokeWidth: 3));

        string[] ops = Ops(pdf);
        ops.Should().Contain("S");
        ops.Any(o => o.EndsWith(" c", StringComparison.Ordinal)).Should().BeTrue();
        ops.Should().NotContain("f");
    }

    [Fact]
    public void DrawPath_EmptyPath_DrawsNothing()
    {
        byte[] before = Build(_ => { });
        byte[] after = Build(p => p.DrawPath(new Path(), fill: new Color(1, 0, 0)));

        Ops(after).Should().BeEquivalentTo(Ops(before));
    }

    [Fact]
    public void DrawPath_NoFillNoStroke_DrawsNothing()
    {
        byte[] before = Build(_ => { });
        byte[] after = Build(p => p.DrawPath(
            new Path().MoveTo(0, 0).LineTo(10, 10)));

        Ops(after).Should().BeEquivalentTo(Ops(before));
    }

    [Fact]
    public void DrawPath_NullPath_Throws()
    {
        PdfDocumentBuilder doc = PdfDocumentBuilder.Create();
        PageBuilder page = doc.AddPage(PageSize.A4);

        Action act = () => page.DrawPath(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DrawImage_WithOpacity_EmitsGsOpAndExtGStateResource()
    {
        byte[] pdf = Build(p => p.DrawImage(OnePixelPng, 100, 100, 50, 50, 0.30));

        Ops(pdf).Should().Contain("/GsA0 gs");
        ImageAlpha(pdf, "GsA0").Should().BeApproximately(0.30, 0.0001);
    }

    [Fact]
    public void DrawImage_RepeatedOpacity_SharesOneExtGState()
    {
        byte[] pdf = Build(p =>
        {
            p.DrawImage(OnePixelPng, 100, 100, 50, 50, 0.30);
            p.DrawImage(OnePixelPng, 200, 100, 50, 50, 0.30);
        });

        string[] ops = Ops(pdf);
        ops.Count(o => o == "/GsA0 gs").Should().Be(2);
        ops.Should().NotContain("/GsA1 gs");
    }

    [Fact]
    public void DrawImage_WithoutOpacity_HasNoExtGState()
    {
        byte[] pdf = Build(p => p.DrawImage(OnePixelPng, 100, 100, 50, 50));

        Ops(pdf).Any(o => o.EndsWith(" gs", StringComparison.Ordinal)).Should().BeFalse();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void DrawImage_OpacityOutOfRange_Throws(double opacity)
    {
        PdfDocumentBuilder doc = PdfDocumentBuilder.Create();
        PageBuilder page = doc.AddPage(PageSize.A4);

        Action act = () => page.DrawImage(OnePixelPng, 0, 0, 10, 10, opacity);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static byte[] Build(Action<PageBuilder> draw)
    {
        PdfDocumentBuilder doc = PdfDocumentBuilder.Create();
        PageBuilder page = doc.AddPage(PageSize.A4);
        draw(page);
        return doc.ToByteArray();
    }

    private static string[] Ops(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        PdfPrimitive contents = doc.Pages[0].Contents
            ?? throw new InvalidOperationException("page has no /Contents");
        PdfStream stream = (PdfStream)doc.Objects.Resolve(contents);
        string text = Encoding.Latin1.GetString(stream.RawBytes);
        return text.Replace("\r", string.Empty).Split('\n').Select(o => o.Trim()).ToArray();
    }

    private static double? ImageAlpha(byte[] pdf, string gsKey)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(pdf), leaveOpen: false);
        PdfDictionary? resources = doc.Pages[0].Resources;
        PdfDictionary? extGState = resources?.GetAs<PdfDictionary>(PdfName.Intern("ExtGState"));
        PdfDictionary? gs = extGState?.GetAs<PdfDictionary>(PdfName.Intern(gsKey));
        PdfReal? ca = gs?.GetAs<PdfReal>(PdfName.Intern("ca"));
        return ca?.Value;
    }

    // A minimal valid 1x1 opaque RGB PNG so DrawImage has real bytes to embed.
    private static byte[] BuildSolidPng()
    {
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGNgYGAAAAAEAAH2FzhVAAAAAElFTkSuQmCC");
    }
}
