// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.32.0 — Huffman code lengths are limited to 15 bits correctly.
//        Before, large skewed inputs (about 9 MB and up) could get an
//        over-subscribed code set: zlib rejected the stream and Chuvadi's own
//        inflater silently returned different bytes.

using System;
using System.IO;
using System.IO.Compression;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

public sealed class DeflateCodeLengths3320Tests
{
    [Fact]
    public void FibonacciCounts_NeedMoreThan15Bits_AndAreLimitedToAValidCode()
    {
        // Fibonacci counts give a Huffman tree as deep as there are symbols.
        int[] freqs = new int[30];
        int a = 1;
        int b = 1;
        for (int i = 0; i < freqs.Length; i++)
        {
            freqs[i] = a;
            (a, b) = (b, a + b);
        }

        int[] lengths = DeflateDeflater.BuildCodeLengths(freqs, 15);

        lengths.Should().OnlyContain(l => l >= 1 && l <= 15);
        KraftSum(lengths).Should().Be(1.0, "a complete prefix code, neither over- nor under-subscribed");
    }

    [Fact]
    public void ShallowTrees_AreUnchanged()
    {
        int[] freqs = { 5, 9, 12, 13, 16, 45 };

        int[] lengths = DeflateDeflater.BuildCodeLengths(freqs, 15);

        lengths.Should().Equal(4, 4, 3, 3, 3, 1);
    }

    [Fact]
    public void LargeSkewedInput_RoundTrips_ThroughZlibAndOurOwnInflater()
    {
        // The data the smallest failing input (9,020,867 bytes) was cut from:
        // Sub-filtered RGB PNG rows of a 4967-pixel-wide grey image, bands of
        // white alternating with noise. The first 9.1 MB.
        byte[] data = SubFilteredRows(9_100_000);

        DeflateFilter filter = new DeflateFilter();
        using MemoryStream encoded = new MemoryStream();
        filter.Encode(new MemoryStream(data), encoded);
        byte[] bytes = encoded.ToArray();

        using (ZLibStream z = new ZLibStream(new MemoryStream(bytes), CompressionMode.Decompress))
        using (MemoryStream inflated = new MemoryStream())
        {
            z.CopyTo(inflated);
            inflated.ToArray().Should().Equal(data, "zlib must accept and reproduce the stream");
        }

        using MemoryStream ours = new MemoryStream();
        filter.Decode(new MemoryStream(bytes), ours);
        ours.ToArray().Should().Equal(data);
    }

    private static byte[] SubFilteredRows(int length)
    {
        const int Width = 4967;
        const int Channels = 3;
        Random random = new Random(1);
        byte[] data = new byte[length];
        byte[] row = new byte[Width * Channels];
        int o = 0;
        for (int y = 0; o < length; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                byte v = (((x / 7) + (y / 5)) % 2) == 0 ? (byte)255 : (byte)random.Next(256);
                row[x * Channels] = v;
                row[(x * Channels) + 1] = v;
                row[(x * Channels) + 2] = v;
            }

            if (o < length)
            {
                data[o++] = 1;
            }

            for (int i = 0; i < row.Length && o < length; i++)
            {
                data[o++] = (byte)(row[i] - (i >= Channels ? row[i - Channels] : 0));
            }
        }

        return data;
    }

    private static double KraftSum(int[] lengths)
    {
        double sum = 0;
        foreach (int l in lengths)
        {
            if (l > 0)
            {
                sum += Math.Pow(2, -l);
            }
        }

        return sum;
    }
}
