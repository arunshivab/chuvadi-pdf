// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Windows Bitmap (BMP) file format — BITMAPFILEHEADER,
//        BITMAPCOREHEADER (12), BITMAPINFOHEADER (40) and the V4/V5
//        extensions (108/124); BI_RGB, BI_RLE8, BI_RLE4, BI_BITFIELDS.
// PHASE: Phase 2.7 — Chuvadi.Pdf.Images (Image → PDF)
// Decodes a BMP byte stream into an ImageFrame.

using System;
using System.IO;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Decodes a Windows BMP image into an <see cref="ImageFrame"/>.
/// </summary>
/// <remarks>
/// Supports:
/// <list type="bullet">
///   <item>Headers: BITMAPCOREHEADER (12 bytes), BITMAPINFOHEADER (40 bytes),
///   and the V2–V5 extensions (52, 56, 108, 124 bytes).</item>
///   <item>Bit depths: 1, 4, 8 (palette), 16 (5-5-5 default or BI_BITFIELDS
///   masks), 24 (BGR), 32 (BGRX, or masked channels including alpha under
///   BI_BITFIELDS).</item>
///   <item>Compression: BI_RGB (uncompressed), BI_RLE8, BI_RLE4,
///   BI_BITFIELDS.</item>
///   <item>Row order: bottom-up (positive height) and top-down (negative
///   height).</item>
/// </list>
/// The decoder is the inverse companion of <see cref="BmpEncoder"/>; together
/// they round-trip 24-bit BI_RGB bitmaps losslessly.
/// </remarks>
public static class BmpDecoder
{
    private const int CompressionRgb = 0;
    private const int CompressionRle8 = 1;
    private const int CompressionRle4 = 2;
    private const int CompressionBitFields = 3;

