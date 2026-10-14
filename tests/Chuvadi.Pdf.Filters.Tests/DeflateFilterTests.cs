// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.4 — FlateDecode, RFC 1950, RFC 1951
// PHASE: Phase 1 — Chuvadi.Pdf.Filters tests

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

/// <summary>
/// Tests for DeflateFilter (FlateDecode).
/// Uses .NET's built-in ZLibStream as the reference implementation
/// to generate valid zlib-compressed test vectors.
/// </summary>
public sealed class DeflateFilterTests
{
    private static readonly DeflateFilter Filter = new();
    private static readonly FilterPipeline Pipeline = new();

    // ── Helper: compress with .NET ZLibStream (reference implementation) ──

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    // ── FilterName ────────────────────────────────────────────────────────

    [Fact]
    public void FilterName_IsFlateDecode()
    {
        Filter.FilterName.Should().Be("FlateDecode");
    }

    // ── Basic round-trip ──────────────────────────────────────────────────

    [Fact]
    public void Decode_SimpleText_RoundTrips()
    {
        byte[] original = Encoding.UTF8.GetBytes("Hello, Chuvadi!");
        byte[] compressed = ZlibCompress(original);

        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_EmptyInput_ProducesEmpty()
    {
        byte[] original = [];
        byte[] compressed = ZlibCompress(original);

        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);

        decoded.Should().BeEmpty();
    }

    [Fact]
    public void Decode_SingleByte_RoundTrips()
    {
        byte[] original = [0x42];
        byte[] compressed = ZlibCompress(original);

        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_AllByteValues_RoundTrips()
    {
        byte[] original = new byte[256];

        for (int i = 0; i < 256; i++)
        {
            original[i] = (byte)i;
        }

        byte[] compressed = ZlibCompress(original);
        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_LargeRepetitiveData_RoundTrips()
    {
        // Repetitive data compresses well and exercises back-references.
        byte[] original = new byte[10000];

        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i % 17);
        }

        byte[] compressed = ZlibCompress(original);
        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_LargeRandomLikeData_RoundTrips()
    {
        // Pseudo-random data exercises stored blocks or minimal compression.
        byte[] original = new byte[8192];

        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)((i * 13 + 7) & 0xFF);
        }

