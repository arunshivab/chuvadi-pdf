// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 3 — vector extraction accessors.

using System.Linq;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.DisplayList.Tests;

public sealed class PathGeometryAccessorsTests
{
    [Fact]
    public void Flatten_Line_KeepsEndpoints()
    {
        PathGeometry geometry = new PathGeometry().MoveTo(0, 0).LineTo(10, 5);

        var subpaths = geometry.Flatten();

        subpaths.Should().ContainSingle();
        subpaths[0].Should().HaveCount(2);
        subpaths[0][0].Should().Be((0.0, 0.0));
        subpaths[0][1].Should().Be((10.0, 5.0));
    }

    [Fact]
    public void Flatten_Cubic_ApproximatesCurve()
    {
        // Control points (0,0)(0,10)(10,10)(10,0): the curve peaks at y = 7.5.
        PathGeometry geometry = new PathGeometry().MoveTo(0, 0).CubicTo(0, 10, 10, 10, 10, 0);

        var points = geometry.Flatten(0.05)[0];

        points.Count.Should().BeGreaterThan(2);
        points[0].Should().Be((0.0, 0.0));
        points[^1].Should().Be((10.0, 0.0));
        points.Max(p => p.Y).Should().BeApproximately(7.5, 0.2);
    }

    [Fact]
    public void Flatten_ClosedSubpath_RepeatsStart()
    {
        PathGeometry geometry = new PathGeometry().MoveTo(0, 0).LineTo(10, 0).LineTo(5, 10).Close();

        var points = geometry.Flatten()[0];

        points.Should().HaveCount(4);
        points[^1].Should().Be((0.0, 0.0));
    }

    [Fact]
    public void Bounds_Rectangle_IsTight()
    {
        PathGeometry geometry = new PathGeometry()
            .MoveTo(2, 3).LineTo(12, 3).LineTo(12, 9).LineTo(2, 9).Close();

        geometry.Bounds().Should().Be(new Rect(2, 3, 10, 6));
    }

    [Fact]
    public void Bounds_Empty_IsZero()
    {
        new PathGeometry().Bounds().Should().Be(new Rect(0, 0, 0, 0));
    }

    [Fact]
    public void SignedArea_CounterClockwiseSquare_IsPositive()
    {
        PathGeometry geometry = new PathGeometry()
            .MoveTo(0, 0).LineTo(1, 0).LineTo(1, 1).LineTo(0, 1).Close();

        geometry.SignedArea().Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void SignedArea_ClockwiseSquare_IsNegative()
    {
        PathGeometry geometry = new PathGeometry()
            .MoveTo(0, 0).LineTo(0, 1).LineTo(1, 1).LineTo(1, 0).Close();

        geometry.SignedArea().Should().BeApproximately(-1.0, 1e-9);
    }

    [Fact]
    public void Contains_PointInsideAndOutsideSquare()
    {
        PathGeometry geometry = new PathGeometry()
            .MoveTo(0, 0).LineTo(2, 0).LineTo(2, 2).LineTo(0, 2).Close();

        geometry.Contains(1, 1, FillRule.NonZero).Should().BeTrue();
        geometry.Contains(1, 1, FillRule.EvenOdd).Should().BeTrue();
        geometry.Contains(3, 3, FillRule.NonZero).Should().BeFalse();
    }

    [Fact]
    public void Contains_NestedSameWinding_DistinguishesFillRules()
    {
        // Outer and inner squares share winding (both counter-clockwise).
        PathGeometry geometry = new PathGeometry()
            .MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).LineTo(0, 10).Close()
            .MoveTo(3, 3).LineTo(7, 3).LineTo(7, 7).LineTo(3, 7).Close();

        // Inside the inner square: even-odd sees two boundaries (outside),
        // non-zero accumulates winding 2 (inside).
        geometry.Contains(5, 5, FillRule.EvenOdd).Should().BeFalse();
        geometry.Contains(5, 5, FillRule.NonZero).Should().BeTrue();

        // Between the squares: both rules agree it is inside.
        geometry.Contains(1, 1, FillRule.EvenOdd).Should().BeTrue();
        geometry.Contains(1, 1, FillRule.NonZero).Should().BeTrue();
    }
}
