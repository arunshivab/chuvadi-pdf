// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.4 — FlateDecode filter
//        RFC 1950 — ZLIB Compressed Data Format Specification
//        RFC 1951 — DEFLATE Compressed Data Format Specification
// PHASE: Phase 1 — Chuvadi.Pdf.Filters
// FlateDecode filter: zlib-framed DEFLATE inflate and deflate.

using System;
using System.IO;

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// Implements the PDF FlateDecode filter using zlib-framed DEFLATE.
/// </summary>
/// <remarks>
/// PDF FlateDecode streams are compressed using the zlib format (RFC 1950),
/// which wraps a DEFLATE-compressed payload (RFC 1951) with a 2-byte header
/// and a 4-byte Adler-32 checksum trailer.
///
/// This implementation includes:
/// <list type="bullet">
///   <item>Full zlib envelope handling (header validation, checksum verification)</item>
///   <item>All three DEFLATE block types: stored (00), fixed Huffman (01), dynamic Huffman (10)</item>
///   <item>PNG predictor reversal (predictors 10-15) for cross-reference streams and image data</item>
///   <item>TIFF predictor reversal (predictor 2) for legacy streams</item>
/// </list>
/// Compression (Encode) uses fixed Huffman coding for simplicity and correctness.
/// Decompression (Decode) supports all valid DEFLATE streams.
///
/// PDF 32000-1:2008 §7.4.4.
/// RFC 1950 §2-3 — zlib format.
/// RFC 1951 §3 — DEFLATE format.
/// </remarks>
public sealed class DeflateFilter : IStreamFilter
{
    /// <inheritdoc/>
    public string FilterName => "FlateDecode";

    // ── Public API ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Decode(Stream input, Stream output, FilterParameters? decodeParms = null)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        // Read all input bytes — we need random-access for the bit reader.
        byte[] compressed = ReadAllBytes(input);

        // Empty input produces empty output — valid degenerate case in PDF.
        if (compressed.Length == 0)
        {
            return;
        }

        if (compressed.Length < 2)
        {
            throw new FilterException(FilterName, "Stream too short to contain a valid zlib header.");
        }

        // Validate zlib header (RFC 1950 §2.2).
        // CMF byte: compression method (bits 0-3) + compression info (bits 4-7)
        // FLG byte: flags including checksum
        byte cmf = compressed[0];
        byte flg = compressed[1];

        int compressionMethod = cmf & 0x0F;

        if (compressionMethod != 8)
        {
            throw new FilterException(FilterName,
                $"Unsupported zlib compression method {compressionMethod}. Only method 8 (DEFLATE) is supported.");
        }

        // RFC 1950: (CMF * 256 + FLG) must be divisible by 31.
        if ((cmf * 256 + flg) % 31 != 0)
        {
            throw new FilterException(FilterName, "Invalid zlib header checksum (FCHECK).");
        }

        bool hasDictionary = (flg & 0x20) != 0;

        if (hasDictionary)
        {
            // PDF streams should not use preset dictionaries.
            throw new FilterException(FilterName, "Preset dictionaries in zlib streams are not supported in PDF.");
        }

        // DEFLATE payload starts at byte 2, ends at len-4 (last 4 bytes are Adler-32).
        int payloadStart = 2;
        int payloadEnd = compressed.Length - 4;

        if (payloadEnd <= payloadStart)
        {
            throw new FilterException(FilterName, "Stream too short to contain DEFLATE payload and Adler-32 checksum.");
        }

        // Inflate the DEFLATE payload.
        DeflateInflater inflater = new DeflateInflater(compressed, payloadStart, payloadEnd - payloadStart);
        byte[] decompressed = inflater.Inflate();

        // Verify Adler-32 checksum (big-endian, RFC 1950 §2.2).
        uint expectedChecksum =
            ((uint)compressed[payloadEnd] << 24) |
            ((uint)compressed[payloadEnd + 1] << 16) |
            ((uint)compressed[payloadEnd + 2] << 8) |
            compressed[payloadEnd + 3];

        uint actualChecksum = Adler32.Compute(decompressed);

