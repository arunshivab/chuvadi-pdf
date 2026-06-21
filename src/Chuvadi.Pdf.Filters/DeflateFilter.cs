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
    private readonly DeflateEffort _effort;

    /// <summary>
    /// Initialises a <see cref="DeflateFilter"/>.
    /// </summary>
    /// <param name="effort">
    /// Encoder effort. <see cref="DeflateEffort.Default"/> uses the fast
    /// greedy-parse fixed/dynamic-Huffman path. <see cref="DeflateEffort.Maximum"/>
    /// additionally tries the BCL deflater and an iterated optimal ("zopfli-style")
    /// parse, keeping whichever candidate is smallest — at the cost of speed.
    /// </param>
    public DeflateFilter(DeflateEffort effort = DeflateEffort.Default)
    {
        _effort = effort;
    }

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

        // Compress using DEFLATE at the configured effort.
        DeflateDeflater deflater = new DeflateDeflater(toCompress, _effort);
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
/// Compresses data using DEFLATE (RFC 1951): LZ77 matching followed by the
/// smaller of fixed-Huffman, dynamic-Huffman, or stored encoding.
/// </summary>
internal sealed class DeflateDeflater
{
    // LZ77 + Huffman DEFLATE (RFC 1951 §3.2.5-3.2.7). Hash-chain match search
    // over a 32 KiB window produces a token stream once; that stream is then
    // emitted with fixed and with dynamic Huffman codes and the smaller is
    // kept, falling back to stored blocks for incompressible input. The
    // companion inflater in this file decodes all three block types.
    private const int MinMatch = 3;
    private const int MaxMatch = 258;
    private const int WindowSize = 32768;
    private const int HashBits = 15;
    private const int HashSize = 1 << HashBits;
    private const int MaxChainLength = 128;

    // Symbol-space sizes (RFC 1951 §3.2.5-3.2.7).
    private const int LitLenSymbols = 286;
    private const int DistanceSymbols = 30;
    private const int CodeLengthSymbols = 19;
    private const int MaxCodeBits = 15;
    private const int MaxCodeLengthBits = 7;
    private const int EndOfBlock = 256;

    private readonly byte[] _data;
    private readonly DeflateEffort _effort;

    internal DeflateDeflater(byte[] data, DeflateEffort effort = DeflateEffort.Default)
    {
        _data = data;
        _effort = effort;
    }

    // A literal byte or a back-reference (length, distance) produced by LZ77.
    private readonly struct Token
    {
        internal Token(byte literal)
        {
            IsMatch = false;
            Literal = literal;
            Length = 0;
            Distance = 0;
        }

        internal Token(int length, int distance)
        {
            IsMatch = true;
            Literal = 0;
            Length = length;
            Distance = distance;
        }

        internal bool IsMatch { get; }

        internal byte Literal { get; }

        internal int Length { get; }

        internal int Distance { get; }
    }

    internal byte[] Deflate()
    {
        if (_data.Length == 0)
        {
            return DeflateStored();
        }

        System.Collections.Generic.List<Token> tokens = Tokenize();

        byte[] best = EmitFixed(tokens);
        byte[] dynamicBytes = EmitDynamic(tokens);
        if (dynamicBytes.Length < best.Length)
        {
            best = dynamicBytes;
        }

        if (_effort == DeflateEffort.Maximum)
        {
            // (a) Runtime (BCL) deflater — a strong lazy-matching parse.
            byte[]? bcl = TryDeflateBcl();
            if (bcl is not null && bcl.Length < best.Length)
            {
                best = bcl;
            }

            // (c) Iterated optimal ("zopfli-style") parse, emitted both ways.
            System.Collections.Generic.List<Token> optimal = TokenizeOptimal();
            byte[] optimalDynamic = EmitDynamic(optimal);
            if (optimalDynamic.Length < best.Length)
            {
                best = optimalDynamic;
            }

            byte[] optimalFixed = EmitFixed(optimal);
            if (optimalFixed.Length < best.Length)
            {
                best = optimalFixed;
            }
        }

        int storedSize = StoredSize(_data.Length);
        return best.Length <= storedSize ? best : DeflateStored();
    }

