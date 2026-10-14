// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for v3.20: four-point perspective correction (deskew).

using System;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Images.Tests;

public sealed class PerspectiveCorrectionTests
{
    [Fact]
    public void FullImageCorners_ReproduceTheImage()
    {
        ImageFrame source = Gradient(64, 48);

        ImageFrame result = PerspectiveCorrection.Correct(
            source, new PointF(0, 0), new PointF(64, 0), new PointF(64, 48), new PointF(0, 48));

        result.Width.Should().Be(64);
        result.Height.Should().Be(48);
        for (int y = 0; y < 48; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                result.Pixels.GetPixelBgra(x, y).Should().Be(source.Pixels.GetPixelBgra(x, y));
            }
        }
    }

    [Fact]
    public void AxisAlignedQuad_IsAnExactCrop()
    {
        ImageFrame source = Gradient(100, 100);

        ImageFrame result = PerspectiveCorrection.Correct(
            source, new PointF(20, 30), new PointF(70, 30), new PointF(70, 90), new PointF(20, 90));

        result.Width.Should().Be(50);
        result.Height.Should().Be(60);
        result.Pixels.GetPixelBgra(0, 0).Should().Be(source.Pixels.GetPixelBgra(20, 30));
        result.Pixels.GetPixelBgra(49, 59).Should().Be(source.Pixels.GetPixelBgra(69, 89));
    }

    [Fact]
    public void Trapezoid_IsStraightened_WithPerspectiveNotLinearSpacing()
    {
        // A page photographed at an angle: a symmetric trapezoid, narrow at
        // the top. Its quadrants (split at the projective centre — the
        // diagonals' intersection, y ≈ 69.2, not the halfway row 100) are
        // painted four colours; everything outside is black.
        PointF tl = new PointF(60, 20);
        PointF tr = new PointF(140, 20);
        PointF br = new PointF(190, 180);
        PointF bl = new PointF(10, 180);
        const double CentreY = 20 + (160 * 40.0 / 130.0);
        ImageFrame source = ImageFrame.Create(200, 200, ImageColorFormat.Rgb24);
        for (int y = 0; y < 200; y++)
        {
            for (int x = 0; x < 200; x++)
            {
                double px = x + 0.5;
                double py = y + 0.5;
                double halfWidth = 40 + ((py - 20) * 50.0 / 160.0);
                bool inside = py > 20 && py < 180 && Math.Abs(px - 100) < halfWidth;
                (byte b, byte g, byte r) = !inside ? ((byte)0, (byte)0, (byte)0)
                    : py < CentreY ? (px < 100 ? ((byte)0, (byte)0, (byte)255) : ((byte)255, (byte)0, (byte)0))
                    : (px < 100 ? ((byte)0, (byte)255, (byte)0) : ((byte)0, (byte)255, (byte)255));
                source.Pixels.SetPixelBgra(x, y, b, g, r, 255);
            }
        }

        ImageFrame result = PerspectiveCorrection.Correct(source, tl, tr, br, bl, 100, 100);

        // Quadrant interiors, away from the boundaries.
        result.Pixels.GetPixelBgra(20, 20).Should().Be(((byte)0, (byte)0, (byte)255, (byte)255), "top-left is red");
        result.Pixels.GetPixelBgra(80, 20).Should().Be(((byte)255, (byte)0, (byte)0, (byte)255), "top-right is blue");
        result.Pixels.GetPixelBgra(20, 80).Should().Be(((byte)0, (byte)255, (byte)0, (byte)255), "bottom-left is green");
        result.Pixels.GetPixelBgra(80, 80).Should().Be(((byte)0, (byte)255, (byte)255, (byte)255), "bottom-right is yellow");

        // The colour change sits at the output's middle row (perspective),
        // not at row 31 where linear spacing would put it.
        result.Pixels.GetPixelBgra(20, 46).Should().Be(((byte)0, (byte)0, (byte)255, (byte)255));
        result.Pixels.GetPixelBgra(20, 53).Should().Be(((byte)0, (byte)255, (byte)0, (byte)255));

        // No black from outside the page anywhere in the interior.
        for (int y = 2; y < 98; y++)
        {
            for (int x = 2; x < 98; x++)
            {
                (byte b, byte g, byte r, byte _) = result.Pixels.GetPixelBgra(x, y);
                (b + g + r).Should().BeGreaterThan(0, "pixel ({0},{1}) is inside the page", x, y);
            }
        }
    }

    [Fact]
    public void SuggestSize_UsesTheLongerEdges()
    {
        (int w, int h) = PerspectiveCorrection.SuggestSize(
            new PointF(60, 20), new PointF(140, 20), new PointF(190, 180), new PointF(10, 180));

        w.Should().Be(180);
        h.Should().Be(168);
    }

    [Fact]
    public void BadCorners_AreRejected()
    {
        ImageFrame source = Gradient(10, 10);
        PointF a = new PointF(0, 0);
        PointF b = new PointF(10, 0);
        PointF c = new PointF(10, 10);
        PointF d = new PointF(0, 10);

        Action crossed = () => PerspectiveCorrection.Correct(source, a, c, b, d);
        Action collinear = () => PerspectiveCorrection.Correct(source, a, new PointF(5, 0), b, c);
        Action nonFinite = () => PerspectiveCorrection.Correct(source, a, b, c, new PointF(double.NaN, 10));
        Action badSize = () => PerspectiveCorrection.Correct(source, a, b, c, d, 0, 10);
        Action nullSource = () => PerspectiveCorrection.Correct(null!, a, b, c, d);

        crossed.Should().Throw<ArgumentException>();
        collinear.Should().Throw<ArgumentException>();
        nonFinite.Should().Throw<ArgumentException>();
        badSize.Should().Throw<ArgumentOutOfRangeException>();
        nullSource.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TransparentNeighbours_DoNotDarkenTheEdge()
    {
        // Left half opaque white, right half fully transparent black.
        ImageFrame source = ImageFrame.Create(4, 1, ImageColorFormat.Rgba32);
        for (int x = 0; x < 4; x++)
        {
            byte v = x < 2 ? (byte)255 : (byte)0;
            source.Pixels.SetPixelBgra(x, 0, v, v, v, v);
        }

        // Shift by half a pixel so the boundary sample mixes the two.
        ImageFrame result = PerspectiveCorrection.Correct(
            source, new PointF(0.5, 0), new PointF(3.5, 0), new PointF(3.5, 1), new PointF(0.5, 1), 3, 1);

        (byte b, byte g, byte r, byte a) = result.Pixels.GetPixelBgra(1, 0);
        a.Should().BeInRange(120, 135);
        (b, g, r).Should().Be(((byte)255, (byte)255, (byte)255), "colour is interpolated premultiplied");
    }

    private static ImageFrame Gradient(int width, int height)
    {
        ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Rgb24);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                frame.Pixels.SetPixelBgra(x, y, (byte)(x * 2), (byte)(y * 2), (byte)((x + y) % 256), 255);
            }
        }

        return frame;
    }
}