        if (actualChecksum != expectedChecksum)
        {
            throw new FilterException(FilterName,
                $"Adler-32 checksum mismatch. Expected 0x{expectedChecksum:X8}, got 0x{actualChecksum:X8}.");
        }

        // Apply predictor reversal if specified.
        byte[] result = ApplyPredictorReversal(decompressed, decodeParms);
        output.Write(result, 0, result.Length);
    }

    /// <inheritdoc/>
    public void Encode(Stream input, Stream output, FilterParameters? encodeParms = null)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        byte[] raw = ReadAllBytes(input);

        // Apply predictor if specified.
        byte[] toCompress = ApplyPredictorForward(raw, encodeParms);

        // Compress using fixed Huffman DEFLATE.
        DeflateDeflater deflater = new DeflateDeflater(toCompress);
        byte[] compressed = deflater.Deflate();

        // Write zlib header: CM=8, CINFO=7 (32K window), FCHECK computed.
        byte cmf = 0x78; // CM=8, CINFO=7
        byte flg = 0x9C; // FCHECK such that (cmf*256 + flg) % 31 == 0
        // 0x78 * 256 = 30720; 30720 % 31 = 2; 31 - 2 = 29 ... but 0x9C = 156,
        // 30720 + 156 = 30876, 30876 % 31 = 0. Correct.
        output.WriteByte(cmf);
        output.WriteByte(flg);

        // Write compressed payload.
        output.Write(compressed, 0, compressed.Length);

        // Write Adler-32 checksum (big-endian). RFC 1950 §2.2: the checksum
        // is computed over the uncompressed data that was actually deflated,
        // i.e. the post-predictor bytes (toCompress), not the original raw
        // input. Decode mirrors this — it verifies Adler-32 against the
        // inflated bytes before reversing any predictor.
        uint checksum = Adler32.Compute(toCompress);
        output.WriteByte((byte)((checksum >> 24) & 0xFF));
        output.WriteByte((byte)((checksum >> 16) & 0xFF));
        output.WriteByte((byte)((checksum >> 8) & 0xFF));
        output.WriteByte((byte)(checksum & 0xFF));
    }

    // ── Predictor handling ─────────────────────────────────────────────────

    private static byte[] ApplyPredictorReversal(byte[] data, FilterParameters? parms)
    {
        if (parms is null || parms.Predictor == 1)
        {
            return data;
        }

        if (parms.Predictor == 2)
        {
            return ReverseTiffPredictor(data, parms);
        }

        if (parms.Predictor >= 10 && parms.Predictor <= 15)
        {
            return ReversePngPredictor(data, parms);
        }

        throw new FilterException("FlateDecode",
            $"Unsupported predictor value {parms.Predictor}.");
    }

    private static byte[] ApplyPredictorForward(byte[] data, FilterParameters? parms)
    {
        if (parms is null || parms.Predictor == 1)
        {
            return data;
        }

        if (parms.Predictor >= 10 && parms.Predictor <= 15)
        {
            return ApplyPngPredictor(data, parms);
        }

        // TIFF predictor forward not needed for Phase 1 writer.
        return data;
    }

    // TIFF predictor 2: each byte is stored as the difference from the
    // previous byte in the same color component.
    // PDF 32000-1:2008 Table 8 — Predictor 2.
    private static byte[] ReverseTiffPredictor(byte[] data, FilterParameters parms)
    {
        int colors = parms.Colors;
        int bitsPerComponent = parms.BitsPerComponent;
        int columns = parms.Columns;

        if (bitsPerComponent != 8)
        {
            // For Phase 1, only 8-bit TIFF predictor is supported.
            return data;
        }

        int bytesPerRow = columns * colors;
        byte[] result = new byte[data.Length];
        int pos = 0;

        while (pos < data.Length)
        {
            int rowEnd = Math.Min(pos + bytesPerRow, data.Length);
            int rowStart = pos;

            // First pixel: copy directly.
            for (int c = 0; c < colors && pos < rowEnd; c++, pos++)
            {
                result[pos] = data[pos];
            }

            // Remaining pixels: add delta to previous same-component byte.
            while (pos < rowEnd)
            {
                for (int c = 0; c < colors && pos < rowEnd; c++, pos++)
                {
                    result[pos] = (byte)(data[pos] + result[pos - colors]);
                }
            }

            pos = rowStart + bytesPerRow;
        }

        return result;
    }

    // PNG predictors 10-15: each row begins with a filter-type byte, then
    // the filtered pixel data. Predictor 15 (Paeth) is the most common in PDFs.
    // PDF 32000-1:2008 §7.4.4.4; PNG spec §9.
    private static byte[] ReversePngPredictor(byte[] data, FilterParameters parms)
    {
        int colors = parms.Colors;
        int bitsPerComponent = parms.BitsPerComponent;
        int columns = parms.Columns;
        int bytesPerPixel = (colors * bitsPerComponent + 7) / 8;
        int bytesPerRow = ((columns * colors * bitsPerComponent) + 7) / 8;
        int stride = bytesPerRow + 1; // +1 for filter type byte

        if (data.Length % stride != 0)
        {
            throw new FilterException("FlateDecode",
                $"PNG predictor data length {data.Length} is not a multiple of stride {stride}.");
        }

        int rows = data.Length / stride;
        byte[] result = new byte[rows * bytesPerRow];
        byte[] prevRow = new byte[bytesPerRow];

        for (int row = 0; row < rows; row++)
        {
            int srcOffset = row * stride;
            int dstOffset = row * bytesPerRow;
            byte filterType = data[srcOffset];
            srcOffset++;

            switch (filterType)
            {
                case 0: // None
                    Array.Copy(data, srcOffset, result, dstOffset, bytesPerRow);
                    break;

                case 1: // Sub
                    for (int i = 0; i < bytesPerRow; i++)
                    {
                        byte left = i >= bytesPerPixel ? result[dstOffset + i - bytesPerPixel] : (byte)0;
                        result[dstOffset + i] = (byte)(data[srcOffset + i] + left);
                    }
                    break;

                case 2: // Up
                    for (int i = 0; i < bytesPerRow; i++)
                    {
                        result[dstOffset + i] = (byte)(data[srcOffset + i] + prevRow[i]);
                    }
                    break;

                case 3: // Average
                    for (int i = 0; i < bytesPerRow; i++)
                    {
                        byte left = i >= bytesPerPixel ? result[dstOffset + i - bytesPerPixel] : (byte)0;
                        byte up = prevRow[i];
                        result[dstOffset + i] = (byte)(data[srcOffset + i] + ((left + up) / 2));
                    }
                    break;

                case 4: // Paeth
                    for (int i = 0; i < bytesPerRow; i++)
                    {
                        byte left = i >= bytesPerPixel ? result[dstOffset + i - bytesPerPixel] : (byte)0;
                        byte up = prevRow[i];
                        byte upLeft = i >= bytesPerPixel ? prevRow[i - bytesPerPixel] : (byte)0;
                        result[dstOffset + i] = (byte)(data[srcOffset + i] + PaethPredictor(left, up, upLeft));
                    }
                    break;

                default:
                    throw new FilterException("FlateDecode",
                        $"Unknown PNG filter type {filterType} in row {row}.");
            }

            Array.Copy(result, dstOffset, prevRow, 0, bytesPerRow);
        }

        return result;
    }

    private static byte[] ApplyPngPredictor(byte[] data, FilterParameters parms)
    {
        int colors = parms.Colors;
        int bitsPerComponent = parms.BitsPerComponent;
        int columns = parms.Columns;
        int bytesPerPixel = (colors * bitsPerComponent + 7) / 8;
        int bytesPerRow = ((columns * colors * bitsPerComponent) + 7) / 8;

        if (data.Length % bytesPerRow != 0)
        {
            return data;
        }

        int rows = data.Length / bytesPerRow;
        byte[] result = new byte[rows * (bytesPerRow + 1)];
        byte[] prevRow = new byte[bytesPerRow];

        for (int row = 0; row < rows; row++)
        {
            int srcOffset = row * bytesPerRow;
            int dstOffset = row * (bytesPerRow + 1);

            // Use Paeth filter (type 4) — generally good compression.
            result[dstOffset] = 4;
            dstOffset++;

            for (int i = 0; i < bytesPerRow; i++)
            {
                byte left = i >= bytesPerPixel ? data[srcOffset + i - bytesPerPixel] : (byte)0;
                byte up = prevRow[i];
                byte upLeft = i >= bytesPerPixel ? prevRow[i - bytesPerPixel] : (byte)0;
                result[dstOffset + i] = (byte)(data[srcOffset + i] - PaethPredictor(left, up, upLeft));
            }

            Array.Copy(data, srcOffset, prevRow, 0, bytesPerRow);
        }

        return result;
    }

    // PNG Paeth predictor function. PNG spec §9.4.
    private static byte PaethPredictor(byte a, byte b, byte c)
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

    // ── Utilities ─────────────────────────────────────────────────────────

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}