    // ── (a) Runtime deflater candidate ─────────────────────────────────
    // Produces raw DEFLATE via the BCL at maximum compression. Returns null on
    // any failure so the custom candidates still apply.
    private byte[]? TryDeflateBcl()
    {
        try
        {
            using System.IO.MemoryStream output = new System.IO.MemoryStream();
            using (System.IO.Compression.DeflateStream ds = new System.IO.Compression.DeflateStream(
                output, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            {
                ds.Write(_data, 0, _data.Length);
            }

            return output.ToArray();
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    private static int StoredSize(int dataLength)
    {
        if (dataLength == 0)
        {
            return 5;
        }
        int blocks = (dataLength + 65534) / 65535;
        return dataLength + (blocks * 5);
    }

    // ── LZ77 tokenization ──────────────────────────────────────────────

    private System.Collections.Generic.List<Token> Tokenize()
    {
        System.Collections.Generic.List<Token> tokens = new System.Collections.Generic.List<Token>();

        int[] head = new int[HashSize];
        int[] prev = new int[WindowSize];
        for (int i = 0; i < HashSize; i++)
        {
            head[i] = -1;
        }

        int pos = 0;
        int length = _data.Length;

        while (pos < length)
        {
            int matchLength = 0;
            int matchDistance = 0;

            if (pos + MinMatch <= length)
            {
                int hash = Hash(pos);
                int candidate = head[hash];
                int chain = MaxChainLength;
                int limit = pos - WindowSize;

                while (candidate >= 0 && candidate > limit && chain-- > 0)
                {
                    int candidateLength = MatchLength(candidate, pos, length);
                    if (candidateLength > matchLength)
                    {
                        matchLength = candidateLength;
                        matchDistance = pos - candidate;
                        if (matchLength >= MaxMatch)
                        {
                            break;
                        }
                    }
                    candidate = prev[candidate & (WindowSize - 1)];
                }
            }

            if (matchLength >= MinMatch)
            {
                tokens.Add(new Token(matchLength, matchDistance));

                int stop = Math.Min(pos + matchLength, length - MinMatch + 1);
                for (int p = pos; p < stop; p++)
                {
                    InsertHash(head, prev, p);
                }
                pos += matchLength;
            }
            else
            {
                tokens.Add(new Token(_data[pos]));
                if (pos + MinMatch <= length)
                {
                    InsertHash(head, prev, pos);
                }
                pos++;
            }
        }

        return tokens;
    }

    private int Hash(int pos)
    {
        return ((_data[pos] << 10) ^ (_data[pos + 1] << 5) ^ _data[pos + 2]) & (HashSize - 1);
    }

    private void InsertHash(int[] head, int[] prev, int pos)
    {
        int hash = Hash(pos);
        prev[pos & (WindowSize - 1)] = head[hash];
        head[hash] = pos;
    }

    private int MatchLength(int candidate, int pos, int length)
    {
        int max = Math.Min(MaxMatch, length - pos);
        int n = 0;
        while (n < max && _data[candidate + n] == _data[pos + n])
        {
            n++;
        }
        return n;
    }

    // ── Fixed-Huffman emission (RFC 1951 §3.2.6) ───────────────────────

    private byte[] EmitFixed(System.Collections.Generic.List<Token> tokens)
    {
        LsbBitWriter writer = new();

        // BFINAL = 1, BTYPE = 01 (fixed Huffman).
        writer.WriteBits(1, 1);
        writer.WriteBits(1, 2);

        foreach (Token token in tokens)
        {
            if (token.IsMatch)
            {
                EmitLengthDistance(writer, token.Length, token.Distance);
            }
            else
            {
                EmitLiteral(writer, token.Literal);
            }
        }

        EmitEndOfBlock(writer);
        return writer.ToArray();
    }

    private static void EmitLiteral(LsbBitWriter writer, byte value)
    {
        if (value <= 143)
        {
            writer.WriteBitsMsbFirst(0x30 + value, 8);
        }
        else
        {
            writer.WriteBitsMsbFirst(0x190 + (value - 144), 9);
        }
    }

    private static void EmitEndOfBlock(LsbBitWriter writer)
    {
        writer.WriteBitsMsbFirst(0, 7);                 // symbol 256
    }

    private static void EmitLengthSymbol(LsbBitWriter writer, int symbol)
    {
        if (symbol <= 279)
        {
            writer.WriteBitsMsbFirst(symbol - 256, 7);  // 256-279: 7 bits
        }
        else
        {
            writer.WriteBitsMsbFirst(0xC0 + (symbol - 280), 8);
        }
    }

    private static void EmitLengthDistance(LsbBitWriter writer, int length, int distance)
    {
        int lengthCode = LengthCodeOf(length);
        EmitLengthSymbol(writer, 257 + lengthCode);
        if (LengthExtraBits[lengthCode] > 0)
        {
            writer.WriteBits(length - LengthBase[lengthCode], LengthExtraBits[lengthCode]);
        }

        int distanceCode = DistanceCodeOf(distance);
        writer.WriteBitsMsbFirst(distanceCode, 5);
        if (DistanceExtraBits[distanceCode] > 0)
        {
            writer.WriteBits(distance - DistanceBase[distanceCode], DistanceExtraBits[distanceCode]);
        }
    }

    // ── Dynamic-Huffman emission (RFC 1951 §3.2.7) ─────────────────────

    private byte[] EmitDynamic(System.Collections.Generic.List<Token> tokens)
    {
        int[] litLenFreq = new int[LitLenSymbols];
        int[] distFreq = new int[DistanceSymbols];

        foreach (Token token in tokens)
        {
            if (token.IsMatch)
            {
                litLenFreq[257 + LengthCodeOf(token.Length)]++;
                distFreq[DistanceCodeOf(token.Distance)]++;
            }
            else
            {
                litLenFreq[token.Literal]++;
            }
        }

        litLenFreq[EndOfBlock]++;

        int[] litLenLengths = BuildCodeLengths(litLenFreq, MaxCodeBits);
        int[] distLengths = BuildCodeLengths(distFreq, MaxCodeBits);

        // DEFLATE requires at least one distance code, even when no matches
        // were emitted; supply a single unused code of length 1.
        if (CountNonZero(distLengths) == 0)
        {
            distLengths[0] = 1;
        }

        int[] litLenCodes = BuildCanonicalCodes(litLenLengths);
        int[] distCodes = BuildCanonicalCodes(distLengths);

        int hlit = Math.Max(257, LastNonZeroIndex(litLenLengths) + 1);
        int hdist = LastNonZeroIndex(distLengths) + 1;
        if (hdist < 1)
        {
            hdist = 1;
        }

        // Combined code-length sequence (literal/length then distance).
        int[] combined = new int[hlit + hdist];
        Array.Copy(litLenLengths, 0, combined, 0, hlit);
        Array.Copy(distLengths, 0, combined, hlit, hdist);

        System.Collections.Generic.List<RleItem> rle = RunLengthEncode(combined);

        int[] codeLengthFreq = new int[CodeLengthSymbols];
        foreach (RleItem item in rle)
        {
            codeLengthFreq[item.Symbol]++;
        }

        int[] codeLengthLengths = BuildCodeLengths(codeLengthFreq, MaxCodeLengthBits);
        int[] codeLengthCodes = BuildCanonicalCodes(codeLengthLengths);

        int hclen = 4;
        for (int k = CodeLengthSymbols - 1; k >= 4; k--)
        {
            if (codeLengthLengths[CodeLengthOrder[k]] > 0)
            {
                hclen = k + 1;
                break;
            }
        }

        LsbBitWriter writer = new();

        // BFINAL = 1, BTYPE = 10 (dynamic Huffman).
        writer.WriteBits(1, 1);
        writer.WriteBits(2, 2);

        writer.WriteBits(hlit - 257, 5);
        writer.WriteBits(hdist - 1, 5);
        writer.WriteBits(hclen - 4, 4);

        for (int k = 0; k < hclen; k++)
        {
            writer.WriteBits(codeLengthLengths[CodeLengthOrder[k]], 3);
        }

        foreach (RleItem item in rle)
        {
            writer.WriteBitsMsbFirst(codeLengthCodes[item.Symbol], codeLengthLengths[item.Symbol]);
            if (item.ExtraBits > 0)
            {
                writer.WriteBits(item.Extra, item.ExtraBits);
            }
        }

        foreach (Token token in tokens)
        {
            if (token.IsMatch)
            {
                int lengthCode = LengthCodeOf(token.Length);
                int lengthSymbol = 257 + lengthCode;
                writer.WriteBitsMsbFirst(litLenCodes[lengthSymbol], litLenLengths[lengthSymbol]);
                if (LengthExtraBits[lengthCode] > 0)
                {
                    writer.WriteBits(token.Length - LengthBase[lengthCode], LengthExtraBits[lengthCode]);
                }

                int distanceCode = DistanceCodeOf(token.Distance);
                writer.WriteBitsMsbFirst(distCodes[distanceCode], distLengths[distanceCode]);
                if (DistanceExtraBits[distanceCode] > 0)
                {
                    writer.WriteBits(token.Distance - DistanceBase[distanceCode], DistanceExtraBits[distanceCode]);
                }
            }
            else
            {
                writer.WriteBitsMsbFirst(litLenCodes[token.Literal], litLenLengths[token.Literal]);
            }
        }

        writer.WriteBitsMsbFirst(litLenCodes[EndOfBlock], litLenLengths[EndOfBlock]);
        return writer.ToArray();
    }

    // A code-length-alphabet symbol (0-18) plus any RLE extra bits.
    private readonly struct RleItem
    {
        internal RleItem(int symbol, int extra, int extraBits)
        {
            Symbol = symbol;
            Extra = extra;
            ExtraBits = extraBits;
        }

        internal int Symbol { get; }

        internal int Extra { get; }

        internal int ExtraBits { get; }
    }

    // RLE the code-length sequence using symbols 0-15 plus 16 (repeat previous
    // 3-6), 17 (zero run 3-10), 18 (zero run 11-138). RFC 1951 §3.2.7.
    private static System.Collections.Generic.List<RleItem> RunLengthEncode(int[] lengths)
    {
        System.Collections.Generic.List<RleItem> items = new System.Collections.Generic.List<RleItem>();

        int i = 0;
        while (i < lengths.Length)
        {
            int value = lengths[i];
            int run = 1;
            while (i + run < lengths.Length && lengths[i + run] == value)
            {
                run++;
            }

            if (value == 0)
            {
                while (run >= 11)
                {
                    int take = Math.Min(run, 138);
                    items.Add(new RleItem(18, take - 11, 7));
                    run -= take;
                    i += take;
                }
                while (run >= 3)
                {
                    int take = Math.Min(run, 10);
                    items.Add(new RleItem(17, take - 3, 3));
                    run -= take;
                    i += take;
                }
                while (run > 0)
                {
                    items.Add(new RleItem(0, 0, 0));
                    run--;
                    i++;
                }
            }
            else
            {
                items.Add(new RleItem(value, 0, 0));
                run--;
                i++;
                while (run >= 3)
                {
                    int take = Math.Min(run, 6);
                    items.Add(new RleItem(16, take - 3, 2));
                    run -= take;
                    i += take;
                }
                while (run > 0)
                {
                    items.Add(new RleItem(value, 0, 0));
                    run--;
                    i++;
                }
            }
        }

        return items;
    }

    // ── Length-limited Huffman code-length construction ────────────────

    // Builds canonical code lengths (each <= maxBits) for the given symbol
    // frequencies. Unused symbols get length 0. Uses a Huffman tree for the
    // initial lengths, the zlib bit-length overflow redistribution to enforce
    // the limit, then assigns shortest codes to the most frequent symbols.
    private static int[] BuildCodeLengths(int[] freqs, int maxBits)
    {
        int n = freqs.Length;
        int[] lengths = new int[n];

        System.Collections.Generic.List<int> active = new System.Collections.Generic.List<int>();
        for (int s = 0; s < n; s++)
        {
            if (freqs[s] > 0)
            {
                active.Add(s);
            }
        }

        if (active.Count == 0)
        {
            return lengths;
        }
        if (active.Count == 1)
        {
            lengths[active[0]] = 1;
            return lengths;
        }

        int m = active.Count;
        int maxNodes = (2 * m) - 1;
        long[] weight = new long[maxNodes];
        int[] left = new int[maxNodes];
        int[] right = new int[maxNodes];
        bool[] used = new bool[maxNodes];

        for (int i = 0; i < m; i++)
        {
            weight[i] = freqs[active[i]];
            left[i] = -1;
            right[i] = -1;
        }

        int count = m;
        for (int step = 0; step < m - 1; step++)
        {
            int a = PickMin(weight, used, count);
            used[a] = true;
            int b = PickMin(weight, used, count);
            used[b] = true;
            weight[count] = weight[a] + weight[b];
            left[count] = a;
            right[count] = b;
            count++;
        }

        int[] depth = new int[count];
        ComputeDepths(count - 1, left, right, depth);

        int[] blCount = new int[maxBits + 1];
        int overflow = 0;
        for (int i = 0; i < m; i++)
        {
            int d = depth[i];
            if (d > maxBits)
            {
                d = maxBits;
                overflow++;
            }
            blCount[d]++;
        }

        if (overflow > 0)
        {
            do
            {
                int bits = maxBits - 1;
                while (blCount[bits] == 0)
                {
                    bits--;
                }
                blCount[bits]--;
                blCount[bits + 1] += 2;
                blCount[maxBits]--;
                overflow -= 2;
            }
            while (overflow > 0);
        }

        // Least frequent symbols receive the longest codes.
        active.Sort((x, y) => freqs[x] != freqs[y] ? freqs[x].CompareTo(freqs[y]) : x.CompareTo(y));

        int idx = 0;
        for (int bits = maxBits; bits >= 1; bits--)
        {
            int c = blCount[bits];
            for (int j = 0; j < c; j++)
            {
                lengths[active[idx]] = bits;
                idx++;
            }
        }

        return lengths;
    }

    private static int PickMin(long[] weight, bool[] used, int count)
    {
        int best = -1;
        for (int i = 0; i < count; i++)
        {
            if (!used[i] && (best < 0 || weight[i] < weight[best]))
            {
                best = i;
            }
        }
        return best;
    }

    private static void ComputeDepths(int root, int[] left, int[] right, int[] depth)
    {
        // Iterative post-order/BFS depth assignment (avoids deep recursion on
        // skewed trees). depth[root] = 0; children are one deeper.
        System.Collections.Generic.Stack<int> nodes = new System.Collections.Generic.Stack<int>();
        nodes.Push(root);
        depth[root] = 0;
        while (nodes.Count > 0)
        {
            int node = nodes.Pop();
            int l = left[node];
            int r = right[node];
            if (l >= 0)
            {
                depth[l] = depth[node] + 1;
                nodes.Push(l);
            }
            if (r >= 0)
            {
                depth[r] = depth[node] + 1;
                nodes.Push(r);
            }
        }
    }

    private static int[] BuildCanonicalCodes(int[] lengths)
    {
        int n = lengths.Length;
        int[] codes = new int[n];

        int maxLen = 0;
        foreach (int l in lengths)
        {
            if (l > maxLen)
            {
                maxLen = l;
            }
        }
        if (maxLen == 0)
        {
            return codes;
        }

        int[] blCount = new int[maxLen + 1];
        foreach (int l in lengths)
        {
            if (l > 0)
            {
                blCount[l]++;
            }
        }

        int[] nextCode = new int[maxLen + 2];
        int code = 0;
        for (int bits = 1; bits <= maxLen; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        for (int s = 0; s < n; s++)
        {
            int len = lengths[s];
            if (len > 0)
            {
                codes[s] = nextCode[len];
                nextCode[len]++;
            }
        }

        return codes;
    }

    private static int CountNonZero(int[] values)
    {
        int c = 0;
        foreach (int v in values)
        {
            if (v > 0)
            {
                c++;
            }
        }
        return c;
    }

    private static int LastNonZeroIndex(int[] values)
    {
        for (int i = values.Length - 1; i >= 0; i--)
        {
            if (values[i] > 0)
            {
                return i;
            }
        }
        return -1;
    }

    private static int LengthCodeOf(int length)
    {
        int lengthCode = LengthBase.Length - 1;
        while (LengthBase[lengthCode] > length)
        {
            lengthCode--;
        }
        return lengthCode;
    }

    private static int DistanceCodeOf(int distance)
    {
        int distanceCode = DistanceBase.Length - 1;
        while (DistanceBase[distanceCode] > distance)
        {
            distanceCode--;
        }
        return distanceCode;
    }

    // ── Length and distance tables (RFC 1951 §3.2.5) ───────────────────

    private static readonly int[] LengthBase =
    [
        3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
        35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258,
    ];

    private static readonly int[] LengthExtraBits =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
        3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0,
    ];

    private static readonly int[] DistanceBase =
    [
        1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
        257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145,
        8193, 12289, 16385, 24577,
    ];

    private static readonly int[] DistanceExtraBits =
    [
        0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
        7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13,
    ];

    // Code-length alphabet order for the dynamic header (RFC 1951 §3.2.7).
    private static readonly int[] CodeLengthOrder =
    [
        16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15,
    ];

    // ── Stored fallback ─────────────────────────────────────────────

    private byte[] DeflateStored()
    {
        System.Collections.Generic.List<byte> output = new();

        int remaining = _data.Length;
        int pos = 0;

        if (remaining == 0)
        {
            output.Add(0x01);
            output.Add(0x00);
            output.Add(0x00);
            output.Add(0xFF);
            output.Add(0xFF);
            return [.. output];
        }

        while (remaining > 0)
        {
            int blockSize = Math.Min(remaining, 65535);
            bool isFinal = (remaining - blockSize) == 0;

            output.Add(isFinal ? (byte)0x01 : (byte)0x00);
            output.Add((byte)(blockSize & 0xFF));
            output.Add((byte)((blockSize >> 8) & 0xFF));
            int nlen = (~blockSize) & 0xFFFF;
            output.Add((byte)(nlen & 0xFF));
            output.Add((byte)((nlen >> 8) & 0xFF));

            for (int i = 0; i < blockSize; i++)
            {
                output.Add(_data[pos + i]);
            }

            pos += blockSize;
            remaining -= blockSize;
        }

        return [.. output];
    }

    // DEFLATE packs bits LSB-first within bytes; Huffman codes themselves
    // are written most-significant-bit first (RFC 1951 §3.1.1).
    private sealed class LsbBitWriter
    {
        private readonly System.Collections.Generic.List<byte> _bytes = new();
        private int _buffer;
        private int _bitCount;

        internal void WriteBits(int value, int count)
        {
            // value's bits, least significant first.
            _buffer |= (value & ((1 << count) - 1)) << _bitCount;
            _bitCount += count;
            FlushFullBytes();
        }

        internal void WriteBitsMsbFirst(int code, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                WriteBits((code >> i) & 1, 1);
            }
        }

        private void FlushFullBytes()
        {
            while (_bitCount >= 8)
            {
                _bytes.Add((byte)(_buffer & 0xFF));
                _buffer >>= 8;
                _bitCount -= 8;
            }
        }

        internal byte[] ToArray()
        {
            if (_bitCount > 0)
            {
                _bytes.Add((byte)(_buffer & 0xFF));
                _buffer = 0;
                _bitCount = 0;
            }
            return [.. _bytes];
        }
    }

    // ── (c) Iterated optimal ("zopfli-style") parse ────────────────────
    //
    // Produces an LZ77 token stream chosen by least-cost (shortest-path) search
    // under a Huffman cost model that is refined across a few iterations. The
    // distance of the longest match at each position is reused for all shorter
    // lengths at that position (valid: a match of length L at distance d implies
    // every prefix length l <= L also matches at d). The final token stream is
    // re-emitted through the real fixed/dynamic Huffman encoders, so output is
    // always a valid DEFLATE stream regardless of cost-model accuracy.

    private System.Collections.Generic.List<Token> TokenizeOptimal()
    {
        int n = _data.Length;
        ComputeLongestMatches(out int[] matchLen, out int[] matchDist);

        int[] litCost = new int[LitLenSymbols];
        int[] distCost = new int[DistanceSymbols];
        BuildCostModel(Tokenize(), litCost, distCost);

        int[] cost = new int[n + 1];
        int[] choiceLen = new int[n + 1];
        int[] choiceDist = new int[n + 1];

        int iterations = n < 65536 ? 10 : (n < 524288 ? 5 : 2);
        System.Collections.Generic.List<Token> tokens = Tokenize();

        for (int iter = 0; iter < iterations; iter++)
        {
            for (int i = 0; i <= n; i++)
            {
                cost[i] = int.MaxValue;
            }

            cost[0] = 0;

            for (int i = 0; i < n; i++)
            {
                if (cost[i] == int.MaxValue)
                {
                    continue;
                }

                int literal = cost[i] + litCost[_data[i]];
                if (literal < cost[i + 1])
                {
                    cost[i + 1] = literal;
                    choiceLen[i + 1] = 1;
                    choiceDist[i + 1] = 0;
                }

                int maxL = matchLen[i];
                if (maxL >= MinMatch)
                {
                    int dist = matchDist[i];
                    int distanceCost = distCost[DistanceCodeOf(dist)]
                        + DistanceExtraBits[DistanceCodeOf(dist)];
                    for (int l = MinMatch; l <= maxL; l++)
                    {
                        int lengthCode = LengthCodeOf(l);
                        int c = cost[i] + litCost[257 + lengthCode]
                            + LengthExtraBits[lengthCode] + distanceCost;
                        if (c < cost[i + l])
                        {
                            cost[i + l] = c;
                            choiceLen[i + l] = l;
                            choiceDist[i + l] = dist;
                        }
                    }
                }
            }

            tokens = Backtrack(choiceLen, choiceDist, n);
            Array.Clear(litCost, 0, litCost.Length);
            Array.Clear(distCost, 0, distCost.Length);
            BuildCostModel(tokens, litCost, distCost);
        }

        return tokens;
    }

    private System.Collections.Generic.List<Token> Backtrack(
        int[] choiceLen, int[] choiceDist, int n)
    {
        System.Collections.Generic.List<Token> reversed =
            new System.Collections.Generic.List<Token>();
        int p = n;
        while (p > 0)
        {
            int len = choiceLen[p];
            if (len <= 1)
            {
                reversed.Add(new Token(_data[p - 1]));
                p -= 1;
            }
            else
            {
                reversed.Add(new Token(len, choiceDist[p]));
                p -= len;
            }
        }

        reversed.Reverse();
        return reversed;
    }

    private void ComputeLongestMatches(out int[] matchLen, out int[] matchDist)
    {
        int length = _data.Length;
        matchLen = new int[length];
        matchDist = new int[length];

        int[] head = new int[HashSize];
        int[] prev = new int[WindowSize];
        for (int i = 0; i < HashSize; i++)
        {
            head[i] = -1;
        }

        for (int pos = 0; pos < length; pos++)
        {
            if (pos + MinMatch > length)
            {
                continue;
            }

            int hash = Hash(pos);
            int candidate = head[hash];
            int chain = MaxChainLength;
            int limit = pos - WindowSize;
            int bestLen = 0;
            int bestDist = 0;

            while (candidate >= 0 && candidate > limit && chain-- > 0)
            {
                int len = MatchLength(candidate, pos, length);
                if (len > bestLen)
                {
                    bestLen = len;
                    bestDist = pos - candidate;
                    if (bestLen >= MaxMatch)
                    {
                        break;
                    }
                }

                candidate = prev[candidate & (WindowSize - 1)];
            }

            InsertHash(head, prev, pos);
            matchLen[pos] = bestLen;
            matchDist[pos] = bestDist;
        }
    }

    private void BuildCostModel(
        System.Collections.Generic.List<Token> tokens, int[] litCost, int[] distCost)
    {
        int[] litFreq = new int[LitLenSymbols];
        int[] distFreq = new int[DistanceSymbols];

        foreach (Token token in tokens)
        {
            if (token.IsMatch)
            {
                litFreq[257 + LengthCodeOf(token.Length)]++;
                distFreq[DistanceCodeOf(token.Distance)]++;
            }
            else
            {
                litFreq[token.Literal]++;
            }
        }

        litFreq[EndOfBlock]++;

        int[] litLengths = BuildCodeLengths(litFreq, MaxCodeBits);
        int[] distLengths = BuildCodeLengths(distFreq, MaxCodeBits);
        FillCosts(litLengths, litCost, 8);
        FillCosts(distLengths, distCost, 5);
    }

    // Translates Huffman code lengths into per-symbol bit costs. Symbols with no
    // code (length 0) get a fallback slightly worse than the longest real code so
    // the search avoids them without treating them as free.
    private static void FillCosts(int[] huffLengths, int[] cost, int defaultFallback)
    {
        int maxLen = 0;
        for (int i = 0; i < huffLengths.Length; i++)
        {
            if (huffLengths[i] > maxLen)
            {
                maxLen = huffLengths[i];
            }
        }

        int fallback = maxLen > 0 ? Math.Min(maxLen + 1, MaxCodeBits) : defaultFallback;
        for (int i = 0; i < huffLengths.Length; i++)
        {
            cost[i] = huffLengths[i] > 0 ? huffLengths[i] : fallback;
        }
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
