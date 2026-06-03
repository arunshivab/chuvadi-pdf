// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3, §8.5, §8.6
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics tests

using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Graphics.Tests;

// ── PointF ────────────────────────────────────────────────────────────────

public sealed class PointFTests
{
    [Fact]
    public void Constructor_SetsXY()
    {
        PointF p = new PointF(3.0, 4.0);
        p.X.Should().Be(3.0);
        p.Y.Should().Be(4.0);
    }

    [Fact]
    public void Translate_AddsOffset()
    {
        PointF p = new PointF(1, 2);
        PointF result = p.Translate(3, 4);
        result.X.Should().Be(4);
        result.Y.Should().Be(6);
    }

    [Fact]
    public void DistanceTo_PythagoreanTriple()
    {
        PointF a = new PointF(0, 0);
        PointF b = new PointF(3, 4);
        a.DistanceTo(b).Should().BeApproximately(5.0, 1e-10);
    }

    [Fact]
    public void Equality_SameValues_IsEqual()
    {
        PointF a = new PointF(1, 2);
        PointF b = new PointF(1, 2);
        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Zero_IsOrigin()
    {
        PointF.Zero.X.Should().Be(0);
        PointF.Zero.Y.Should().Be(0);
    }
}

// ── SizeF ─────────────────────────────────────────────────────────────────

public sealed class SizeFTests
{
    [Fact]
    public void Constructor_SetsWidthHeight()
    {
        SizeF s = new SizeF(100, 200);
        s.Width.Should().Be(100);
        s.Height.Should().Be(200);
    }

    [Fact]
    public void Constructor_NegativeWidth_Throws()
    {
        Action act = () => new SizeF(-1, 10);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Zero_IsEmpty()
    {
        SizeF.Zero.IsEmpty.Should().BeTrue();
    }
}

// ── RectangleF ────────────────────────────────────────────────────────────

public sealed class RectangleFTests
{
    [Fact]
    public void Properties_Correct()
    {
        RectangleF r = new RectangleF(10, 20, 100, 50);
        r.Right.Should().Be(110);
        r.Top.Should().Be(70);
    }

    [Fact]
    public void Contains_InsidePoint_ReturnsTrue()
    {
        RectangleF r = new RectangleF(0, 0, 100, 100);
        r.Contains(new PointF(50, 50)).Should().BeTrue();
    }

    [Fact]
    public void Contains_OutsidePoint_ReturnsFalse()
    {
        RectangleF r = new RectangleF(0, 0, 100, 100);
        r.Contains(new PointF(150, 50)).Should().BeFalse();
    }

    [Fact]
    public void Intersect_Overlapping_ReturnsOverlap()
    {
        RectangleF a = new RectangleF(0, 0, 100, 100);
        RectangleF b = new RectangleF(50, 50, 100, 100);
        RectangleF intersection = a.Intersect(b);
        intersection.Width.Should().BeApproximately(50, 1e-10);
        intersection.Height.Should().BeApproximately(50, 1e-10);
    }

    [Fact]
    public void Intersect_NonOverlapping_ReturnsZero()
    {
        RectangleF a = new RectangleF(0, 0, 10, 10);
        RectangleF b = new RectangleF(100, 100, 10, 10);
        a.Intersect(b).Should().Be(RectangleF.Zero);
    }

    [Fact]
    public void FromCorners_NormalisesCorners()
    {
        RectangleF r = RectangleF.FromCorners(100, 100, 0, 0);
        r.X.Should().Be(0);
        r.Y.Should().Be(0);
        r.Width.Should().Be(100);
        r.Height.Should().Be(100);
    }

    [Fact]
    public void Inflate_ExpandsOnAllSides()
    {
        RectangleF r = new RectangleF(10, 10, 100, 100);
        RectangleF inflated = r.Inflate(5);
        inflated.X.Should().Be(5);
        inflated.Y.Should().Be(5);
        inflated.Width.Should().Be(110);
        inflated.Height.Should().Be(110);
    }
}

// ── ColorF ────────────────────────────────────────────────────────────────

public sealed class ColorFTests
{
    [Fact]
    public void FromGray_SetsComponents()
    {
        ColorF c = ColorF.FromGray(0.5f);
        c.Space.Should().Be(ColorSpace.Gray);
        c.Gray.Should().BeApproximately(0.5f, 1e-6f);
        c.Alpha.Should().BeApproximately(1f, 1e-6f);
    }

