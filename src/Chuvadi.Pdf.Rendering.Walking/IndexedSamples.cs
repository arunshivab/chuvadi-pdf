// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.6.6.3 (Indexed colour spaces), §8.9.5.2
//        (Decode arrays), §8.9.3 (sample data: rows start on byte boundaries)
// PHASE: 3.32.0 (D-268) — palette images of 1, 2, 4 or 8 bits per sample,
//        shared by the raster and the text/SVG display-list builders.

using System;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.Walking;

/// <summary>
/// Reads the samples of an Indexed (palette) image: one palette index per
/// pixel, whatever the bits per sample, with the image's /Decode applied.
/// </summary>
internal static class IndexedSamples
{
    /// <summary>
    /// Unpacks <paramref name="samples"/> into one palette index per pixel,
    /// row by row. Returns null when <paramref name="bitsPerComponent"/> is
    /// not 1, 2, 4 or 8 or there are too few bytes.
    /// </summary>
    /// <param name="samples">The decoded image data.</param>
    /// <param name="width">The image width in pixels.</param>
    /// <param name="height">The image height in pixels.</param>
    /// <param name="bitsPerComponent">Bits per sample.</param>
    /// <param name="decode">The image's /Decode array, or null for the default [0 2^bpc-1].</param>
    /// <param name="objects">Resolves indirect /Decode entries.</param>
    /// <param name="hival">The palette's highest index; results are clamped to 0..hival.</param>
    internal static byte[]? ToIndices(
        byte[] samples, int width, int height, int bitsPerComponent, PdfPrimitive? decode, PdfObjectStore objects, int hival)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(objects);
        if (bitsPerComponent is not (1 or 2 or 4 or 8) || width <= 0 || height <= 0)
        {
            return null;
        }

        long stride = (((long)width * bitsPerComponent) + 7) / 8;
        if (samples.LongLength < stride * height)
        {
            return null;
        }

        int max = (1 << bitsPerComponent) - 1;
        byte[] map = SampleMap(decode, objects, max, hival);
        byte[] indices = new byte[(long)width * height];
        int mask = max;
        for (int y = 0; y < height; y++)
        {
            long row = y * stride;
            long o = (long)y * width;
            for (int x = 0; x < width; x++)
            {
                long bit = (long)x * bitsPerComponent;
                int shift = 8 - bitsPerComponent - (int)(bit & 7);
                int sample = (samples[row + (bit >> 3)] >> shift) & mask;
                indices[o + x] = map[sample];
            }
        }

        return indices;
    }

    // The palette index for each possible sample value: /Decode [Dmin Dmax]
    // maps sample s to Dmin + s * (Dmax - Dmin) / (2^bpc - 1), rounded and
    // clamped to the palette (§8.9.5.2). The default is [0 2^bpc-1], so a
    // sample is its own index.
    private static byte[] SampleMap(PdfPrimitive? decode, PdfObjectStore objects, int max, int hival)
    {
        double dMin = 0;
        double dMax = max;
        if (decode is not null && objects.Resolve(decode) is PdfArray array && array.Count >= 2
            && Number(objects.Resolve(array[0])) is double lo && Number(objects.Resolve(array[1])) is double hi)
        {
            dMin = lo;
            dMax = hi;
        }

        int top = Math.Clamp(hival, 0, 255);
        byte[] map = new byte[max + 1];
        for (int s = 0; s <= max; s++)
        {
            double value = dMin + (s * (dMax - dMin) / max);
            map[s] = (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, top);
        }

        return map;
    }

    private static double? Number(PdfPrimitive value) => value switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => null,
    };
}