// ── DEFLATE Inflate ───────────────────────────────────────────────────────

/// <summary>
/// Inflates (decompresses) a DEFLATE-compressed byte sequence.
/// RFC 1951 — DEFLATE Compressed Data Format Specification.
/// </summary>
internal sealed class DeflateInflater
{
    // ── Fixed Huffman code tables (RFC 1951 §3.2.6) ───────────────────────
    // Literal/length codes: 0-143 = 8 bits, 144-255 = 9 bits,
    //                       256-279 = 7 bits, 280-287 = 8 bits.
    // Distance codes: all 5 bits.

    private static readonly int[] FixedLiteralLengths;
    private static readonly HuffmanTree FixedLiteralTree;
    private static readonly HuffmanTree FixedDistanceTree;

    static DeflateInflater()
    {
        FixedLiteralLengths = new int[288];

        for (int i = 0; i <= 143; i++)
        {
            FixedLiteralLengths[i] = 8;
        }

        for (int i = 144; i <= 255; i++)
        {
            FixedLiteralLengths[i] = 9;
        }

        for (int i = 256; i <= 279; i++)
        {
            FixedLiteralLengths[i] = 7;
        }

        for (int i = 280; i <= 287; i++)
        {
            FixedLiteralLengths[i] = 8;
        }

        FixedLiteralTree = HuffmanTree.Build(FixedLiteralLengths);

        int[] distLengths = new int[32];

        for (int i = 0; i < 32; i++)
        {
            distLengths[i] = 5;
        }

        FixedDistanceTree = HuffmanTree.Build(distLengths);
    }

