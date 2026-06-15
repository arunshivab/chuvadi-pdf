// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 0 — compression measurement foundations

using System;
using System.IO;
using Chuvadi.Pdf.Images;

namespace Chuvadi.Benchmarks.Compression;

/// <summary>
/// Structural-similarity quality measurement for the lossy image path. A global
/// (single-window) SSIM on luminance is enough for a stable per-release quality
/// signal: it is monotonic with JPEG quality and fully deterministic. A windowed
/// or multi-scale SSIM is a later perceptual refinement.
/// </summary>
public static class Ssim
{
    private const double C1 = 0.01 * 255.0 * (0.01 * 255.0);
    private const double C2 = 0.03 * 255.0 * (0.03 * 255.0);

    /// <summary>
    /// Encodes <paramref name="rgb"/> as JPEG at <paramref name="quality"/> and
    /// decodes it back — exactly what the lossy compression path does to an
    /// image — then returns the global SSIM between the original and the
    /// reconstruction (1.0 = identical).
    /// </summary>
    /// <param name="rgb">Source 8-bit RGB samples, row-major, length width*height*3.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="quality">JPEG quality (1–100).</param>
    public static double RoundTripQuality(byte[] rgb, int width, int height, int quality)
    {
        ArgumentNullException.ThrowIfNull(rgb);

        ImageFrame original = BuildFrame(rgb, width, height);

        using MemoryStream encoded = new MemoryStream();
        JpegEncoder.Encode(original, encoded, quality);
        ImageFrame reconstructed = JpegDecoder.Decode(encoded.ToArray());

        return Compute(original, reconstructed);
    }

    private static ImageFrame BuildFrame(byte[] rgb, int width, int height)
    {
        ImageFrame frame = ImageFrame.Create(width, height, ImageColorFormat.Rgb24);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int si = ((y * width) + x) * 3;
                frame.Pixels.SetPixelBgra(x, y, rgb[si + 2], rgb[si + 1], rgb[si], 255);
            }
        }

        return frame;
    }

    private static double Compute(ImageFrame a, ImageFrame b)
    {
        int width = Math.Min(a.Width, b.Width);
        int height = Math.Min(a.Height, b.Height);
        int count = width * height;
        if (count == 0)
        {
            return 1.0;
        }

        double sumA = 0.0;
        double sumB = 0.0;
        double sumAA = 0.0;
        double sumBB = 0.0;
        double sumAB = 0.0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double la = Luminance(a.Pixels.GetPixelBgra(x, y));
                double lb = Luminance(b.Pixels.GetPixelBgra(x, y));
                sumA += la;
                sumB += lb;
                sumAA += la * la;
                sumBB += lb * lb;
                sumAB += la * lb;
            }
        }

        double meanA = sumA / count;
        double meanB = sumB / count;
        double varA = (sumAA / count) - (meanA * meanA);
        double varB = (sumBB / count) - (meanB * meanB);
        double cov = (sumAB / count) - (meanA * meanB);

        double numerator = ((2.0 * meanA * meanB) + C1) * ((2.0 * cov) + C2);
        double denominator = ((meanA * meanA) + (meanB * meanB) + C1) * (varA + varB + C2);
        return numerator / denominator;
    }

    private static double Luminance((byte B, byte G, byte R, byte A) pixel)
    {
        return (0.299 * pixel.R) + (0.587 * pixel.G) + (0.114 * pixel.B);
    }
}
