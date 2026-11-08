// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.6.6.3 (Indexed colour spaces), §8.9.5 (image
//        dictionaries), §7.4.4 (FlateDecode)
// PHASE: 3.32.0 (D-268) — the two colour image levels: every page as one
//        4-bit indexed-colour image. D-277: dark greys snap to black in
//        reduced colour.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Writes an image-only PDF in which every page is one 4-bit indexed-colour
/// image (up to 16 colours), Flate-compressed. Two ways of choosing the
/// colours (measured on A1 drawings, 8 Nov):
/// <list type="bullet">
/// <item><see cref="ReducedColorImage"/>: white, black and the page's 14 most
/// used other colours; dark grey pixels become black and every other pixel
/// takes the nearest colour. Closest to the original look.</item>
/// <item><see cref="BlackAndWhiteWithColorImage"/>: clearly coloured pixels
/// take the nearest of the page's 14 most used colours; every other pixel
/// goes through the black-and-white rules of <see cref="BilevelPageWriter"/>,
/// so grey text stays sharp. Smaller.</item>
/// </list>
/// </summary>
internal static class PalettePageWriter
{
    // Most entries an Indexed colour space with 4 bits per sample can use.
    private const int MaxColors = 16;

    // Two palette entries closer than this (sum of the R, G and B
    // differences) are treated as the same shade.
    private const int MinDistance = 60;

    // A pixel whose largest and smallest channel differ by at least this is
    // clearly coloured (not white, grey or black).
    internal const int ChromaThreshold = 48;

    // Reduced colour (D-277): a pixel that is not clearly coloured and is
    // darker than this luminance (0-255) is black. The soft edges of thin
    // black lines and text are dark grey; without this they took the page's
    // grey entries and text came out grey. Measured on A1 drawings (9 Nov):
    // text clearly darker, files 7% smaller, and the palette slots the greys
    // took go to real colours. Mid greys darker than this become black.
    internal const int SnapToBlackBelow = 160;

    /// <summary>
    /// The reduced-colour image of one page: white, black, then the most used
    /// other shades (5 bits per channel), each at least <see cref="MinDistance"/>
    /// from those already chosen. A pixel that is not clearly coloured and is
    /// darker than <see cref="SnapToBlackBelow"/> is black and is not counted
    /// when the shades are chosen; every other pixel maps to the nearest entry.
    /// </summary>
    internal static PdfStream ReducedColorImage(PixelBuffer buffer)
    {
        ReadOnlySpan<byte> px = buffer.Pixels;
        int[] counts = new int[1 << 15];
        for (int o = 0; o < px.Length; o += 4)
        {
            if (!SnapsToBlack(px[o + 2], px[o + 1], px[o]))
            {
                counts[Key5(px[o + 2], px[o + 1], px[o])]++;
            }
        }

        List<(int R, int G, int B)> palette = new List<(int R, int G, int B)> { (255, 255, 255), (0, 0, 0) };
        AddMostUsed(counts, palette, firstCompared: 0);

        byte[] indices = new byte[buffer.Width * buffer.Height];
        Dictionary<int, byte> nearest = new Dictionary<int, byte>();
        for (int i = 0, o = 0; i < indices.Length; i++, o += 4)
        {
            byte r = px[o + 2];
            byte g = px[o + 1];
            byte b = px[o];
            indices[i] = SnapsToBlack(r, g, b) ? (byte)1 : Nearest(palette, 0, r, g, b, nearest);
        }

        return IndexedImage(buffer, palette, indices);
    }

    /// <summary>
    /// The black-and-white-with-colour image of one page. A page with no
    /// clearly coloured pixels is written as the plain 1-bit image.
    /// </summary>
    internal static PdfStream BlackAndWhiteWithColorImage(PixelBuffer buffer, bool dotPatternFills)
    {
        ReadOnlySpan<byte> px = buffer.Pixels;
        byte[] bits = BilevelPageWriter.ToBits(buffer, dotPatternFills);

        int[] counts = new int[1 << 15];
        bool anyColor = false;
        for (int o = 0; o < px.Length; o += 4)
        {
            if (IsChromatic(px[o + 2], px[o + 1], px[o]))
            {
                counts[Key5(px[o + 2], px[o + 1], px[o])]++;
                anyColor = true;
            }
        }

        if (!anyColor)
        {
            return BilevelPageWriter.BilevelImage(buffer, bits);
        }

        // Entries 0 and 1 are white and black, used for everything that is
        // not clearly coloured; only the colours are compared with each other.
        List<(int R, int G, int B)> palette = new List<(int R, int G, int B)> { (255, 255, 255), (0, 0, 0) };
        AddMostUsed(counts, palette, firstCompared: 2);

        int w = buffer.Width;
        int stride = (w + 7) / 8;
        byte[] indices = new byte[w * buffer.Height];
        Dictionary<int, byte> nearest = new Dictionary<int, byte>();
        for (int i = 0, o = 0; i < indices.Length; i++, o += 4)
        {
            byte r = px[o + 2];
            byte g = px[o + 1];
            byte b = px[o];
            if (IsChromatic(r, g, b))
            {
                indices[i] = Nearest(palette, 2, r, g, b, nearest);
            }
            else
            {
                int x = i % w;
                int y = i / w;
                bool white = (bits[(y * stride) + (x >> 3)] & (0x80 >> (x & 7))) != 0;
                indices[i] = white ? (byte)0 : (byte)1;
            }
        }

        return IndexedImage(buffer, palette, indices);
    }

