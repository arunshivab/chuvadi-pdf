// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.81 / ISO 10918-1 — Baseline sequential DCT (SOF0),
//        Annex K reference quantisation and Huffman tables; JFIF 1.02
// PHASE: Phase 2.9 — Reader feature batch (JPEG export)
// Encodes an ImageFrame as a baseline JFIF JPEG.

using System;
using System.IO;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Encodes images as baseline sequential JPEG (SOF0) with JFIF headers.
/// </summary>
/// <remarks>
/// <para>
/// Colour images encode as YCbCr with 4:4:4 sampling (no chroma
/// subsampling) — slightly larger files than 4:2:0 in exchange for clean
/// edges on rasterised text, which is the dominant content this encoder
/// serves. Grayscale frames (<see cref="ImageColorFormat.Gray8"/>) encode
/// as single-component JPEGs. Alpha is ignored (JPEG carries no alpha);
/// CMYK frames are not supported and throw.
/// </para>
/// <para>
/// The quality parameter follows the Independent JPEG Group convention:
/// 1 (worst) to 100 (best), scaling the Annex K reference quantisation
/// tables. The default of 85 matches common screenshot/export quality.
/// </para>
/// </remarks>
public static class JpegEncoder
{
    // ── Annex K reference quantisation tables (natural order) ────────────

    private static readonly byte[] LuminanceQuantBase =
    [
        16, 11, 10, 16, 24, 40, 51, 61,
        12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77,
        24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103, 99,
    ];

    private static readonly byte[] ChrominanceQuantBase =
    [
        17, 18, 24, 47, 99, 99, 99, 99,
        18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99,
        47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
    ];

    // ── Zigzag order: natural index for each zigzag position ─────────────

    private static readonly int[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10,
        17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    ];

    // ── Annex K Huffman tables: BITS (codes per length 1..16) + HUFFVAL ──

    private static readonly byte[] DcLuminanceBits =
        [0, 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];

    private static readonly byte[] DcLuminanceValues =
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    private static readonly byte[] DcChrominanceBits =
        [0, 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];

    private static readonly byte[] DcChrominanceValues =
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    private static readonly byte[] AcLuminanceBits =
        [0, 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7D];