    [Fact]
    public void FromRgb_SetsComponents()
    {
        ColorF c = ColorF.FromRgb(1f, 0.5f, 0f);
        c.Space.Should().Be(ColorSpace.Rgb);
        c.R.Should().BeApproximately(1f, 1e-6f);
        c.G.Should().BeApproximately(0.5f, 1e-6f);
        c.B.Should().BeApproximately(0f, 1e-6f);
    }

    [Fact]
    public void Clamp_OutOfRange_IsClipped()
    {
        ColorF c = ColorF.FromRgb(2f, -1f, 0.5f);
        c.R.Should().BeApproximately(1f, 1e-6f);
        c.G.Should().BeApproximately(0f, 1e-6f);
    }

    [Fact]
    public void Black_IsGrayZero()
    {
        ColorF.Black.Gray.Should().Be(0f);
        ColorF.Black.Alpha.Should().Be(1f);
    }

    [Fact]
    public void ToRgb_Gray_ProducesEqualRGB()
    {
        ColorF gray = ColorF.FromGray(0.5f);
        ColorF rgb = gray.ToRgb();
        rgb.R.Should().BeApproximately(0.5f, 1e-6f);
        rgb.G.Should().BeApproximately(0.5f, 1e-6f);
        rgb.B.Should().BeApproximately(0.5f, 1e-6f);
    }

    [Fact]
    public void ToArgb32_White_IsCorrect()
    {
        uint argb = ColorF.White.ToArgb32();
        argb.Should().Be(0xFFFFFFFF);
    }

    [Fact]
    public void ToArgb32_Black_IsCorrect()
    {
        uint argb = ColorF.Black.ToArgb32();
        argb.Should().Be(0xFF000000);
    }
}

// ── Transform ─────────────────────────────────────────────────────────────

public sealed class TransformTests
{
    [Fact]
    public void Identity_IsIdentity()
    {
        Transform.Identity.IsIdentity.Should().BeTrue();
    }

    [Fact]
    public void CreateTranslation_TranslatesPoint()
    {
        Transform t = Transform.CreateTranslation(10, 20);
        PointF result = t.TransformPoint(new PointF(5, 5));
        result.X.Should().BeApproximately(15, 1e-10);
        result.Y.Should().BeApproximately(25, 1e-10);
    }

    [Fact]
    public void CreateScale_ScalesPoint()
    {
        Transform t = Transform.CreateScale(2.0);
        PointF result = t.TransformPoint(new PointF(3, 4));
        result.X.Should().BeApproximately(6, 1e-10);
        result.Y.Should().BeApproximately(8, 1e-10);
    }

    [Fact]
    public void CreateRotation_90Degrees_RotatesCorrectly()
    {
        Transform t = Transform.CreateRotationDegrees(90);
        PointF result = t.TransformPoint(new PointF(1, 0));
        result.X.Should().BeApproximately(0, 1e-10);
        result.Y.Should().BeApproximately(1, 1e-10);
    }

    [Fact]
    public void Multiply_IdentityByIdentity_IsIdentity()
    {
        Transform result = Transform.Identity * Transform.Identity;
        result.IsIdentity.Should().BeTrue();
    }

    [Fact]
    public void Invert_Translation_ProducesNegativeTranslation()
    {
        Transform t = Transform.CreateTranslation(10, 20);
        Transform inv = t.Invert();
        PointF result = inv.TransformPoint(new PointF(10, 20));
        result.X.Should().BeApproximately(0, 1e-10);
        result.Y.Should().BeApproximately(0, 1e-10);
    }

    [Fact]
    public void Invert_Singular_Throws()
    {
        Transform t = new Transform(0, 0, 0, 0, 0, 0);
        Action act = () => t.Invert();
        act.Should().Throw<InvalidOperationException>();
    }
}

// ── PathSegment ───────────────────────────────────────────────────────────

public sealed class PathSegmentTests
{
    [Fact]
    public void MoveTo_HasCorrectKind()
    {
        PathSegment s = PathSegment.MoveTo(1, 2);
        s.Kind.Should().Be(PathSegmentKind.MoveTo);
        s.P0.X.Should().Be(1);
        s.P0.Y.Should().Be(2);
    }

