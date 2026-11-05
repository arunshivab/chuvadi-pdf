// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 — Brotli Compressed Data Format
// PHASE: Phase 2.2 stage 4 — LZ77 multi-command encoder

using System;
using System.IO;

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// Pure-C# Brotli encoder.
/// </summary>
/// <remarks>
/// <para>
/// Produces valid Brotli streams using LZ77-based compressed meta-blocks. For each
/// call, the encoder runs the LZ77 matcher in <see cref="BrotliCommandStream"/> to
/// produce an insert-and-copy command stream, then <see cref="BrotliCompressedEmitter"/>
/// emits one or more compressed meta-blocks with per-block Huffman trees over the
/// literal, insert-and-copy, and distance alphabets.
/// </para>
/// <para>
/// The encoder also speculatively emits a stored-meta-block variant and returns
/// whichever is smaller. This avoids size regressions on inputs where the
/// compression overhead (prefix-code declarations, frequency overhead for tiny
/// alphabets) exceeds the savings — typically very short or highly-uniform inputs.
/// </para>
/// <para>
/// Output is validated to round-trip through any conformant Brotli decoder including
/// <see cref="System.IO.Compression.BrotliStream"/>.
/// </para>
/// </remarks>
[System.Obsolete("Deprecated: no longer used by Chuvadi; for the LiPi Sans faces use Chuvadi.Fonts (FontPack). Removed at the Pdf move.")]
public static class BrotliEncoder
{
    // RFC §9.2: with MNIBBLES=4, MLEN-1 fits in 16 bits → max 65536 bytes per stored block.
    private const int MaxStoredMetaBlockLen = 1 << 16;

    /// <summary>Encodes <paramref name="data"/> as a valid Brotli stream.</summary>
    public static byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using MemoryStream output = new();
        BrotliBitWriter bw = new(output);

        WriteStreamHeader(bw);

        if (data.Length == 0)
        {
            WriteTrailingEmpty(bw);
            bw.Flush();
            return output.ToArray();
        }

        // Speculatively produce both the compressed and stored variants and pick the
        // smaller one. Both are valid Brotli streams; the smaller wins.
        byte[] compressed = EmitCompressed(data);
        byte[] stored = EmitStored(data);
        return compressed.Length < stored.Length ? compressed : stored;
    }

    private static byte[] EmitCompressed(byte[] data)
    {
        using MemoryStream scratch = new();
        BrotliBitWriter bw = new(scratch);
        WriteStreamHeader(bw);
        BrotliCompressedEmitter.Emit(bw, data);   // emits ISLAST=1 on final meta-block
        bw.Flush();
        return scratch.ToArray();
    }

    private static byte[] EmitStored(byte[] data)
    {
        using MemoryStream scratch = new();
        BrotliBitWriter bw = new(scratch);
        WriteStreamHeader(bw);

        int offset = 0;
        while (offset < data.Length)
        {
            int blockLen = Math.Min(MaxStoredMetaBlockLen, data.Length - offset);
            WriteStoredMetaBlock(bw, scratch, data, offset, blockLen);
            offset += blockLen;
        }
        WriteTrailingEmpty(bw);
        bw.Flush();
        return scratch.ToArray();
    }

    private static void WriteStreamHeader(BrotliBitWriter bw)
    {
        // WBITS = 22. Encoding (RFC §9.1):
        //   first bit 1, then 3 bits N LSB-first.
        //   For WBITS in 18..24: N = WBITS - 17.
        //   N = 22 - 17 = 5 = 0b101.
        bw.WriteBits(1, 1);          // first bit
        bw.WriteBits(5, 3);          // N = 5 → WBITS = 22
    }

    private static void WriteStoredMetaBlock(
        BrotliBitWriter bw, MemoryStream output,
        byte[] data, int offset, int length)
    {
        // Meta-block header (RFC §9.2)
        bw.WriteBits(0, 1);          // ISLAST = 0
        bw.WriteBits(0, 2);          // MNIBBLES = 0 → 4 nibbles
        bw.WriteBits((ulong)(length - 1), 16);   // MLEN - 1 (16 bits)
        bw.WriteBits(1, 1);          // ISUNCOMPRESSED = 1

        // Byte-align before raw data
        bw.Flush();

        // Raw bytes
        output.Write(data, offset, length);
    }

    private static void WriteTrailingEmpty(BrotliBitWriter bw)
    {
        // Trailing empty meta-block: ISLAST=1, ISEMPTY=1, pad to byte boundary
        bw.WriteBits(1, 1);          // ISLAST = 1
        bw.WriteBits(1, 1);          // ISEMPTY = 1
    }
}