        byte[] compressed = ZlibCompress(original);
        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);

        decoded.Should().Equal(original);
    }

    // ── Encode + Decode round-trip ────────────────────────────────────────

    [Fact]
    public void Encode_ThenDecode_RoundTrips()
    {
        byte[] original = Encoding.UTF8.GetBytes("Chuvadi PDF Library — Phase 1");
        byte[] encoded = Pipeline.Encode("FlateDecode", original);
        byte[] decoded = Pipeline.Decode("FlateDecode", encoded);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_ProducesValidZlib()
    {
        byte[] original = Encoding.UTF8.GetBytes("test data");
        byte[] encoded = Pipeline.Encode("FlateDecode", original);

        // Verify .NET's ZLibStream can also decode our output.
        using var input = new MemoryStream(encoded);
        using var output = new MemoryStream();
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        zlib.CopyTo(output);
        byte[] decoded = output.ToArray();

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_EmptyData_RoundTrips()
    {
        byte[] original = [];
        byte[] encoded = Pipeline.Encode("FlateDecode", original);
        byte[] decoded = Pipeline.Decode("FlateDecode", encoded);

        decoded.Should().BeEmpty();
    }

    [Fact]
    public void Encode_LargeData_RoundTrips()
    {
        byte[] original = new byte[100_000];

        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i & 0xFF);
        }

        byte[] encoded = Pipeline.Encode("FlateDecode", original);
        byte[] decoded = Pipeline.Decode("FlateDecode", encoded);

        decoded.Should().Equal(original);
    }

    // ── Error handling ────────────────────────────────────────────────────

    [Fact]
    public void Decode_NullInput_Throws()
    {
        Action act = () => Filter.Decode(null!, new MemoryStream());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Decode_NullOutput_Throws()
    {
        Action act = () => Filter.Decode(new MemoryStream(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Encode_NullInput_Throws()
    {
        Action act = () => Filter.Encode(null!, new MemoryStream());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Encode_NullOutput_Throws()
    {
        Action act = () => Filter.Encode(new MemoryStream(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Decode_TooShort_ThrowsFilterException()
    {
        byte[] tooShort = [0x78];
        Action act = () => Pipeline.Decode("FlateDecode", tooShort);
        act.Should().Throw<FilterException>();
    }

    [Fact]
    public void Decode_InvalidZlibHeader_ThrowsFilterException()
    {
        // Compression method != 8 — invalid.
        byte[] badHeader = [0x79, 0x9C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01];
        Action act = () => Pipeline.Decode("FlateDecode", badHeader);
        act.Should().Throw<FilterException>();
    }

    [Fact]
    public void Decode_CorruptedPayload_ThrowsFilterException()
    {
        // 0x07 = BFINAL=1, BTYPE=3 (reserved) — structurally invalid DEFLATE.
        // Structural corruption (invalid block types, broken Huffman codes,
        // out-of-range back-references, truncation) still throws; only the
        // advisory Adler-32 trailer is lenient (see the PDF-12 tests below).
        byte[] invalidBlockType = [0x78, 0x9C, 0x07];

        Action act = () => Pipeline.Decode("FlateDecode", invalidBlockType);
        act.Should().Throw<FilterException>();
    }

    [Fact]
    public void Decode_TruncatedPayload_ThrowsFilterException()
    {
        // Cutting the stream inside the DEFLATE payload is a structural
        // error (unexpected end of stream), not a trailer problem.
        byte[] original = Encoding.UTF8.GetBytes("Hello World truncated");
        byte[] compressed = ZlibCompress(original);

        byte[] truncated = new byte[6];
        Array.Copy(compressed, truncated, truncated.Length);

        Action act = () => Pipeline.Decode("FlateDecode", truncated);
        act.Should().Throw<FilterException>();
    }

    // ── FilterPipeline ────────────────────────────────────────────────────

    [Fact]
    public void Pipeline_UnknownFilter_ThrowsFilterException()
    {
        Action act = () => Pipeline.Decode("UnknownFilter", []);
        act.Should().Throw<FilterException>();
    }

    [Fact]
    public void Pipeline_IsRegistered_TrueForFlateDecode()
    {
        Pipeline.IsRegistered("FlateDecode").Should().BeTrue();
    }

    [Fact]
    public void Pipeline_IsRegistered_FalseForUnknown()
    {
        Pipeline.IsRegistered("NoSuchFilter").Should().BeFalse();
    }

    [Fact]
    public void Pipeline_DecodeChain_SingleFilter_Works()
    {
        byte[] original = Encoding.UTF8.GetBytes("chain test");
        byte[] compressed = ZlibCompress(original);

        byte[] result = Pipeline.DecodeChain(["FlateDecode"], compressed);

        result.Should().Equal(original);
    }

    // ── FilterParameters ──────────────────────────────────────────────────

    [Fact]
    public void FilterParameters_Defaults_AreCorrect()
    {
        var parms = new FilterParameters();
        parms.Predictor.Should().Be(1);
        parms.Colors.Should().Be(1);
        parms.BitsPerComponent.Should().Be(8);
        parms.Columns.Should().Be(1);
        parms.EarlyChange.Should().Be(1);
    }

    // ── PNG predictor reversal ────────────────────────────────────────────

    [Fact]
    public void Decode_WithPngPredictor_Sub_Reverses()
    {
        // Build a row using Sub filter type (1).
        // Row: [1, 10, 1, 1] -> Sub filter applies deltas.
        // Filtered row: filter_type=1, then deltas from left.
        // Original: 10, 11, 12, 13 (each +1 from previous)
        // Sub-filtered: 10, 1, 1, 1
        byte[] filtered =
        [
            1,   // filter type = Sub
            10, 1, 1, 1,  // sub-filtered values
        ];

        var parms = new FilterParameters
        {
            Predictor = 12, // Sub
            Colors = 1,
            BitsPerComponent = 8,
            Columns = 4
        };

        // Compress the filtered data so Decode can process it.
        byte[] compressed = ZlibCompress(filtered);
        byte[] decoded = Pipeline.Decode("FlateDecode", compressed, parms);

        decoded.Should().Equal([10, 11, 12, 13]);
    }

    [Fact]
    public void Decode_WithPngPredictor_Up_Reverses()
    {
        // Two rows. Row 2 uses Up filter (delta from row above).
        // Row 1 original: 1, 2, 3
        // Row 2 original: 4, 6, 8
        // Row 1 filtered: [2, 1, 2, 3] (filter_type=2, no prev row so same)
        // Row 2 filtered: [2, 3, 4, 5] (filter_type=2, delta from row 1)
        byte[] filtered =
        [
            2, 1, 2, 3,
            2, 3, 4, 5,
        ];

        var parms = new FilterParameters
        {
            Predictor = 12,
            Colors = 1,
            BitsPerComponent = 8,
            Columns = 3
        };

        byte[] compressed = ZlibCompress(filtered);
        byte[] decoded = Pipeline.Decode("FlateDecode", compressed, parms);

        decoded.Should().Equal([1, 2, 3, 4, 6, 8]);
    }

    // ── Adler-32 trailer leniency (PDF-12) ────────────────────────────────
    // The DEFLATE payload ends where the final block says it ends; whatever
    // follows (a correct trailer, a corrupt one, a truncated one, or an EOL
    // before "endstream") never fails a structurally sound decode. Structural
    // corruption inside the payload still throws (see
    // Decode_CorruptedPayload_ThrowsFilterException above).

    [Fact]
    public void Adler32_ValidChecksum_DecodesSuccessfully()
    {
        // A valid zlib stream (correct checksum) decodes without exception.
        byte[] original = Encoding.UTF8.GetBytes("Mark Adler");
        byte[] compressed = ZlibCompress(original);
        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_CorruptChecksumTrailer_IsLenientAndDecodes()
    {
        // The data inflates with a consistent structure; a wrong trailer is a
        // producer bug every mainstream viewer tolerates, so the decode
        // succeeds with the correct content.
        byte[] original = Encoding.UTF8.GetBytes("checksum test");
        byte[] compressed = ZlibCompress(original);

        // The last 4 bytes are the Adler-32 checksum. Corrupt one byte.
        compressed[compressed.Length - 1] ^= 0xFF;

        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_TrailingNewlineAfterTrailer_Decodes()
    {
        // PDF-12 regression: Elsevier journals carry 0x0A after the zlib
        // data. A buffer-anchored trailer read misparsed the checksum one
        // byte late ("Expected 0x7B25270A, got 0xD17B2527") and failed every
        // page after the first.
        byte[] original = Encoding.UTF8.GetBytes("Elsevier page content");
        byte[] compressed = ZlibCompress(original);

        byte[] withNewline = new byte[compressed.Length + 1];
        Array.Copy(compressed, withNewline, compressed.Length);
        withNewline[compressed.Length] = 0x0A;

        byte[] decoded = Pipeline.Decode("FlateDecode", withNewline);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_TrailingCrLfAndJunkAfterTrailer_Decodes()
    {
        // Any amount of trailing junk after the final DEFLATE block is
        // ignored (CRLF, repeated EOLs, stray delimiter bytes).
        byte[] original = Encoding.UTF8.GetBytes("trailing junk tolerance");
        byte[] compressed = ZlibCompress(original);

        byte[] junk = [0x0D, 0x0A, 0x0D, 0x0A, 0x3E, 0x3E];
        byte[] withJunk = new byte[compressed.Length + junk.Length];
        Array.Copy(compressed, withJunk, compressed.Length);
        Array.Copy(junk, 0, withJunk, compressed.Length, junk.Length);

        byte[] decoded = Pipeline.Decode("FlateDecode", withJunk);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_TruncatedChecksumTrailer_Decodes()
    {
        // A stream cut inside the 4-byte trailer still holds the complete
        // DEFLATE payload; the decode succeeds with the full content.
        byte[] original = Encoding.UTF8.GetBytes("truncated trailer");
        byte[] compressed = ZlibCompress(original);

        byte[] truncated = new byte[compressed.Length - 2];
        Array.Copy(compressed, truncated, truncated.Length);

        byte[] decoded = Pipeline.Decode("FlateDecode", truncated);
        decoded.Should().Equal(original);
    }
}