    [Fact]
    public void CubicBezierTo_HasThreePoints()
    {
        PathSegment s = PathSegment.CubicBezierTo(
            new PointF(1, 2), new PointF(3, 4), new PointF(5, 6));
        s.Kind.Should().Be(PathSegmentKind.CubicBezierTo);
        s.P0.X.Should().Be(1);
        s.P1.X.Should().Be(3);
        s.P2.X.Should().Be(5);
    }
}

// ── Path ──────────────────────────────────────────────────────────────────

public sealed class PathTests
{
    [Fact]
    public void Empty_Path_IsEmpty()
    {
        Path p = new Path();
        p.IsEmpty.Should().BeTrue();
        p.Count.Should().Be(0);
    }

    [Fact]
    public void MoveTo_LineTo_ProducesTwoSegments()
    {
        Path p = new Path();
        p.MoveTo(0, 0).LineTo(100, 100);
        p.Count.Should().Be(2);
    }

    [Fact]
    public void LineTo_WithoutMoveTo_Throws()
    {
        Path p = new Path();
        Action act = () => p.LineTo(10, 10);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rectangle_ProducesFiveSegments()
    {
        Path p = new Path();
        p.Rectangle(0, 0, 100, 100);
        p.Count.Should().Be(5); // MoveTo + 3 LineTo + ClosePath
    }

    [Fact]
    public void Clear_ResetsPath()
    {
        Path p = new Path();
        p.MoveTo(0, 0).LineTo(1, 1);
        p.Clear();
        p.IsEmpty.Should().BeTrue();
    }
}

// ── PixelBuffer ───────────────────────────────────────────────────────────

public sealed class PixelBufferTests
{
    [Fact]
    public void Constructor_SetsWidthHeight()
    {
        PixelBuffer buf = new PixelBuffer(800, 600);
        buf.Width.Should().Be(800);
        buf.Height.Should().Be(600);
        buf.ByteCount.Should().Be(800 * 600 * 4);
    }