    private static readonly byte[] AcLuminanceValues =
    [
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12,
        0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08,
        0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16,
        0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
        0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
        0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
        0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98,
        0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
        0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6,
        0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
        0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4,
        0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
        0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA,
        0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    private static readonly byte[] AcChrominanceBits =
        [0, 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77];

    private static readonly byte[] AcChrominanceValues =
    [
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21,
        0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
        0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91,
        0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0,
        0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34,
        0xE1, 0x25, 0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26,
        0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38,
        0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
        0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
        0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
        0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96,
        0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5,
        0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4,
        0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
        0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2,
        0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA,
        0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9,
        0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    /// <summary>
    /// Encodes the frame as a baseline JFIF JPEG and writes it to the stream.
    /// </summary>
    /// <param name="frame">The image to encode. Alpha channels are ignored.</param>
    /// <param name="output">The destination stream.</param>
    /// <param name="quality">
    /// Quality from 1 (smallest, worst) to 100 (largest, best), IJG
    /// convention. Default 85.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="frame"/> or <paramref name="output"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="quality"/> is outside 1–100.
    /// </exception>
    /// <exception cref="ImageException">
    /// Thrown when the frame's colour format cannot be represented in JPEG
    /// (e.g. <see cref="ImageColorFormat.Cmyk32"/>).
    /// </exception>
    public static void Encode(ImageFrame frame, Stream output, int quality = 85)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(output);
        if (quality < 1 || quality > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be 1..100.");
        }
        if (frame.OriginalFormat == ImageColorFormat.Cmyk32)
        {
            throw new ImageException("CMYK frames cannot be encoded as baseline JPEG.");
        }

        bool grayscale = frame.OriginalFormat == ImageColorFormat.Gray8;

        ushort[] lumaQuant = ScaleQuantTable(LuminanceQuantBase, quality);
        ushort[] chromaQuant = ScaleQuantTable(ChrominanceQuantBase, quality);

        HuffmanTable dcLuma = HuffmanTable.Build(DcLuminanceBits, DcLuminanceValues);
        HuffmanTable acLuma = HuffmanTable.Build(AcLuminanceBits, AcLuminanceValues);
        HuffmanTable dcChroma = HuffmanTable.Build(DcChrominanceBits, DcChrominanceValues);
        HuffmanTable acChroma = HuffmanTable.Build(AcChrominanceBits, AcChrominanceValues);

        WriteMarkers(output, frame.Width, frame.Height, grayscale, lumaQuant, chromaQuant);

        // Convert to planar Y / Cb / Cr (or Y only).
        int width = frame.Width;
        int height = frame.Height;
        double[] yPlane = new double[width * height];
        double[]? cbPlane = grayscale ? null : new double[width * height];
        double[]? crPlane = grayscale ? null : new double[width * height];

        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = frame.Pixels.GetRow(y);
            for (int x = 0; x < width; x++)
            {
                int si = x * 4;                       // BGRA source
                double b = row[si];
                double g = row[si + 1];
                double r = row[si + 2];
                int di = (y * width) + x;

                if (grayscale)
                {
                    // Gray frames store the gray value in all three channels.
                    yPlane[di] = g;
                }
                else
                {
                    yPlane[di] = (0.299 * r) + (0.587 * g) + (0.114 * b);
                    cbPlane![di] = 128.0 - (0.168736 * r) - (0.331264 * g) + (0.5 * b);
                    crPlane![di] = 128.0 + (0.5 * r) - (0.418688 * g) - (0.081312 * b);
                }
            }
        }

        BitWriter bits = new(output);
        int blocksX = (width + 7) / 8;
        int blocksY = (height + 7) / 8;

        int dcY = 0;
        int dcCb = 0;
        int dcCr = 0;
        double[] block = new double[64];
        int[] quantised = new int[64];

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                ExtractBlock(yPlane, width, height, bx, by, block);
                dcY = EncodeBlock(bits, block, lumaQuant, dcLuma, acLuma, dcY, quantised);

                if (!grayscale)
                {
                    ExtractBlock(cbPlane!, width, height, bx, by, block);
                    dcCb = EncodeBlock(bits, block, chromaQuant, dcChroma, acChroma, dcCb, quantised);

                    ExtractBlock(crPlane!, width, height, bx, by, block);
                    dcCr = EncodeBlock(bits, block, chromaQuant, dcChroma, acChroma, dcCr, quantised);
                }
            }
        }

        bits.Flush();
        output.WriteByte(0xFF);
        output.WriteByte(0xD9);                       // EOI
    }

    // ── Marker segments ───────────────────────────────────────────────────

    private static void WriteMarkers(
        Stream output, int width, int height, bool grayscale,
        ushort[] lumaQuant, ushort[] chromaQuant)
    {
        output.WriteByte(0xFF);
        output.WriteByte(0xD8);                       // SOI

        // APP0 / JFIF 1.02, no density, no thumbnail.
        WriteSegment(output, 0xE0,
        [
            (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0,
            1, 2,                                     // version 1.02
            0,                                        // units: none
            0, 1, 0, 1,                               // aspect 1:1
            0, 0,                                     // no thumbnail
        ]);

        // DQT — luminance always; chrominance only for colour.
        WriteQuantTable(output, 0, lumaQuant);
        if (!grayscale)
        {
            WriteQuantTable(output, 1, chromaQuant);
        }

        // SOF0 — baseline sequential, 8-bit precision, 1:1 sampling.
        int componentCount = grayscale ? 1 : 3;
        byte[] sof = new byte[6 + (componentCount * 3)];
        sof[0] = 8;
        sof[1] = (byte)(height >> 8);
        sof[2] = (byte)(height & 0xFF);
        sof[3] = (byte)(width >> 8);
        sof[4] = (byte)(width & 0xFF);
        sof[5] = (byte)componentCount;
        for (int c = 0; c < componentCount; c++)
        {
            sof[6 + (c * 3)] = (byte)(c + 1);         // component id
            sof[7 + (c * 3)] = 0x11;                  // 1×1 sampling (4:4:4)
            sof[8 + (c * 3)] = (byte)(c == 0 ? 0 : 1); // quant table
        }
        WriteSegment(output, 0xC0, sof);

        // DHT — DC/AC luminance always; chrominance only for colour.
        WriteHuffmanTable(output, 0x00, DcLuminanceBits, DcLuminanceValues);
        WriteHuffmanTable(output, 0x10, AcLuminanceBits, AcLuminanceValues);
        if (!grayscale)
        {
            WriteHuffmanTable(output, 0x01, DcChrominanceBits, DcChrominanceValues);
            WriteHuffmanTable(output, 0x11, AcChrominanceBits, AcChrominanceValues);
        }

        // SOS.
        byte[] sos = new byte[4 + (componentCount * 2)];
        sos[0] = (byte)componentCount;
        for (int c = 0; c < componentCount; c++)
        {
            sos[1 + (c * 2)] = (byte)(c + 1);
            sos[2 + (c * 2)] = (byte)(c == 0 ? 0x00 : 0x11);
        }
        sos[1 + (componentCount * 2)] = 0;            // spectral start
        sos[2 + (componentCount * 2)] = 63;           // spectral end
        sos[3 + (componentCount * 2)] = 0;            // approximation
        WriteSegment(output, 0xDA, sos);
    }

    private static void WriteSegment(Stream output, byte marker, byte[] payload)
    {
        int length = payload.Length + 2;
        output.WriteByte(0xFF);
        output.WriteByte(marker);
        output.WriteByte((byte)(length >> 8));
        output.WriteByte((byte)(length & 0xFF));
        output.Write(payload, 0, payload.Length);
    }

    private static void WriteQuantTable(Stream output, int id, ushort[] table)
    {
        // Baseline: 8-bit precision (all scaled values clamp to 255).
        byte[] payload = new byte[1 + 64];
        payload[0] = (byte)id;
        for (int i = 0; i < 64; i++)
        {
            payload[1 + i] = (byte)Math.Min(table[ZigZag[i]], (ushort)255);
        }
        WriteSegment(output, 0xDB, payload);
    }

    private static void WriteHuffmanTable(Stream output, byte classAndId, byte[] bits, byte[] values)
    {
        byte[] payload = new byte[1 + 16 + values.Length];
        payload[0] = classAndId;
        for (int i = 1; i <= 16; i++)
        {
            payload[i] = bits[i];
        }
        Array.Copy(values, 0, payload, 17, values.Length);
        WriteSegment(output, 0xC4, payload);
    }

    // ── Block pipeline ────────────────────────────────────────────────────

    private static ushort[] ScaleQuantTable(byte[] baseTable, int quality)
    {
        int scale = quality < 50 ? 5000 / quality : 200 - (quality * 2);
        ushort[] result = new ushort[64];
        for (int i = 0; i < 64; i++)
        {
            int v = ((baseTable[i] * scale) + 50) / 100;
            if (v < 1)
            {
                v = 1;
            }
            if (v > 255)
            {
                v = 255;
            }
            result[i] = (ushort)v;
        }
        return result;
    }

    // Copies one 8×8 block from the plane, replicating edge samples for
    // blocks that overhang the right/bottom edges, with level shift −128.
    private static void ExtractBlock(double[] plane, int width, int height, int bx, int by, double[] block)
    {
        for (int y = 0; y < 8; y++)
        {
            int sy = (by * 8) + y;
            if (sy >= height)
            {
                sy = height - 1;
            }
            for (int x = 0; x < 8; x++)
            {
                int sx = (bx * 8) + x;
                if (sx >= width)
                {
                    sx = width - 1;
                }
                block[(y * 8) + x] = plane[(sy * width) + sx] - 128.0;
            }
        }
    }

    private static int EncodeBlock(
        BitWriter bits, double[] block, ushort[] quant,
        HuffmanTable dcTable, HuffmanTable acTable, int previousDc, int[] quantised)
    {
        ForwardDct(block);

        for (int i = 0; i < 64; i++)
        {
            int natural = ZigZag[i];
            quantised[i] = (int)Math.Round(block[natural] / quant[natural]);
        }

        // DC: differential, category + magnitude bits.
        int dc = quantised[0];
        int diff = dc - previousDc;
        int dcCategory = Category(diff);
        bits.WriteCode(dcTable.Codes[dcCategory], dcTable.Lengths[dcCategory]);
        if (dcCategory > 0)
        {
            bits.WriteBits(MagnitudeBits(diff, dcCategory), dcCategory);
        }

        // AC: run-length of zeros + category, ZRL for runs ≥ 16, EOB at end.
        int run = 0;
        for (int i = 1; i < 64; i++)
        {
            int value = quantised[i];
            if (value == 0)
            {
                run++;
                continue;
            }

            while (run >= 16)
            {
                bits.WriteCode(acTable.Codes[0xF0], acTable.Lengths[0xF0]);   // ZRL
                run -= 16;
            }

            int category = Category(value);
            int symbol = (run << 4) | category;
            bits.WriteCode(acTable.Codes[symbol], acTable.Lengths[symbol]);
            bits.WriteBits(MagnitudeBits(value, category), category);
            run = 0;
        }

        if (run > 0)
        {
            bits.WriteCode(acTable.Codes[0x00], acTable.Lengths[0x00]);       // EOB
        }

        return dc;
    }

    // Number of bits needed to represent |value| (JPEG SSSS category).
    private static int Category(int value)
    {
        int magnitude = Math.Abs(value);
        int category = 0;
        while (magnitude > 0)
        {
            magnitude >>= 1;
            category++;
        }
        return category;
    }

    // The additional bits for a value in the given category: the value
    // itself when positive, value − 1 in two's complement (low bits) when
    // negative (T.81 F.1.2.1).
    private static int MagnitudeBits(int value, int category)
    {
        if (value >= 0)
        {
            return value;
        }
        return value + (1 << category) - 1;
    }

    // In-place 2D DCT-II on an 8×8 block (separable, plain double maths —
    // clarity over speed; encoding is not a hot path).
    private static void ForwardDct(double[] block)
    {
        Span<double> temp = stackalloc double[64];

        // Rows.
        for (int y = 0; y < 8; y++)
        {
            for (int u = 0; u < 8; u++)
            {
                double sum = 0;
                for (int x = 0; x < 8; x++)
                {
                    sum += block[(y * 8) + x] * CosTable[(x * 8) + u];
                }
                temp[(y * 8) + u] = sum * Scale(u);
            }
        }

        // Columns.
        for (int u = 0; u < 8; u++)
        {
            for (int v = 0; v < 8; v++)
            {
                double sum = 0;
                for (int y = 0; y < 8; y++)
                {
                    sum += temp[(y * 8) + u] * CosTable[(y * 8) + v];
                }
                block[(v * 8) + u] = sum * Scale(v);
            }
        }
    }

    private static double Scale(int index) => index == 0 ? 0.353553390593273762 : 0.5;

    // CosTable[x*8+u] = cos((2x+1)·u·π/16).
    private static readonly double[] CosTable = BuildCosTable();

    private static double[] BuildCosTable()
    {
        double[] table = new double[64];
        for (int x = 0; x < 8; x++)
        {
            for (int u = 0; u < 8; u++)
            {
                table[(x * 8) + u] = Math.Cos(((2 * x) + 1) * u * Math.PI / 16.0);
            }
        }
        return table;
    }

    // ── Canonical Huffman code assignment (T.81 C.2) ──────────────────────

    private sealed class HuffmanTable
    {
        private HuffmanTable(int[] codes, int[] lengths)
        {
            Codes = codes;
            Lengths = lengths;
        }

        internal int[] Codes { get; }

        internal int[] Lengths { get; }

        internal static HuffmanTable Build(byte[] bits, byte[] values)
        {
            int[] codes = new int[256];
            int[] lengths = new int[256];
            int code = 0;
            int k = 0;
            for (int length = 1; length <= 16; length++)
            {
                for (int i = 0; i < bits[length]; i++)
                {
                    codes[values[k]] = code;
                    lengths[values[k]] = length;
                    code++;
                    k++;
                }
                code <<= 1;
            }
            return new HuffmanTable(codes, lengths);
        }
    }

    // ── Entropy bit writer with 0xFF byte stuffing ────────────────────────

    private sealed class BitWriter
    {
        private readonly Stream _output;
        private int _buffer;
        private int _bitCount;

        internal BitWriter(Stream output)
        {
            _output = output;
        }

        internal void WriteCode(int code, int length)
        {
            WriteBits(code, length);
        }

        internal void WriteBits(int value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                _buffer = (_buffer << 1) | ((value >> i) & 1);
                _bitCount++;
                if (_bitCount == 8)
                {
                    EmitByte((byte)_buffer);
                    _buffer = 0;
                    _bitCount = 0;
                }
            }
        }

        internal void Flush()
        {
            // Pad the final partial byte with 1-bits (T.81 F.1.2.3).
            if (_bitCount > 0)
            {
                int padded = (_buffer << (8 - _bitCount)) | ((1 << (8 - _bitCount)) - 1);
                EmitByte((byte)padded);
                _buffer = 0;
                _bitCount = 0;
            }
        }

        private void EmitByte(byte value)
        {
            _output.WriteByte(value);
            if (value == 0xFF)
            {
                _output.WriteByte(0x00);              // byte stuffing
            }
        }
    }
}
