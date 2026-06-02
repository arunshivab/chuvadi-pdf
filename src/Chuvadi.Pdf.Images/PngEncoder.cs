// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PNG Specification 1.2 §1–§5 — Chunk structure, IHDR, IDAT, IEND
//        PNG §6 — Filter methods (Sub filter for RGB rows)
//        RFC 1950 — zlib wrapper used in PNG IDAT chunks
// PHASE: Phase 2 — Chuvadi.Pdf.Images
// Encodes an ImageFrame to PNG using the existing DeflateFilter.

using System;
using System.IO;
using Chuvadi.Pdf.Filters;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Encodes an <see cref="ImageFrame"/> to PNG format.
/// </summary>
/// <remarks>
/// Writes a valid PNG file using the existing <see cref="DeflateFilter"/>
/// for IDAT compression. Produces 24-bit RGB PNG (no alpha) or 32-bit RGBA.
/// Uses the Sub row filter for good compression on photographic content.
///
/// PNG Specification 1.2 — http://www.libpng.org/pub/png/spec/1.2/
/// RFC 1950 — zlib format wrapping the DEFLATE stream in IDAT chunks.
/// </remarks>
public static class PngEncoder
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    // PNG filter types (PNG spec §6.2)
    private const byte FilterSub = 1;

    /// <summary>
    /// Encodes an <see cref="ImageFrame"/> to PNG and writes it to
    /// <paramref name="output"/>.
    /// </summary>
    /// <param name="frame">The image to encode.</param>
    /// <param name="output">The stream to write the PNG to. Must be writable.</param>
    /// <param name="includeAlpha">
    /// True to write 32-bit RGBA PNG (colour type 6);
    /// false to write 24-bit RGB PNG (colour type 2).
    /// </param>
    public static void Encode(ImageFrame frame, Stream output, bool includeAlpha = false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(output);

        int width = frame.Width;
        int height = frame.Height;
        int channels = includeAlpha ? 4 : 3;
        byte colorType = includeAlpha ? (byte)6 : (byte)2; // 6=RGBA, 2=RGB

        // PNG signature
        output.Write(PngSignature, 0, 8);

        // IHDR chunk
        byte[] ihdrData = new byte[13];
        WriteUInt32Be(ihdrData, 0, (uint)width);
        WriteUInt32Be(ihdrData, 4, (uint)height);
        ihdrData[8] = 8;         // bit depth
        ihdrData[9] = colorType;
        ihdrData[10] = 0;         // compression: deflate
        ihdrData[11] = 0;         // filter: adaptive
        ihdrData[12] = 0;         // interlace: none
        WriteChunk(output, "IHDR", ihdrData);

        // Build filtered scanlines, then compress into IDAT
        byte[] filtered = BuildFilteredRows(frame, width, height, channels, includeAlpha);
        byte[] compressed = CompressWithZlib(filtered);
        WriteChunk(output, "IDAT", compressed);

        // IEND chunk (empty)
        WriteChunk(output, "IEND", []);
    }

    // ── Row filtering ────────────────────────────────────────────────────────

    private static byte[] BuildFilteredRows(
        ImageFrame frame, int width, int height, int channels, bool includeAlpha)
    {
        // Each row: 1 filter byte + (width × channels) data bytes
        byte[] filtered = new byte[height * (1 + width * channels)];
        int dstOffset = 0;

        for (int y = 0; y < height; y++)
        {
            System.ReadOnlySpan<byte> srcRow = frame.Pixels.GetRow(y);

            // Extract the colour channels we want
            byte[] rowData = new byte[width * channels];

            for (int x = 0; x < width; x++)
            {
                int srcIdx = x * 4; // BGRA source
                int dstIdx = x * channels;

                if (includeAlpha)
                {
                    rowData[dstIdx] = srcRow[srcIdx + 2]; // R
                    rowData[dstIdx + 1] = srcRow[srcIdx + 1]; // G
                    rowData[dstIdx + 2] = srcRow[srcIdx];     // B
                    rowData[dstIdx + 3] = srcRow[srcIdx + 3]; // A
                }
                else
                {
                    rowData[dstIdx] = srcRow[srcIdx + 2]; // R
                    rowData[dstIdx + 1] = srcRow[srcIdx + 1]; // G
                    rowData[dstIdx + 2] = srcRow[srcIdx];     // B
                }
            }

            // Apply Sub filter (good for photos, safe for all content)
            // PNG spec §6.3: Sub(x) = Raw(x) - Raw(x - bpp)
            filtered[dstOffset++] = FilterSub;

            for (int i = 0; i < rowData.Length; i++)
            {
                byte left = i >= channels ? rowData[i - channels] : (byte)0;
                filtered[dstOffset++] = (byte)(rowData[i] - left);
            }
        }

        return filtered;
    }

    // ── zlib / DEFLATE compression ───────────────────────────────────────────

    private static byte[] CompressWithZlib(byte[] raw)
    {
        // DeflateFilter.Encode already emits a complete zlib stream
        // (RFC 1950): the 2-byte CMF/FLG header, the DEFLATE payload, and the
        // trailing 4-byte Adler-32. PNG IDAT data is exactly that zlib stream,
        // so we use the encoder's output verbatim. Wrapping it again here would
        // produce a doubly-framed stream that no PNG decoder can inflate.
        DeflateFilter deflate = new DeflateFilter();

        using MemoryStream inputStream = new MemoryStream(raw);
        using MemoryStream outputStream = new MemoryStream();
        deflate.Encode(inputStream, outputStream, null);
        return outputStream.ToArray();
    }

    // ── PNG chunk writing ──────────────────────────────────────────────────────

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        // Length (4 bytes big-endian)
        WriteUInt32BeToStream(output, (uint)data.Length);

        // Type (4 ASCII bytes)
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes, 0, 4);

        // Data
        if (data.Length > 0)
        {
            output.Write(data, 0, data.Length);
        }

        // CRC-32 of type + data
        uint crc = Crc32(typeBytes, data);
        WriteUInt32BeToStream(output, crc);
    }

    private static void WriteUInt32BeToStream(Stream s, uint value)
    {
        s.WriteByte((byte)((value >> 24) & 0xFF));
        s.WriteByte((byte)((value >> 16) & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
        s.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteUInt32Be(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)((value >> 24) & 0xFF);
        buf[offset + 1] = (byte)((value >> 16) & 0xFF);
        buf[offset + 2] = (byte)((value >> 8) & 0xFF);
        buf[offset + 3] = (byte)(value & 0xFF);
    }

    // ── CRC-32 (PNG spec §5) ────────────────────────────────────────────────

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private static uint[] BuildCrc32Table()
    {
        uint[] table = new uint[256];

        for (uint n = 0; n < 256; n++)
        {
            uint c = n;

            for (int k = 0; k < 8; k++)
            {
                if ((c & 1) != 0)
                {
                    c = 0xEDB88320 ^ (c >> 1);
                }
                else
                {
                    c >>= 1;
                }
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] typeBytes, byte[] data)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte b in typeBytes)
        {
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte b in data)
        {
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }
}