    [Fact]
    public void Constructor_NegativeWidth_Throws()
    {
        Action act = () => new PixelBuffer(-1, 100);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetPixel_GetPixel_RoundTrips()
    {
        PixelBuffer buf = new PixelBuffer(10, 10);
        buf.SetPixel(5, 5, ColorF.FromRgb8(255, 128, 0));
        (byte b, byte g, byte r, byte a) = buf.GetPixelBgra(5, 5);
        r.Should().BeInRange(254, 255);
        g.Should().BeInRange(127, 129);
        b.Should().Be(0);
        a.Should().Be(255);
    }

    [Fact]
    public void SetPixel_OutOfRange_DoesNotThrow()
    {
        PixelBuffer buf = new PixelBuffer(10, 10);
        Action act = () => buf.SetPixel(100, 100, ColorF.Black);
        act.Should().NotThrow();
    }

    [Fact]
    public void ClearWhite_AllPixelsAreWhite()
    {
        PixelBuffer buf = new PixelBuffer(4, 4);
        buf.ClearWhite();
        (byte b, byte g, byte r, byte a) = buf.GetPixelBgra(2, 2);
        r.Should().Be(255);
        g.Should().Be(255);
        b.Should().Be(255);
        a.Should().Be(255);
    }

    [Fact]
    public void BlendPixel_FullyOpaque_OverwritesPixel()
    {
        PixelBuffer buf = new PixelBuffer(10, 10);
        buf.ClearWhite();
        buf.BlendPixel(5, 5, ColorF.Black);
        (byte b, byte g, byte r, byte a) = buf.GetPixelBgra(5, 5);
        r.Should().Be(0);
        g.Should().Be(0);
        b.Should().Be(0);
    }

    [Fact]
    public void BlendPixel_GammaCorrect_ProducesLighterEdgeThanNaive()
    {
        // Blending 50%-alpha black over white in linear light leaves more of
        // the white background showing through than a naive sRGB-space blend,
        // so the gamma-correct result is the lighter (higher) byte value.
        PixelBuffer gamma = new PixelBuffer(4, 4);
        gamma.ClearWhite();
        gamma.BlendPixel(2, 2, ColorF.FromRgb(0f, 0f, 0f, 0.5f), gammaCorrect: true);

        PixelBuffer naive = new PixelBuffer(4, 4);
        naive.ClearWhite();
        naive.BlendPixel(2, 2, ColorF.FromRgb(0f, 0f, 0f, 0.5f), gammaCorrect: false);

        (byte gb, byte gg, byte gr, byte _) = gamma.GetPixelBgra(2, 2);
        (byte nb, byte ng, byte nr, byte _) = naive.GetPixelBgra(2, 2);

        gr.Should().BeGreaterThan(nr);
        gg.Should().BeGreaterThan(ng);
        gb.Should().BeGreaterThan(nb);
    }

    [Fact]
    public void BlendPixel_GammaCorrect_MatchesLinearLightValue()
    {
        // 50%-alpha black over white, mixed in linear light: out_linear = 0.5,
        // re-encoded to sRGB = 188 (+/- 1 for rounding). The naive sRGB blend
        // would give 128, so this pins the gamma-correct math specifically.
        PixelBuffer buf = new PixelBuffer(4, 4);
        buf.ClearWhite();
        buf.BlendPixel(2, 2, ColorF.FromRgb(0f, 0f, 0f, 0.5f), gammaCorrect: true);

        (byte b, byte g, byte r, byte _) = buf.GetPixelBgra(2, 2);
        r.Should().BeInRange(187, 189);
        g.Should().BeInRange(187, 189);
        b.Should().BeInRange(187, 189);
    }
}

// ── PathFlattener ─────────────────────────────────────────────────────────

public sealed class PathFlattenerTests
{
    [Fact]
    public void Constructor_NegativeFlatness_Throws()
    {
        Action act = () => new PathFlattener(-1.0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Flatten_EmptyPath_ReturnsEmpty()
    {
        PathFlattener f = new PathFlattener();
        List<List<PointF>> result = f.Flatten(new Path());
        result.Should().BeEmpty();
    }

    [Fact]
    public void Flatten_Rectangle_ProducesFourPoints()
    {
        PathFlattener f = new PathFlattener(0.25);
        Path p = new Path();
        p.Rectangle(0, 0, 100, 100);
        List<List<PointF>> subpaths = f.Flatten(p);
        subpaths.Should().HaveCount(1);
        // Rectangle: 4 corners + closing back to start = 5 points
        subpaths[0].Count.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Flatten_NullPath_Throws()
    {
        PathFlattener f = new PathFlattener();
        Action act = () => f.Flatten(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Flatten_StraightLine_ProducesTwoPoints()
    {
        PathFlattener f = new PathFlattener(0.25);
        Path p = new Path();
        p.MoveTo(0, 0).LineTo(100, 0);
        List<List<PointF>> subpaths = f.Flatten(p);
        subpaths.Should().HaveCount(1);
        subpaths[0].Should().HaveCount(2);
    }

    [Fact]
    public void Flatten_CubicBezier_ProducesMultiplePoints()
    {
        PathFlattener f = new PathFlattener(0.25);
        Path p = new Path();
        // A clearly curved cubic bezier
        p.MoveTo(0, 0).CubicBezierTo(
            new PointF(33, 100),
            new PointF(66, 100),
            new PointF(100, 0));
        List<List<PointF>> subpaths = f.Flatten(p);
        subpaths.Should().HaveCount(1);
        subpaths[0].Count.Should().BeGreaterThan(2);
    }

    [Fact]
    public void Flatten_Ellipse_ProducesReasonablePointCount()
    {
        PathFlattener f = new PathFlattener(0.5);
        Path p = new Path();
        p.Ellipse(50, 50, 40, 30);
        List<List<PointF>> subpaths = f.Flatten(p);
        subpaths.Should().HaveCount(1);
        // Ellipse should produce a reasonable number of points (not too few, not too many)
        subpaths[0].Count.Should().BeGreaterThan(8);
        subpaths[0].Count.Should().BeLessThan(200);
    }
}
