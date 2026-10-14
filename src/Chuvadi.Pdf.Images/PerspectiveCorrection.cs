// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Projective (homography) image warp — four-point perspective
//        correction with bilinear sampling.
// PHASE: 3.20 — raster 4-point perspective deskew (Reader "Bench").

using System;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Four-point perspective correction ("deskew"): maps a quadrilateral marked
/// on a photo of a page — its four corners — onto an upright rectangle, the
/// way a scanning app straightens a page shot at an angle.
/// </summary>
/// <remarks>
/// <para>
/// Corner coordinates are in source-image pixels with the origin at the
/// top-left and Y pointing down; a pixel's centre is at (x + 0.5, y + 0.5),
/// so the image's full extent runs from (0, 0) to (Width, Height). Corners
/// may lie outside the image; samples falling outside are clamped to the
/// nearest edge pixel.
/// </para>
/// <para>
/// Each output pixel is mapped back through the homography and sampled
/// bilinearly on alpha-premultiplied values, so transparent regions do not
/// bleed dark fringes. The result is a new BGRA frame; the source is not
/// modified.
/// </para>
/// </remarks>
public static class PerspectiveCorrection
{
    /// <summary>The largest output width or height accepted.</summary>
    public const int MaxDimension = 32768;

    /// <summary>
    /// Straightens the quadrilateral to a rectangle sized from its edges: the
    /// width is the longer of the top and bottom edges, the height the longer
    /// of the left and right edges (each rounded, at least one pixel).
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="topLeft">The quadrilateral's top-left corner.</param>
    /// <param name="topRight">The top-right corner.</param>
    /// <param name="bottomRight">The bottom-right corner.</param>
    /// <param name="bottomLeft">The bottom-left corner.</param>
    /// <returns>The corrected image.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the corners do not form a convex quadrilateral.</exception>
    public static ImageFrame Correct(
        ImageFrame source, PointF topLeft, PointF topRight, PointF bottomRight, PointF bottomLeft)
    {
        ArgumentNullException.ThrowIfNull(source);
        (int width, int height) = SuggestSize(topLeft, topRight, bottomRight, bottomLeft);
        return Correct(source, topLeft, topRight, bottomRight, bottomLeft, width, height);
    }

    /// <summary>
    /// Straightens the quadrilateral to a rectangle of the given size.
    /// </summary>
    /// <param name="source">The source image.</param>
    /// <param name="topLeft">The quadrilateral's top-left corner.</param>
    /// <param name="topRight">The top-right corner.</param>
    /// <param name="bottomRight">The bottom-right corner.</param>
    /// <param name="bottomLeft">The bottom-left corner.</param>
    /// <param name="outputWidth">Output width in pixels (1 to <see cref="MaxDimension"/>).</param>
    /// <param name="outputHeight">Output height in pixels (1 to <see cref="MaxDimension"/>).</param>
    /// <returns>The corrected image.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an output dimension is out of range.</exception>
    /// <exception cref="ArgumentException">Thrown when the corners do not form a convex quadrilateral.</exception>
    public static ImageFrame Correct(
        ImageFrame source, PointF topLeft, PointF topRight, PointF bottomRight, PointF bottomLeft,
        int outputWidth, int outputHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (outputWidth < 1 || outputWidth > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(outputWidth), outputWidth, "Output width is out of range.");
        }

