// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PNG Specification 1.2 §3 — File structure
//        PNG §4 — Chunk specifications (IHDR, IDAT, PLTE, tRNS)
//        PNG §6 — Filter reconstruction (None, Sub, Up, Average, Paeth)
// PHASE: Phase 2 — Chuvadi.Pdf.Images
// Decodes a PNG byte stream into an ImageFrame.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Decodes a PNG image into an <see cref="ImageFrame"/>.
/// </summary>
/// <remarks>
/// Supports:
/// <list type="bullet">
///   <item>Colour types: Grayscale (0), RGB (2), Indexed (3), Grayscale+Alpha (4), RGBA (6)</item>
///   <item>Bit depths: 1, 2, 4, 8 (16-bit downsampled to 8-bit)</item>
///   <item>Row filters: None, Sub, Up, Average, Paeth</item>
///   <item>Interlacing: None (Adam7 interlace not supported)</item>
/// </list>
/// PNG Specification 1.2 — http://www.libpng.org/pub/png/spec/1.2/
/// </remarks>
public static class PngDecoder
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>
    /// Decodes a PNG from a byte array.
    /// </summary>
    /// <param name="data">The raw PNG bytes.</param>
    /// <returns>A decoded <see cref="ImageFrame"/>.</returns>
    /// <exception cref="ImageException">Thrown on invalid PNG data.</exception>
    public static ImageFrame Decode(byte[] data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        using (MemoryStream ms = new MemoryStream(data))
        {
            return Decode(ms);
        }
    }

    /// <summary>
    /// Decodes a PNG from a stream.
    /// </summary>
    public static ImageFrame Decode(Stream input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        // Verify PNG signature (8 bytes)
        byte[] sig = new byte[8];
        ReadExact(input, sig, 8);

        for (int i = 0; i < 8; i++)
        {
            if (sig[i] != PngSignature[i])
            {
                throw new ImageException("Not a valid PNG: signature mismatch.");
            }
        }

        // Parse chunks
        int width = 0;
        int height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        byte interlace = 0;
        byte[]? palette = null;
        byte[]? transparency = null;
        List<byte[]> idatChunks = new List<byte[]>();
        bool hasIhdr = false;

        while (true)
        {
            uint chunkLen = ReadUInt32Be(input);

            byte[] typeBytes = new byte[4];
            ReadExact(input, typeBytes, 4);
            string type = System.Text.Encoding.ASCII.GetString(typeBytes);

            byte[] chunkData = new byte[chunkLen];

            if (chunkLen > 0)
            {
                ReadExact(input, chunkData, (int)chunkLen);
            }

            ReadUInt32Be(input); // CRC — we trust the data

            if (type == "IHDR")
            {
                if (chunkLen < 13)
                {
                    throw new ImageException("PNG IHDR chunk too short.");
                }

                width = (int)ReadUInt32BeFrom(chunkData, 0);
                height = (int)ReadUInt32BeFrom(chunkData, 4);
                bitDepth = chunkData[8];
                colorType = chunkData[9];
                interlace = chunkData[12];
                hasIhdr = true;
            }
            else if (type == "PLTE")
            {
                palette = chunkData;
            }
            else if (type == "tRNS")
            {
                transparency = chunkData;
            }
            else if (type == "IDAT")
            {
                idatChunks.Add(chunkData);
            }
            else if (type == "IEND")
            {
                break;
            }
        }

        if (!hasIhdr)
        {
            throw new ImageException("PNG missing IHDR chunk.");
        }

        if (interlace != 0)
        {
            throw new ImageException("PNG Adam7 interlacing is not supported.");
        }

        // Concatenate all IDAT chunks and decompress
        int totalIdat = 0;

        foreach (byte[] chunk in idatChunks)
        {
            totalIdat += chunk.Length;
        }

        byte[] compressed = new byte[totalIdat];
        int offset = 0;

        foreach (byte[] chunk in idatChunks)
        {
            Array.Copy(chunk, 0, compressed, offset, chunk.Length);
            offset += chunk.Length;
        }

        byte[] decompressed = DecompressZlib(compressed);

        // Reconstruct pixels from filtered scanlines
        return Reconstruct(decompressed, width, height, bitDepth, colorType, palette, transparency);
    }

    // ── Zlib decompression ────────────────────────────────────────────────

    private static byte[] DecompressZlib(byte[] data)
    {
        // Skip 2-byte zlib header (CMF + FLG), strip 4-byte Adler-32 trailer
        if (data.Length < 6)
        {
            throw new ImageException("PNG IDAT data too short.");
        }

        // DeflateFilter consumes the complete zlib envelope (2-byte header,
        // DEFLATE payload, 4-byte Adler-32 trailer). Pass the IDAT bytes
        // through unmodified rather than pre-stripping the header/trailer.
        DeflateFilter filter = new DeflateFilter();

        using (MemoryStream input = new MemoryStream(data))
        using (MemoryStream output = new MemoryStream())
        {
            filter.Decode(input, output, null);
            return output.ToArray();
        }
    }

    // ── Scanline reconstruction ───────────────────────────────────────────

    private static ImageFrame Reconstruct(
        byte[] raw, int width, int height,
        byte bitDepth, byte colorType,
        byte[]? palette, byte[]? transparency)
    {
        // Samples per pixel and bytes per sample
        int samplesPerPixel = colorType switch
        {
            0 => 1, // Grayscale
            2 => 3, // RGB
            3 => 1, // Indexed
            4 => 2, // Grayscale + Alpha
            6 => 4, // RGBA
            _ => throw new ImageException($"Unsupported PNG colour type {colorType}."),
        };

        // Bytes per pixel (rounded up for bit depths < 8)
        int bpp = Math.Max(1, samplesPerPixel * bitDepth / 8);
        int rowBytes = (width * samplesPerPixel * bitDepth + 7) / 8;

        PixelBuffer buffer = new PixelBuffer(width, height);
        byte[] prevRow = new byte[rowBytes];
        int rawOffset = 0;

        for (int y = 0; y < height; y++)
        {
            if (rawOffset >= raw.Length)
            {
                throw new ImageException("PNG scanline data truncated.");
            }

            byte filterByte = raw[rawOffset++];
            byte[] row = new byte[rowBytes];

            int toCopy = Math.Min(rowBytes, raw.Length - rawOffset);
            Array.Copy(raw, rawOffset, row, 0, toCopy);
            rawOffset += rowBytes;

            // Apply filter reconstruction (PNG spec §6.3)
            ApplyFilter(filterByte, row, prevRow, bpp);

            // Convert row to BGRA and write to PixelBuffer
            WriteRowToBuffer(buffer, row, y, width, bitDepth, colorType, palette, transparency);

            byte[] temp = prevRow;
            prevRow = row;
            row = temp;
        }

        ImageColorFormat format = colorType switch
        {
            0 => ImageColorFormat.Gray8,
            2 => ImageColorFormat.Rgb24,
            3 => ImageColorFormat.Rgb24,
            4 => ImageColorFormat.Gray8,
            6 => ImageColorFormat.Rgba32,
            _ => ImageColorFormat.Rgb24,
        };

        return new ImageFrame(buffer, format);
    }

    private static void ApplyFilter(byte filterType, byte[] row, byte[] prev, int bpp)
    {
        switch (filterType)
        {
            case 0: // None
                break;

            case 1: // Sub: row[i] += row[i - bpp]
                for (int i = bpp; i < row.Length; i++)
                {
                    row[i] = (byte)(row[i] + row[i - bpp]);
                }
                break;

            case 2: // Up: row[i] += prev[i]
                for (int i = 0; i < row.Length; i++)
                {
                    row[i] = (byte)(row[i] + prev[i]);
                }
                break;

            case 3: // Average: row[i] += floor((row[i-bpp] + prev[i]) / 2)
                for (int i = 0; i < row.Length; i++)
                {
                    int left = i >= bpp ? row[i - bpp] : 0;
                    row[i] = (byte)(row[i] + ((left + prev[i]) >> 1));
                }
                break;

            case 4: // Paeth predictor
                for (int i = 0; i < row.Length; i++)
                {
                    int left = i >= bpp ? row[i - bpp] : 0;
                    int up = prev[i];
                    int upLeft = i >= bpp ? prev[i - bpp] : 0;
                    row[i] = (byte)(row[i] + PaethPredictor(left, up, upLeft));
                }
                break;

            default:
                throw new ImageException($"Unknown PNG filter type {filterType}.");
        }
    }

    private static int PaethPredictor(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        if (pb <= pc)
        {
            return b;
        }

        return c;
    }

    private static void WriteRowToBuffer(
        PixelBuffer buffer, byte[] row, int y, int width,
        byte bitDepth, byte colorType, byte[]? palette, byte[]? transparency)
    {
        for (int x = 0; x < width; x++)
        {
            byte r, g, b, a;

            switch (colorType)
            {
                case 0: // Grayscale
                    byte gray = SampleAt(row, x, 1, bitDepth);
                    r = g = b = gray;
                    a = transparency != null && transparency.Length >= 2
                        && ((transparency[0] << 8) | transparency[1]) == gray
                        ? (byte)0 : (byte)255;
                    break;

                case 2: // RGB
                    r = SampleAt(row, x * 3, 1, bitDepth);
                    g = SampleAt(row, x * 3 + 1, 1, bitDepth);
                    b = SampleAt(row, x * 3 + 2, 1, bitDepth);
                    a = 255;
                    break;

                case 3: // Indexed
                    int idx = SampleAt(row, x, 1, bitDepth);
                    int pi = idx * 3;
                    r = palette != null && pi + 2 < palette.Length ? palette[pi] : (byte)0;
                    g = palette != null && pi + 2 < palette.Length ? palette[pi + 1] : (byte)0;
                    b = palette != null && pi + 2 < palette.Length ? palette[pi + 2] : (byte)0;
                    a = transparency != null && idx < transparency.Length
                        ? transparency[idx] : (byte)255;
                    break;

                case 4: // Grayscale + Alpha
                    byte ga = bitDepth == 16
                        ? row[x * 4]
                        : row[x * 2];
                    r = g = b = ga;
                    a = bitDepth == 16 ? row[x * 4 + 2] : row[x * 2 + 1];
                    break;

                case 6: // RGBA
                    if (bitDepth == 16)
                    {
                        r = row[x * 8];
                        g = row[x * 8 + 2];
                        b = row[x * 8 + 4];
                        a = row[x * 8 + 6];
                    }
                    else
                    {
                        r = row[x * 4];
                        g = row[x * 4 + 1];
                        b = row[x * 4 + 2];
                        a = row[x * 4 + 3];
                    }
                    break;

                default:
                    r = g = b = a = 0;
                    break;
            }

            buffer.SetPixelBgra(x, y, b, g, r, a);
        }
    }

    private static byte SampleAt(byte[] row, int index, int samplesPerPixel, byte bitDepth)
    {
        if (bitDepth >= 8)
        {
            int byteIdx = index * (bitDepth / 8) * samplesPerPixel / samplesPerPixel;

            if (byteIdx >= row.Length)
            {
                return 0;
            }

            byte val = row[byteIdx];

            // 16-bit: take high byte only (downsample to 8-bit)
            return val;
        }

        // Sub-byte: 1, 2, or 4 bits per sample
        int pixelsPerByte = 8 / bitDepth;
        int byteIndex = index / pixelsPerByte;
        int bitShift = (pixelsPerByte - 1 - (index % pixelsPerByte)) * bitDepth;
        int mask = (1 << bitDepth) - 1;

        if (byteIndex >= row.Length)
        {
            return 0;
        }

        int sample = (row[byteIndex] >> bitShift) & mask;
        // Scale to 8-bit
        return bitDepth == 1 ? (byte)(sample * 255)
             : bitDepth == 2 ? (byte)(sample * 85)
             : (byte)(sample * 17); // 4-bit: 0..15 → 0..255
    }

    // ── Stream helpers ────────────────────────────────────────────────────

    private static void ReadExact(Stream s, byte[] buf, int count)
    {
        int read = 0;

        while (read < count)
        {
            int n = s.Read(buf, read, count - read);

            if (n == 0)
            {
                throw new ImageException("PNG data truncated unexpectedly.");
            }

            read += n;
        }
    }

    private static uint ReadUInt32Be(Stream s)
    {
        byte[] buf = new byte[4];
        ReadExact(s, buf, 4);
        return ReadUInt32BeFrom(buf, 0);
    }

    private static uint ReadUInt32BeFrom(byte[] buf, int offset)
    {
        return ((uint)buf[offset] << 24)
             | ((uint)buf[offset + 1] << 16)
             | ((uint)buf[offset + 2] << 8)
             | buf[offset + 3];
    }
}