    /// <summary>
    /// Decodes a BMP from a byte array.
    /// </summary>
    /// <param name="data">The raw BMP bytes.</param>
    /// <returns>A decoded <see cref="ImageFrame"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ImageException">Thrown on invalid or unsupported BMP data.</exception>
    public static ImageFrame Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return DecodeCore(data);
    }

    /// <summary>
    /// Decodes a BMP from a stream.
    /// </summary>
    /// <param name="input">The stream positioned at the start of the BMP data.</param>
    /// <returns>A decoded <see cref="ImageFrame"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    /// <exception cref="ImageException">Thrown on invalid or unsupported BMP data.</exception>
    public static ImageFrame Decode(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using (MemoryStream ms = new MemoryStream())
        {
            input.CopyTo(ms);
            return DecodeCore(ms.ToArray());
        }
    }

    private static ImageFrame DecodeCore(byte[] d)
    {
        if (d.Length < 26 || d[0] != (byte)'B' || d[1] != (byte)'M')
        {
            throw new ImageException("Not a valid BMP: signature mismatch.");
        }

        uint dataOffset = ReadU32(d, 10);
        uint headerSize = ReadU32(d, 14);

        int width;
        int height;
        int bpp;
        int compression = CompressionRgb;
        int paletteEntrySize;
        int paletteOffset;
        int colorsUsed = 0;
        uint maskR = 0;
        uint maskG = 0;
        uint maskB = 0;
        uint maskA = 0;

        if (headerSize == 12)
        {
            // BITMAPCOREHEADER: u16 width, u16 height, u16 planes, u16 bpp.
            width = ReadU16(d, 18);
            height = ReadU16(d, 20);
            bpp = ReadU16(d, 24);
            paletteEntrySize = 3;
            paletteOffset = 14 + 12;
        }
        else if (headerSize >= 40)
        {
            width = ReadI32(d, 18);
            height = ReadI32(d, 22);
            bpp = ReadU16(d, 28);
            compression = (int)ReadU32(d, 30);
            colorsUsed = (int)ReadU32(d, 46);
            paletteEntrySize = 4;
            paletteOffset = 14 + (int)headerSize;

            if (compression == CompressionBitFields)
            {
                if (headerSize >= 108)
                {
                    // V4/V5: masks live inside the header.
                    maskR = ReadU32(d, 54);
                    maskG = ReadU32(d, 58);
                    maskB = ReadU32(d, 62);
                    maskA = ReadU32(d, 66);
                }
                else
                {
                    // INFOHEADER: three masks immediately follow the header.
                    if (d.Length < paletteOffset + 12)
                    {
                        throw new ImageException("BMP truncated: BI_BITFIELDS masks missing.");
                    }
                    maskR = ReadU32(d, paletteOffset);
                    maskG = ReadU32(d, paletteOffset + 4);
                    maskB = ReadU32(d, paletteOffset + 8);
                    paletteOffset += 12;
                }
            }
        }
        else
        {
            throw new ImageException($"Unsupported BMP header size: {headerSize}.");
        }

        bool topDown = height < 0;
        if (topDown)
        {
            height = -height;
        }

        if (width <= 0 || height <= 0)
        {
            throw new ImageException($"Invalid BMP dimensions: {width}x{height}.");
        }

        if (bpp != 1 && bpp != 4 && bpp != 8 && bpp != 16 && bpp != 24 && bpp != 32)
        {
            throw new ImageException($"Unsupported BMP bit depth: {bpp}.");
        }

        if (compression != CompressionRgb &&
            compression != CompressionRle8 &&
            compression != CompressionRle4 &&
            compression != CompressionBitFields)
        {
            throw new ImageException($"Unsupported BMP compression: {compression}.");
        }

        byte[][]? palette = null;
        if (bpp <= 8)
        {
            int entries = colorsUsed > 0 ? colorsUsed : 1 << bpp;
            palette = ReadPalette(d, paletteOffset, entries, paletteEntrySize);
        }

        bool hasAlpha = compression == CompressionBitFields && maskA != 0;
        ImageColorFormat format = hasAlpha ? ImageColorFormat.Rgba32 : ImageColorFormat.Rgb24;
        ImageFrame frame = ImageFrame.Create(width, height, format);

        if (compression == CompressionRle8 || compression == CompressionRle4)
        {
            DecodeRle(d, (int)dataOffset, frame, width, height, topDown, palette!,
                isRle4: compression == CompressionRle4);
            return frame;
        }

        DecodeUncompressed(d, (int)dataOffset, frame, width, height, topDown, bpp,
            compression, palette, maskR, maskG, maskB, maskA);
        return frame;
    }

    private static byte[][] ReadPalette(byte[] d, int offset, int entries, int entrySize)
    {
        if (d.Length < offset + (entries * entrySize))
        {
            throw new ImageException("BMP truncated: palette extends past end of data.");
        }

        byte[][] palette = new byte[entries][];
        for (int i = 0; i < entries; i++)
        {
            int p = offset + (i * entrySize);
            // Palette entries are stored B, G, R (+ reserved for 4-byte entries).
            palette[i] = new byte[] { d[p], d[p + 1], d[p + 2] };
        }
        return palette;
    }

    private static void DecodeUncompressed(
        byte[] d, int dataOffset, ImageFrame frame,
        int width, int height, bool topDown, int bpp, int compression,
        byte[][]? palette, uint maskR, uint maskG, uint maskB, uint maskA)
    {
        int rowBytes = ((width * bpp) + 31) / 32 * 4;
        long required = (long)dataOffset + ((long)rowBytes * height);
        if (d.Length < required)
        {
            throw new ImageException("BMP truncated: pixel data extends past end of data.");
        }

        // Default 16/32-bit channel layouts when BI_RGB (no masks supplied).
        if (compression == CompressionRgb && bpp == 16)
        {
            maskR = 0x7C00;
            maskG = 0x03E0;
            maskB = 0x001F;
            maskA = 0;
        }
        if (compression == CompressionRgb && bpp == 32)
        {
            maskR = 0x00FF0000;
            maskG = 0x0000FF00;
            maskB = 0x000000FF;
            maskA = 0;
        }

        for (int row = 0; row < height; row++)
        {
            int y = topDown ? row : height - 1 - row;
            int rowStart = dataOffset + (row * rowBytes);

            for (int x = 0; x < width; x++)
            {
                byte rOut;
                byte gOut;
                byte bOut;
                byte aOut = 255;

                switch (bpp)
                {
                    case 1:
                    case 4:
                    case 8:
                        {
                            int index = ReadPaletteIndex(d, rowStart, x, bpp);
                            byte[] entry = PaletteEntry(palette!, index);
                            bOut = entry[0];
                            gOut = entry[1];
                            rOut = entry[2];
                            break;
                        }
                    case 16:
                        {
                            uint v = (uint)(d[rowStart + (x * 2)] | (d[rowStart + (x * 2) + 1] << 8));
                            rOut = ExpandMasked(v, maskR);
                            gOut = ExpandMasked(v, maskG);
                            bOut = ExpandMasked(v, maskB);
                            if (maskA != 0)
                            {
                                aOut = ExpandMasked(v, maskA);
                            }
                            break;
                        }
                    case 24:
                        {
                            int p = rowStart + (x * 3);
                            bOut = d[p];
                            gOut = d[p + 1];
                            rOut = d[p + 2];
                            break;
                        }
                    default:
                        {
                            // 32-bit.
                            uint v = ReadU32(d, rowStart + (x * 4));
                            rOut = ExpandMasked(v, maskR);
                            gOut = ExpandMasked(v, maskG);
                            bOut = ExpandMasked(v, maskB);
                            if (maskA != 0)
                            {
                                aOut = ExpandMasked(v, maskA);
                            }
                            break;
                        }
                }

                frame.Pixels.SetPixelBgra(x, y, bOut, gOut, rOut, aOut);
            }
        }
    }

    private static int ReadPaletteIndex(byte[] d, int rowStart, int x, int bpp)
    {
        if (bpp == 8)
        {
            return d[rowStart + x];
        }
        if (bpp == 4)
        {
            byte b = d[rowStart + (x / 2)];
            return (x & 1) == 0 ? b >> 4 : b & 0x0F;
        }
        // 1-bit.
        byte bits = d[rowStart + (x / 8)];
        return (bits >> (7 - (x & 7))) & 1;
    }

    private static byte[] PaletteEntry(byte[][] palette, int index)
    {
        if (index < 0 || index >= palette.Length)
        {
            throw new ImageException($"BMP palette index {index} out of range [0, {palette.Length}).");
        }
        return palette[index];
    }

    private static void DecodeRle(
        byte[] d, int pos, ImageFrame frame,
        int width, int height, bool topDown, byte[][] palette, bool isRle4)
    {
        // RLE bitmaps are bottom-up unless the height was negative (top-down
        // RLE is not produced by Windows but is tolerated here).
        int x = 0;
        int row = 0;

        while (pos + 1 < d.Length)
        {
            byte count = d[pos];
            byte value = d[pos + 1];
            pos += 2;

            if (count > 0)
            {
                // Encoded run: `count` pixels of palette value(s).
                for (int i = 0; i < count && x < width; i++)
                {
                    int index;
                    if (isRle4)
                    {
                        index = (i & 1) == 0 ? value >> 4 : value & 0x0F;
                    }
                    else
                    {
                        index = value;
                    }
                    WriteRlePixel(frame, palette, x, row, height, topDown, index);
                    x++;
                }
                continue;
            }

            // Escape codes.
            if (value == 0)
            {
                // End of line.
                x = 0;
                row++;
                if (row >= height)
                {
                    return;
                }
                continue;
            }
            if (value == 1)
            {
                // End of bitmap.
                return;
            }
            if (value == 2)
            {
                // Delta: move right dx, up dy (toward later rows in file order).
                if (pos + 1 >= d.Length)
                {
                    throw new ImageException("BMP truncated inside RLE delta.");
                }
                x += d[pos];
                row += d[pos + 1];
                pos += 2;
                if (row >= height)
                {
                    return;
                }
                continue;
            }

            // Absolute mode: `value` literal pixels follow.
            int literal = value;
            int bytesNeeded = isRle4 ? (literal + 1) / 2 : literal;
            // Absolute runs are padded to a 16-bit boundary.
            int padded = (bytesNeeded + 1) & ~1;
            if (pos + padded > d.Length)
            {
                throw new ImageException("BMP truncated inside RLE absolute run.");
            }
            for (int i = 0; i < literal && x < width; i++)
            {
                int index;
                if (isRle4)
                {
                    byte b = d[pos + (i / 2)];
                    index = (i & 1) == 0 ? b >> 4 : b & 0x0F;
                }
                else
                {
                    index = d[pos + i];
                }
                WriteRlePixel(frame, palette, x, row, height, topDown, index);
                x++;
            }
            pos += padded;
        }
    }

    private static void WriteRlePixel(
        ImageFrame frame, byte[][] palette, int x, int row, int height, bool topDown, int index)
    {
        if (row >= height)
        {
            return;
        }
        int y = topDown ? row : height - 1 - row;
        byte[] entry = PaletteEntry(palette, index);
        frame.Pixels.SetPixelBgra(x, y, entry[0], entry[1], entry[2], 255);
    }

    // Expands the channel selected by `mask` from packed value `v` to 8 bits.
    private static byte ExpandMasked(uint v, uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        int shift = 0;
        uint m = mask;
        while ((m & 1) == 0)
        {
            m >>= 1;
            shift++;
        }

        int bits = 0;
        while (((m >> bits) & 1) == 1)
        {
            bits++;
        }

        uint raw = (v & mask) >> shift;
        if (bits >= 8)
        {
            return (byte)(raw >> (bits - 8));
        }

        // Scale up: replicate high bits into the low positions.
        uint maxIn = (1u << bits) - 1;
        return (byte)((raw * 255 + (maxIn / 2)) / maxIn);
    }

    private static ushort ReadU16(byte[] d, int p) => (ushort)(d[p] | (d[p + 1] << 8));

    private static int ReadI32(byte[] d, int p)
        => d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24);

    private static uint ReadU32(byte[] d, int p)
        => (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24));
}
