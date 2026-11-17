// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9.6.3 (explicit masking), §8.9.6.4 (colour key masking)
// PHASE: 3.35.0 (backlog 60) — an image's /Mask is turned into the image op's
//        alpha, so the SVG and WPF outputs (which apply SoftMaskAlpha) mask it
//        as the raster renderer does.

using Chuvadi.Pdf.Images;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Turns an image's /Mask into per-pixel alpha (0 or 255).</summary>
internal static class ExplicitMask
{
    // A 1-bit mask of mw x mh (rows padded to a byte) as alpha at the mask's
    // own size: a sample of 1 hides the image (0 when zeroMasks).
    internal static byte[]? StencilAlpha(byte[] bits, int mw, int mh, bool zeroMasks)
    {
        int stride = (mw + 7) / 8;
        if ((long)bits.Length < (long)stride * mh)
        {
            return null;
        }

        byte[] alpha = new byte[mw * mh];
        for (int y = 0; y < mh; y++)
        {
            for (int x = 0; x < mw; x++)
            {
                int bit = (bits[(y * stride) + (x >> 3)] >> (7 - (x & 7))) & 1;
                alpha[(y * mw) + x] = (bit == 1) != zeroMasks ? (byte)0 : (byte)255;
            }
        }

        return alpha;
    }

    // Colour-key alpha at the image's size: a pixel whose every component lies
    // in its [lo, hi] range is hidden. Samples are one byte per component.
    internal static byte[]? ColorKeyAlpha(byte[] samples, int width, int height, int components, int[] lo, int[] hi)
    {
        long pixels = (long)width * height;
        if (samples.Length < pixels * components)
        {
            return null;
        }

        byte[] alpha = new byte[pixels];
        for (long i = 0; i < pixels; i++)
        {
            bool inside = true;
            for (int c = 0; c < components && inside; c++)
            {
                int v = samples[(i * components) + c];
                inside = v >= lo[c] && v <= hi[c];
            }

            alpha[i] = inside ? (byte)0 : (byte)255;
        }

        return alpha;
    }

    // A gray or RGB JPEG of the given size as one byte per component, with the
    // component count; null for any other JPEG (CMYK, or a size mismatch).
    internal static byte[]? JpegSamples(byte[] jpeg, int width, int height, out int components)
    {
        components = 0;
        ImageFrame frame;
        try
        {
            frame = JpegDecoder.Decode(jpeg);
        }
        catch (ImageException)
        {
            return null;
        }

        if (frame.Width != width || frame.Height != height)
        {
            return null;
        }

        int n = frame.OriginalFormat switch
        {
            ImageColorFormat.Gray8 => 1,
            ImageColorFormat.Rgb24 => 3,
            _ => 0,
        };
        if (n == 0)
        {
            return null;
        }

        byte[] samples = new byte[width * height * n];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (byte b, byte g, byte r, byte _) = frame.Pixels.GetPixelBgra(x, y);
                int o = ((y * width) + x) * n;
                samples[o] = r;
                if (n == 3)
                {
                    samples[o + 1] = g;
                    samples[o + 2] = b;
                }
            }
        }

        components = n;
        return samples;
    }
}
