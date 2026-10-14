// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for PDF-04: shared arrowhead geometry.

using System;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Graphics.Tests;

public sealed class ArrowGeometryTests
{
    [Fact]
    public void HeadPoints_HorizontalArrow_ProducesSymmetricTriangleAtTip()
    {
        PointF[] head = ArrowGeometry.HeadPoints(new PointF(0, 0), new PointF(100, 0), 10, 8);

        head.Should().HaveCount(3);
        head[0].Should().Be(new PointF(100, 0));
        head[1].X.Should().BeApproximately(90, 1e-9);
        head[2].X.Should().BeApproximately(90, 1e-9);
        Math.Abs(head[1].Y).Should().BeApproximately(4, 1e-9);
        (head[1].Y + head[2].Y).Should().BeApproximately(0, 1e-9, "the base is centred on the shaft");
    }

    [Fact]
    public void HeadPoints_DiagonalArrow_BaseIsPerpendicularToShaft()
    {
        PointF tail = new PointF(10, 10);
        PointF tip = new PointF(40, 50);
        PointF[] head = ArrowGeometry.HeadPoints(tail, tip, 10, 6);

        // Base direction dotted with shaft direction is zero.
        double bx = head[1].X - head[2].X;
        double by = head[1].Y - head[2].Y;
        double sx = tip.X - tail.X;
        double sy = tip.Y - tail.Y;
        ((bx * sx) + (by * sy)).Should().BeApproximately(0, 1e-9);
        head[1].DistanceTo(head[2]).Should().BeApproximately(6, 1e-9);
    }

    [Fact]
    public void HeadPoints_ShortArrow_ClampsHeadToShaftLengthAndScalesWidth()
    {
        PointF[] head = ArrowGeometry.HeadPoints(new PointF(0, 0), new PointF(5, 0), 10, 8);

        head[1].X.Should().BeApproximately(0, 1e-9, "the head never overshoots the tail");
        head[1].DistanceTo(head[2]).Should().BeApproximately(4, 1e-9, "width scales with the clamped length");
    }

    [Fact]
    public void HeadPoints_ZeroLengthArrow_ReturnsEmpty()
    {
        ArrowGeometry.HeadPoints(new PointF(3, 3), new PointF(3, 3), 10, 8).Should().BeEmpty();
    }

    [Fact]
    public void ShaftEnd_StopsAtHeadBase()
    {
        PointF end = ArrowGeometry.ShaftEnd(new PointF(0, 0), new PointF(0, 100), 12);
        end.X.Should().BeApproximately(0, 1e-9);
        end.Y.Should().BeApproximately(88, 1e-9);
    }

    [Fact]
    public void HeadPoints_NegativeSize_Throws()
    {
        Action act = () => ArrowGeometry.HeadPoints(new PointF(0, 0), new PointF(1, 0), -1, 2);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Defaults_ScaleWithStrokeWidth()
    {
        ArrowGeometry.DefaultHeadLength(0.5).Should().Be(6.0, "never shorter than 6");
        ArrowGeometry.DefaultHeadLength(4).Should().Be(16.0);
        ArrowGeometry.DefaultHeadWidth(16).Should().Be(12.0);
    }

    [Fact]
    public void PathArrowHead_AppendsClosedTriangle()
    {
        Path path = new Path().ArrowHead(new PointF(0, 0), new PointF(50, 0), 10, 8);

        path.Segments.Should().HaveCount(4);
        path.Segments[0].Kind.Should().Be(PathSegmentKind.MoveTo);
        path.Segments[0].P0.Should().Be(new PointF(50, 0));
        path.Segments[3].Kind.Should().Be(PathSegmentKind.ClosePath);
    }

    [Fact]
    public void PathArrowHead_ZeroLength_AppendsNothing()
    {
        new Path().ArrowHead(new PointF(1, 1), new PointF(1, 1), 10, 8).IsEmpty.Should().BeTrue();
    }
}
