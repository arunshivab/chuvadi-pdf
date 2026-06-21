// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.4 — FlateDecode, RFC 1950, RFC 1951
// PHASE: Phase 2 — item 8, max-level lossless re-deflate
//
// Maximum-effort FlateDecode widens the candidate set (BCL deflater + iterated
// optimal "zopfli-style" parse) and keeps the smallest result. These tests pin
// the two invariants that matter: every candidate decodes back to the exact
// input (including via the BCL's own zlib reader, i.e. valid zlib output), and
// maximum effort is never larger than the default fast path.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

public sealed class MaxDeflateTests
{
    private static readonly DeflateFilter Default = new(DeflateEffort.Default);
    private static readonly DeflateFilter Maximum = new(DeflateEffort.Maximum);

    private static byte[] Encode(DeflateFilter filter, byte[] data)
    {
        using MemoryStream output = new MemoryStream();
        filter.Encode(new MemoryStream(data), output);
        return output.ToArray();
    }

    private static byte[] Decode(byte[] zlib)
    {
        using MemoryStream output = new MemoryStream();
        new DeflateFilter().Decode(new MemoryStream(zlib), output);
        return output.ToArray();
    }

    private static byte[] ZlibDecode(byte[] zlib)
    {
        using MemoryStream input = new MemoryStream(zlib);
        using MemoryStream output = new MemoryStream();
        using ZLibStream stream = new ZLibStream(input, CompressionMode.Decompress);
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] RepeatText(string unit, int times)
        => Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(unit, times)));

    private static byte[] PseudoRandom(int length, int seed)
    {
        Random random = new Random(seed);
        byte[] data = new byte[length];
        random.NextBytes(data);
        return data;
    }

    private static byte[] MixedContentStream()
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < 500; i++)
        {
            builder.Append("BT /F1 12 Tf 1 0 0 1 ").Append(i % 600).Append(' ')
                .Append((i * 7) % 800).Append(" Tm (Sample text line ").Append(i)
                .Append(") Tj ET\n0.2 0.4 0.6 rg ").Append(i % 200).Append(' ')
                .Append(i % 100).Append(" 120 18 re f\n");
        }

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    public static TheoryData<string> Payloads => new TheoryData<string>
    {
        "empty",
        "tiny",
        "repetitive",
        "mixed",
        "random",
    };

    private static byte[] PayloadFor(string name) => name switch
    {
        "empty" => Array.Empty<byte>(),
        "tiny" => Encoding.ASCII.GetBytes("X"),
        "repetitive" => RepeatText("The quick brown fox jumps over the lazy dog. ", 400),
        "mixed" => MixedContentStream(),
        "random" => PseudoRandom(4096, 99),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public void MaximumEffort_RoundTripsExactly(string payloadName)
    {
        byte[] data = PayloadFor(payloadName);

        byte[] encoded = Encode(Maximum, data);

        Decode(encoded).Should().Equal(data);
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public void MaximumEffort_ProducesValidZlib(string payloadName)
    {
        byte[] data = PayloadFor(payloadName);

        byte[] encoded = Encode(Maximum, data);

        // The BCL's own zlib reader must accept our maximum-effort output.
        ZlibDecode(encoded).Should().Equal(data);
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public void MaximumEffort_NeverLargerThanDefault(string payloadName)
    {
        byte[] data = PayloadFor(payloadName);

        byte[] maximum = Encode(Maximum, data);
        byte[] standard = Encode(Default, data);

        maximum.Length.Should().BeLessThanOrEqualTo(standard.Length);
    }

    [Fact]
    public void MaximumEffort_BeatsDefault_OnMixedText()
    {
        byte[] data = MixedContentStream();

        byte[] maximum = Encode(Maximum, data);
        byte[] standard = Encode(Default, data);

        maximum.Length.Should().BeLessThan(standard.Length,
            "the optimal parse should improve on the greedy default for realistic streams");
    }
}