    // Not clearly coloured, and darker than SnapToBlackBelow (Rec. 601 luma,
    // as the black-and-white rules use).
    private static bool SnapsToBlack(int r, int g, int b) =>
        !IsChromatic(r, g, b) && (((r * 299) + (g * 587) + (b * 114)) / 1000) < SnapToBlackBelow;

    private static bool IsChromatic(int r, int g, int b) =>
        Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) >= ChromaThreshold;

    private static int Key5(int r, int g, int b) => ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);

    // Adds the most used shades (ties: lower key first, so the result never
    // depends on dictionary order) until the palette is full.
    private static void AddMostUsed(int[] counts, List<(int R, int G, int B)> palette, int firstCompared)
    {
        List<int> keys = new List<int>();
        for (int k = 0; k < counts.Length; k++)
        {
            if (counts[k] > 0)
            {
                keys.Add(k);
            }
        }

        keys.Sort((a, b) => counts[a] != counts[b] ? counts[b].CompareTo(counts[a]) : a.CompareTo(b));
        foreach (int key in keys)
        {
            if (palette.Count >= MaxColors)
            {
                break;
            }

            int r = ((key >> 10) & 31) * 255 / 31;
            int g = ((key >> 5) & 31) * 255 / 31;
            int b = (key & 31) * 255 / 31;
            bool distinct = true;
            for (int p = firstCompared; p < palette.Count; p++)
            {
                if (Math.Abs(palette[p].R - r) + Math.Abs(palette[p].G - g) + Math.Abs(palette[p].B - b) <= MinDistance)
                {
                    distinct = false;
                    break;
                }
            }

            if (distinct)
            {
                palette.Add((r, g, b));
            }
        }
    }

    // The nearest entry from `first` on (squared RGB distance; ties: the
    // lower index), cached per exact colour.
    private static byte Nearest(List<(int R, int G, int B)> palette, int first, int r, int g, int b, Dictionary<int, byte> cache)
    {
        int key = (r << 16) | (g << 8) | b;
        if (cache.TryGetValue(key, out byte found))
        {
            return found;
        }

        int best = int.MaxValue;
        byte index = (byte)first;
        for (int p = first; p < palette.Count; p++)
        {
            int dr = palette[p].R - r;
            int dg = palette[p].G - g;
            int db = palette[p].B - b;
            int d = (dr * dr) + (dg * dg) + (db * db);
            if (d < best)
            {
                best = d;
                index = (byte)p;
            }
        }

        cache[key] = index;
        return index;
    }

    // Packs the indices two per byte (high nibble first, rows padded to a
    // whole byte) into an /Indexed /DeviceRGB image.
    private static PdfStream IndexedImage(PixelBuffer buffer, List<(int R, int G, int B)> palette, byte[] indices)
    {
        int w = buffer.Width;
        int h = buffer.Height;
        int stride = (w + 1) / 2;
        byte[] packed = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int index = indices[(y * w) + x];
                packed[(y * stride) + (x >> 1)] |= (byte)((x & 1) == 0 ? index << 4 : index);
            }
        }

        byte[] lookup = new byte[palette.Count * 3];
        for (int p = 0; p < palette.Count; p++)
        {
            lookup[p * 3] = (byte)palette[p].R;
            lookup[(p * 3) + 1] = (byte)palette[p].G;
            lookup[(p * 3) + 2] = (byte)palette[p].B;
        }

        byte[] data = BilevelPageWriter.Deflate(packed);
        PdfDictionary image = new PdfDictionary();
        image.Set(PdfName.Type, PdfName.XObject);
        image.Set(PdfName.Subtype, PdfName.Intern("Image"));
        image.Set(PdfName.Intern("Width"), w);
        image.Set(PdfName.Intern("Height"), h);
        image.Set(PdfName.Intern("ColorSpace"), new PdfArray(new PdfPrimitive[]
        {
            PdfName.Intern("Indexed"),
            PdfName.Intern("DeviceRGB"),
            new PdfInteger(palette.Count - 1),
            new PdfString(lookup, preferHexForm: true),
        }));
        image.Set(PdfName.Intern("BitsPerComponent"), 4);
        image.Set(PdfName.Intern("Filter"), PdfName.Intern("FlateDecode"));
        image.Set(PdfName.Length, data.Length);
        return new PdfStream(image, data);
    }
}
