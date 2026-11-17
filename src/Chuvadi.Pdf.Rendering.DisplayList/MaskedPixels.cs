// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.6.5.3 (soft-mask images)
// PHASE: 3.35.0 (backlog 62) — shared by the SVG and WPF outputs (moved from
//        the SVG image encoder, unchanged), so WPF applies soft masks too.

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Combines an image's colour samples with its soft-mask alpha.</summary>
internal static class MaskedPixels
{
    // Expands a 1- or 3-channel base image to RGBA using the soft-mask samples as
    // alpha. The mask is nearest-neighbour resampled when its size differs from
    // the base. Returns null if the base buffer is too small to interpret.
    internal static byte[]? Rgba(
        byte[] basePixels,
        int channels,
        int width,
        int height,
        byte[] alpha,
        int alphaWidth,
        int alphaHeight)
    {
        long pixelCount = (long)width * height;
        if (channels != 1 && channels != 3) { return null; }
        if (basePixels.Length < pixelCount * channels) { return null; }

        byte[] rgba = new byte[pixelCount * 4];
        bool sameSize = alphaWidth == width && alphaHeight == height && alphaWidth > 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                byte r;
                byte g;
                byte b;

                if (channels == 1)
                {
                    byte v = basePixels[index];
                    r = v;
                    g = v;
                    b = v;
                }
                else
                {
                    r = basePixels[(index * 3) + 0];
                    g = basePixels[(index * 3) + 1];
                    b = basePixels[(index * 3) + 2];
                }

                byte a;
                if (sameSize)
                {
                    a = index < alpha.Length ? alpha[index] : (byte)255;
                }
                else if (alphaWidth > 0 && alphaHeight > 0)
                {
                    int ax = (int)((long)x * alphaWidth / width);
                    int ay = (int)((long)y * alphaHeight / height);
                    int ai = (ay * alphaWidth) + ax;
                    a = ai >= 0 && ai < alpha.Length ? alpha[ai] : (byte)255;
                }
                else
                {
                    a = 255;
                }

                int outIndex = index * 4;
                rgba[outIndex + 0] = r;
                rgba[outIndex + 1] = g;
                rgba[outIndex + 2] = b;
                rgba[outIndex + 3] = a;
            }
        }

        return rgba;
    }
}