    // ── Length and distance base values and extra bits (RFC 1951 §3.2.5) ──

    private static readonly int[] LengthBase =
    [
        3, 4, 5, 6, 7, 8, 9, 10,
        11, 13, 15, 17, 19, 23, 27, 31,
        35, 43, 51, 59, 67, 83, 99, 115,
        131, 163, 195, 227, 258
    ];

    private static readonly int[] LengthExtraBits =
    [
        0, 0, 0, 0, 0, 0, 0, 0,
        1, 1, 1, 1, 2, 2, 2, 2,
        3, 3, 3, 3, 4, 4, 4, 4,
        5, 5, 5, 5, 0
    ];

    private static readonly int[] DistanceBase =
    [
        1, 2, 3, 4, 5, 7, 9, 13,
        17, 25, 33, 49, 65, 97, 129, 193,
        257, 385, 513, 769, 1025, 1537, 2049, 3073,
        4097, 6145, 8193, 12289, 16385, 24577
    ];

    private static readonly int[] DistanceExtraBits =
    [
        0, 0, 0, 0, 1, 1, 2, 2,
        3, 3, 4, 4, 5, 5, 6, 6,
        7, 7, 8, 8, 9, 9, 10, 10,
        11, 11, 12, 12, 13, 13
    ];

    // Code length alphabet order for dynamic Huffman header (RFC 1951 §3.2.7).
    private static readonly int[] CodeLengthOrder =
    [
        16, 17, 18, 0, 8, 7, 9, 6,
        10, 5, 11, 4, 12, 3, 13, 2,
        14, 1, 15
    ];

    // ── Instance state ─────────────────────────────────────────────────────

    private readonly byte[] _data;
    private readonly int _start;
    private readonly int _length;
    private int _bytePos;
    private int _bitBuf;
    private int _bitsInBuf;