        if (outputHeight < 1 || outputHeight > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(outputHeight), outputHeight, "Output height is out of range.");
        }

        PointF[] quad = { topLeft, topRight, bottomRight, bottomLeft };
        ValidateQuad(quad);

        // Homography from the output rectangle's corners to the quad.
        PointF[] rect =
        {
            new PointF(0, 0),
            new PointF(outputWidth, 0),
            new PointF(outputWidth, outputHeight),
            new PointF(0, outputHeight),
        };
        double[] h = ComputeHomography(rect, quad);

        PixelBuffer src = source.Pixels;
        PixelBuffer dst = new PixelBuffer(outputWidth, outputHeight);
        ReadOnlySpan<byte> pixels = src.Pixels;
        int stride = src.Stride;

        for (int v = 0; v < outputHeight; v++)
        {
            double y = v + 0.5;
            for (int u = 0; u < outputWidth; u++)
            {
                double x = u + 0.5;
                double w = (h[6] * x) + (h[7] * y) + 1.0;
                double sx = ((h[0] * x) + (h[1] * y) + h[2]) / w;
                double sy = ((h[3] * x) + (h[4] * y) + h[5]) / w;
                (byte b, byte g, byte r, byte a) = Sample(pixels, stride, src.Width, src.Height, sx - 0.5, sy - 0.5);
                dst.SetPixelBgra(u, v, b, g, r, a);
            }
        }

        return new ImageFrame(dst, source.OriginalFormat);
    }

    /// <summary>
    /// Returns the output size <see cref="Correct(ImageFrame, PointF, PointF, PointF, PointF)"/>
    /// uses for these corners: the longer horizontal edge by the longer
    /// vertical edge, rounded, each at least 1 and at most <see cref="MaxDimension"/>.
    /// </summary>
    /// <param name="topLeft">The top-left corner.</param>
    /// <param name="topRight">The top-right corner.</param>
    /// <param name="bottomRight">The bottom-right corner.</param>
    /// <param name="bottomLeft">The bottom-left corner.</param>
    /// <returns>The suggested width and height in pixels.</returns>
    public static (int Width, int Height) SuggestSize(
        PointF topLeft, PointF topRight, PointF bottomRight, PointF bottomLeft)
    {
        double width = Math.Max(Distance(topLeft, topRight), Distance(bottomLeft, bottomRight));
        double height = Math.Max(Distance(topLeft, bottomLeft), Distance(topRight, bottomRight));
        return (ClampDimension(width), ClampDimension(height));
    }

    private static int ClampDimension(double value)
    {
        if (double.IsNaN(value))
        {
            return 1;
        }

        return (int)Math.Clamp(Math.Round(value), 1, MaxDimension);
    }

    private static double Distance(PointF a, PointF b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    // The corners must be finite and form a strictly convex quadrilateral
    // (every turn the same way, non-zero), which rules out degenerate,
    // self-intersecting or mis-ordered corner sets.
    private static void ValidateQuad(PointF[] quad)
    {
        foreach (PointF p in quad)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y))
            {
                throw new ArgumentException("Corner coordinates must be finite.", nameof(quad));
            }
        }

        int sign = 0;
        for (int i = 0; i < 4; i++)
        {
            PointF a = quad[i];
            PointF b = quad[(i + 1) % 4];
            PointF c = quad[(i + 2) % 4];
            double cross = ((b.X - a.X) * (c.Y - b.Y)) - ((b.Y - a.Y) * (c.X - b.X));
            if (Math.Abs(cross) < 1e-9)
            {
                throw new ArgumentException("The corners are degenerate (three are collinear).", nameof(quad));
            }

            int s = Math.Sign(cross);
            if (sign == 0)
            {
                sign = s;
            }
            else if (s != sign)
            {
                throw new ArgumentException(
                    "The corners must form a convex quadrilateral, given in order around it.", nameof(quad));
            }
        }
    }

    // Solves for h (8 values, h[8] = 1) with to = H · from for the four
    // correspondences, by Gaussian elimination with partial pivoting.
    internal static double[] ComputeHomography(PointF[] from, PointF[] to)
    {
        double[,] m = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            double x = from[i].X;
            double y = from[i].Y;
            double u = to[i].X;
            double v = to[i].Y;
            int r = 2 * i;
            m[r, 0] = x;
            m[r, 1] = y;
            m[r, 2] = 1;
            m[r, 6] = -x * u;
            m[r, 7] = -y * u;
            m[r, 8] = u;
            m[r + 1, 3] = x;
            m[r + 1, 4] = y;
            m[r + 1, 5] = 1;
            m[r + 1, 6] = -x * v;
            m[r + 1, 7] = -y * v;
            m[r + 1, 8] = v;
        }

        for (int col = 0; col < 8; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < 8; row++)
            {
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                {
                    pivot = row;
                }
            }

            if (Math.Abs(m[pivot, col]) < 1e-12)
            {
                throw new ArgumentException("The corners do not define a perspective mapping.", nameof(to));
            }

            if (pivot != col)
            {
                for (int k = 0; k < 9; k++)
                {
                    (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                }
            }

            for (int row = 0; row < 8; row++)
            {
                if (row == col)
                {
                    continue;
                }

                double factor = m[row, col] / m[col, col];
                if (factor == 0)
                {
                    continue;
                }

                for (int k = col; k < 9; k++)
                {
                    m[row, k] -= factor * m[col, k];
                }
            }
        }

        double[] h = new double[8];
        for (int i = 0; i < 8; i++)
        {
            h[i] = m[i, 8] / m[i, i];
        }

        return h;
    }

    // Bilinear sample at continuous pixel coordinates (pixel centres on
    // integers), clamped to the image, interpolated premultiplied.
    private static (byte B, byte G, byte R, byte A) Sample(
        ReadOnlySpan<byte> pixels, int stride, int width, int height, double x, double y)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        int x1 = Math.Min(x0 + 1, width - 1);
        int y1 = Math.Min(y0 + 1, height - 1);
        double fx = x - x0;
        double fy = y - y0;

        double w00 = (1 - fx) * (1 - fy);
        double w10 = fx * (1 - fy);
        double w01 = (1 - fx) * fy;
        double w11 = fx * fy;

        double b = 0, g = 0, r = 0, a = 0;
        Accumulate(pixels, (y0 * stride) + (x0 * 4), w00, ref b, ref g, ref r, ref a);
        Accumulate(pixels, (y0 * stride) + (x1 * 4), w10, ref b, ref g, ref r, ref a);
        Accumulate(pixels, (y1 * stride) + (x0 * 4), w01, ref b, ref g, ref r, ref a);
        Accumulate(pixels, (y1 * stride) + (x1 * 4), w11, ref b, ref g, ref r, ref a);

        if (a <= 0)
        {
            return (0, 0, 0, 0);
        }

        return (ToByte(b / a * 255), ToByte(g / a * 255), ToByte(r / a * 255), ToByte(a));
    }

    private static void Accumulate(
        ReadOnlySpan<byte> pixels, int offset, double weight,
        ref double b, ref double g, ref double r, ref double a)
    {
        if (weight == 0)
        {
            return;
        }

        double alpha = pixels[offset + 3];
        double wa = weight * alpha / 255.0;
        b += pixels[offset] * wa;
        g += pixels[offset + 1] * wa;
        r += pixels[offset + 2] * wa;
        a += weight * alpha;
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
