// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 3 — vector extraction accessors.

using System;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.DisplayList.Tests;

public sealed class RenderOpBoundsTests
{
    private static PathGeometry Rectangle() => new PathGeometry()
        .MoveTo(2, 3).LineTo(12, 3).LineTo(12, 9).LineTo(2, 9).Close();

    [Fact]
    public void PathOp_Bounds_MatchesGeometry()
    {
        PathOp op = new PathOp { Geometry = Rectangle(), Mode = PaintMode.Fill };

        op.Bounds().Should().Be(new Rect(2, 3, 10, 6));
    }

    [Fact]
    public void ClipOp_Bounds_MatchesGeometry()
    {
        ClipOp op = new ClipOp { Geometry = Rectangle() };

        op.Bounds().Should().Be(new Rect(2, 3, 10, 6));
    }

    [Fact]
    public void ImageOp_Bounds_FromAxisAlignedTransform()
    {
        ImageOp op = new ImageOp
        {
            PixelData = Array.Empty<byte>(),
            Format = ImageFormat.Raw,
            Width = 1,
            Height = 1,
            Transform = new AffineMatrix(100, 0, 0, 50, 10, 20),
        };

        op.Bounds().Should().Be(new Rect(10, 20, 100, 50));
    }

    [Fact]
    public void ImageOp_Bounds_FromRotatedTransform()
    {
        // 90-degree rotation: (x,y) -> (-y, x).
        ImageOp op = new ImageOp
        {
            PixelData = Array.Empty<byte>(),
            Format = ImageFormat.Raw,
            Width = 1,
            Height = 1,
            Transform = new AffineMatrix(0, 1, -1, 0, 0, 0),
        };

        op.Bounds().Should().Be(new Rect(-1, 0, 1, 1));
    }

    [Fact]
    public void TryGetBounds_ReturnsValueForDrawingOps_NullOtherwise()
    {
        PathOp path = new PathOp { Geometry = Rectangle(), Mode = PaintMode.Stroke };
        ImageOp image = new ImageOp
        {
            PixelData = Array.Empty<byte>(),
            Format = ImageFormat.Raw,
            Width = 1,
            Height = 1,
            Transform = new AffineMatrix(100, 0, 0, 50, 10, 20),
        };
        TransformOp transform = new TransformOp { Push = true };

        path.TryGetBounds().Should().Be(new Rect(2, 3, 10, 6));
        image.TryGetBounds().Should().Be(new Rect(10, 20, 100, 50));
        ((RenderOp)transform).TryGetBounds().Should().BeNull();
    }
}