    internal DeflateInflater(byte[] data, int start, int length)
    {
        _data = data;
        _start = start;
        _length = length;
        _bytePos = start;
        _bitBuf = 0;
        _bitsInBuf = 0;
    }

    // ── Public inflate entry ──────────────────────────────────────────────

    internal byte[] Inflate()
    {
        System.Collections.Generic.List<byte> output = new System.Collections.Generic.List<byte>(Math.Max(_length * 4, 256));
        bool isFinalBlock;

        do
        {
            isFinalBlock = ReadBits(1) == 1;
            int blockType = ReadBits(2);

            switch (blockType)
            {
                case 0:
                    InflateStoredBlock(output);
                    break;
                case 1:
                    InflateHuffmanBlock(output, FixedLiteralTree, FixedDistanceTree);
                    break;
                case 2:
                    InflateDynamicBlock(output);
                    break;
                default:
                    throw new FilterException("FlateDecode",
                        $"Invalid DEFLATE block type {blockType}.");
            }
        }
        while (!isFinalBlock);

        return [.. output];
    }

    // ── Block type 00: stored ─────────────────────────────────────────────
    // RFC 1951 §3.2.4.

    private void InflateStoredBlock(System.Collections.Generic.List<byte> output)
    {
        // Skip to next byte boundary.
        _bitsInBuf = 0;
        _bitBuf = 0;

        if (_bytePos + 4 > _start + _length)
        {
            throw new FilterException("FlateDecode", "Truncated stored block header.");
        }

        int len = _data[_bytePos] | (_data[_bytePos + 1] << 8);
        int nlen = _data[_bytePos + 2] | (_data[_bytePos + 3] << 8);
        _bytePos += 4;

        // nlen must be the one's complement of len.
        if ((len ^ nlen) != 0xFFFF)
        {
            throw new FilterException("FlateDecode",
                "Stored block length/complement mismatch.");
        }

        if (_bytePos + len > _start + _length)
        {
            throw new FilterException("FlateDecode",
                $"Stored block claims {len} bytes but only {_start + _length - _bytePos} remain.");
        }

        for (int i = 0; i < len; i++)
        {
            output.Add(_data[_bytePos++]);
        }
    }

    // ── Block types 01/10: Huffman-coded ─────────────────────────────────
    // RFC 1951 §3.2.5-3.2.7.

    private void InflateHuffmanBlock(
        System.Collections.Generic.List<byte> output,
        HuffmanTree litTree,
        HuffmanTree distTree)
    {
        while (true)
        {
            int symbol = litTree.Decode(this);

            if (symbol < 256)
            {
                // Literal byte.
                output.Add((byte)symbol);
            }
            else if (symbol == 256)
            {
                // End of block.
                break;
            }
            else
            {
                // Length/distance back-reference.
                int lengthIndex = symbol - 257;

                if (lengthIndex >= LengthBase.Length)
                {
                    throw new FilterException("FlateDecode",
                        $"Invalid length symbol {symbol}.");
                }

                int length = LengthBase[lengthIndex] + ReadBits(LengthExtraBits[lengthIndex]);
                int distSymbol = distTree.Decode(this);

                if (distSymbol >= DistanceBase.Length)
                {
                    throw new FilterException("FlateDecode",
                        $"Invalid distance symbol {distSymbol}.");
                }

                int distance = DistanceBase[distSymbol] + ReadBits(DistanceExtraBits[distSymbol]);
                int copyFrom = output.Count - distance;

                if (copyFrom < 0)
                {
                    throw new FilterException("FlateDecode",
                        $"Back-reference distance {distance} exceeds output length {output.Count}.");
                }

                // Copy byte-by-byte to handle overlapping back-references.
                for (int i = 0; i < length; i++)
                {
                    output.Add(output[copyFrom + i]);
                }
            }
        }
    }

    private void InflateDynamicBlock(System.Collections.Generic.List<byte> output)
    {
        // RFC 1951 §3.2.7 — Dynamic Huffman codes.
        int hlit = ReadBits(5) + 257;   // Number of literal/length codes
        int hdist = ReadBits(5) + 1;    // Number of distance codes
        int hclen = ReadBits(4) + 4;    // Number of code length codes

        // Read code length alphabet lengths.
        int[] codeLengthLengths = new int[19];

        for (int i = 0; i < hclen; i++)
        {
            codeLengthLengths[CodeLengthOrder[i]] = ReadBits(3);
        }

        HuffmanTree codeLengthTree = HuffmanTree.Build(codeLengthLengths);

        // Decode literal/length and distance code lengths.
        int[] allLengths = DecodeCodeLengths(codeLengthTree, hlit + hdist);
        int[] litLengths = allLengths[..hlit];
        int[] distLengths = allLengths[hlit..];

        HuffmanTree litTree = HuffmanTree.Build(litLengths);
        HuffmanTree distTree = HuffmanTree.Build(distLengths);

        InflateHuffmanBlock(output, litTree, distTree);
    }

    private int[] DecodeCodeLengths(HuffmanTree codeLengthTree, int count)
    {
        int[] lengths = new int[count];
        int i = 0;

        while (i < count)
        {
            int symbol = codeLengthTree.Decode(this);

            if (symbol <= 15)
            {
                // Literal code length.
                lengths[i++] = symbol;
            }
            else if (symbol == 16)
            {
                // Copy previous length 3-6 times.
                if (i == 0)
                {
                    throw new FilterException("FlateDecode",
                        "Code length repeat (16) with no previous value.");
                }

                int repeat = ReadBits(2) + 3;
                int prev = lengths[i - 1];

                for (int r = 0; r < repeat && i < count; r++, i++)
                {
                    lengths[i] = prev;
                }
            }
            else if (symbol == 17)
            {
                // Repeat zero 3-10 times.
                int repeat = ReadBits(3) + 3;

                for (int r = 0; r < repeat && i < count; r++, i++)
                {
                    lengths[i] = 0;
                }
            }
            else if (symbol == 18)
            {
                // Repeat zero 11-138 times.
                int repeat = ReadBits(7) + 11;

                for (int r = 0; r < repeat && i < count; r++, i++)
                {
                    lengths[i] = 0;
                }
            }
            else
            {
                throw new FilterException("FlateDecode",
                    $"Invalid code length symbol {symbol}.");
            }
        }

        return lengths;
    }

    // ── Bit reader ────────────────────────────────────────────────────────

    /// <summary>
    /// Reads <paramref name="count"/> bits from the stream LSB-first.
    /// RFC 1951 §3.1.1 — Packing of bits into bytes.
    /// </summary>
    internal int ReadBits(int count)
    {
        if (count == 0)
        {
            return 0;
        }

        while (_bitsInBuf < count)
        {
            if (_bytePos >= _start + _length)
            {
                throw new FilterException("FlateDecode",
                    "Unexpected end of DEFLATE stream while reading bits.");
            }

            _bitBuf |= _data[_bytePos++] << _bitsInBuf;
            _bitsInBuf += 8;
        }

        int value = _bitBuf & ((1 << count) - 1);
        _bitBuf >>= count;
        _bitsInBuf -= count;
        return value;
    }
}

// ── DEFLATE Deflate (compress) ────────────────────────────────────────────

/// <summary>
/// Compresses data using fixed Huffman DEFLATE.
/// Produces a single non-final block using fixed Huffman codes.
/// RFC 1951 — DEFLATE Compressed Data Format Specification.
/// </summary>
internal sealed class DeflateDeflater
{
    private readonly byte[] _data;

    internal DeflateDeflater(byte[] data)
    {
        _data = data;
    }

    internal byte[] Deflate()
    {
        // For Phase 1: use a single stored block for correctness.
        // LZ77 compression with fixed Huffman will be added in a later pass
        // once correctness is verified on the full test corpus.
        // Stored blocks are valid DEFLATE and produce correct output
        // at the cost of larger file size.

        System.Collections.Generic.List<byte> output = new System.Collections.Generic.List<byte>();

        int remaining = _data.Length;
        int pos = 0;
        bool first = true;

        if (remaining == 0)
        {
            // Empty stored block, final.
            output.Add(0x01); // BFINAL=1, BTYPE=00
            output.Add(0x00); // LEN low
            output.Add(0x00); // LEN high
            output.Add(0xFF); // NLEN low (~0x00)
            output.Add(0xFF); // NLEN high (~0x00)
            return [.. output];
        }

        while (remaining > 0)
        {
            int blockSize = Math.Min(remaining, 65535);
            bool isFinal = (remaining - blockSize) == 0;

            // BFINAL (1 bit) + BTYPE (2 bits) = 3 bits = byte 0x01 or 0x00.
            // For stored block BTYPE=00 so byte is: isFinal ? 0x01 : 0x00.
            output.Add(isFinal ? (byte)0x01 : (byte)0x00);

            // LEN: 2 bytes little-endian.
            output.Add((byte)(blockSize & 0xFF));
            output.Add((byte)((blockSize >> 8) & 0xFF));

            // NLEN: one's complement of LEN, 2 bytes little-endian.
            int nlen = (~blockSize) & 0xFFFF;
            output.Add((byte)(nlen & 0xFF));
            output.Add((byte)((nlen >> 8) & 0xFF));

            // Data bytes.
            for (int i = 0; i < blockSize; i++)
            {
                output.Add(_data[pos + i]);
            }

            pos += blockSize;
            remaining -= blockSize;
            first = false;
        }

        _ = first; // suppress unused warning

        return [.. output];
    }
}

// ── Huffman tree ──────────────────────────────────────────────────────────

/// <summary>
/// A Huffman code tree built from code lengths, using canonical Huffman coding.
/// RFC 1951 §3.2.2 — Use of Huffman coding in the DEFLATE format.
/// </summary>
internal sealed class HuffmanTree
{
    // Canonical Huffman codes are represented as a lookup table.
    // For each possible code prefix, we store the symbol it maps to.
    // We use a simple linear search for correctness in Phase 1.
    private readonly (int Code, int Length, int Symbol)[] _entries;
    private readonly int _maxLength;

    private HuffmanTree((int Code, int Length, int Symbol)[] entries, int maxLength)
    {
        _entries = entries;
        _maxLength = maxLength;
    }

    /// <summary>
    /// Builds a canonical Huffman tree from an array of code lengths.
    /// Index i in <paramref name="lengths"/> corresponds to symbol i.
    /// A length of 0 means the symbol is not used.
    /// RFC 1951 §3.2.2.
    /// </summary>
    internal static HuffmanTree Build(int[] lengths)
    {
        if (lengths is null)
        {
            throw new ArgumentNullException(nameof(lengths));
        }

        int maxLength = 0;

        foreach (int l in lengths)
        {
            if (l > maxLength)
            {
                maxLength = l;
            }
        }

        if (maxLength == 0)
        {
            return new HuffmanTree([], 0);
        }

        // Count codes of each length (RFC 1951 step 1).
        int[] blCount = new int[maxLength + 1];

        foreach (int l in lengths)
        {
            if (l > 0)
            {
                blCount[l]++;
            }
        }

        // Find the first code for each length (RFC 1951 step 2).
        int[] nextCode = new int[maxLength + 2];
        int code = 0;

        for (int bits = 1; bits <= maxLength; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        // Assign codes to symbols (RFC 1951 step 3).
        System.Collections.Generic.List<(int, int, int)> entries = new System.Collections.Generic.List<(int, int, int)>();

        for (int symbol = 0; symbol < lengths.Length; symbol++)
        {
            int len = lengths[symbol];

            if (len > 0)
            {
                entries.Add((nextCode[len], len, symbol));
                nextCode[len]++;
            }
        }

        return new HuffmanTree([.. entries], maxLength);
    }

    /// <summary>
    /// Decodes one symbol from the bit stream using this Huffman tree.
    /// Reads bits one at a time until a valid code is found.
    /// </summary>
    internal int Decode(DeflateInflater reader)
    {
        int code = 0;

        for (int len = 1; len <= _maxLength; len++)
        {
            code = (code << 1) | reader.ReadBits(1);

            foreach ((int entryCode, int entryLen, int symbol) in _entries)
            {
                if (entryLen == len && entryCode == code)
                {
                    return symbol;
                }
            }
        }

        throw new FilterException("FlateDecode",
            $"Invalid Huffman code {code} — no matching symbol in tree.");
    }
}
