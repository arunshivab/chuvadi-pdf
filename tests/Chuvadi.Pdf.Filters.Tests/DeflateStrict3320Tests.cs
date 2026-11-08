// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 1951 §3.2.2 (canonical Huffman codes), §3.2.7 (dynamic blocks)
// PHASE: 3.32.0 (D-271) — the inflater rejects an over-subscribed Huffman
//        code set, as zlib does. Before, such a stream decoded silently to
//        the wrong bytes.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

public sealed class DeflateStrict3320Tests
{
    private static readonly int[] ThreeOneBitCodes = { 1, 1, 1 };

    private static readonly int[] OverAtDepthThree = { 1, 2, 3, 3, 3 };

    private static readonly int[] Complete = { 1, 2, 3, 3 };

    private static readonly int[] Incomplete = { 1, 3 };

    private static readonly int[] SingleCode = { 0, 1 };

    [Fact]
    public void OverSubscribedLengths_AreRejected()
    {
        Action three = () => HuffmanTree.Build(ThreeOneBitCodes);
        Action deep = () => HuffmanTree.Build(OverAtDepthThree);

        three.Should().Throw<FilterException>().WithMessage("*over-subscribed*");
        deep.Should().Throw<FilterException>().WithMessage("*over-subscribed*");
    }

    [Fact]
    public void CompleteAndIncompleteLengths_AreStillAccepted()
    {
        Action complete = () => HuffmanTree.Build(Complete);
        Action incomplete = () => HuffmanTree.Build(Incomplete);
        Action single = () => HuffmanTree.Build(SingleCode);

        complete.Should().NotThrow();
        incomplete.Should().NotThrow();
        single.Should().NotThrow();
    }

    [Fact]
    public void StreamWithOverSubscribedCodeLengthCode_IsRejected_LikeZlib()
    {
        byte[] stream = OverSubscribedStream();

        Action zlib = () =>
        {
            using ZLibStream z = new ZLibStream(new MemoryStream(stream), CompressionMode.Decompress);
            z.CopyTo(new MemoryStream());
        };
        Action ours = () => new DeflateFilter().Decode(new MemoryStream(stream), new MemoryStream());

        zlib.Should().Throw<InvalidDataException>("zlib rejects the code set");
        ours.Should().Throw<FilterException>().WithMessage("*over-subscribed*damaged*");
    }

    [Fact]
    public void ValidStreams_StillRoundTrip()
    {
        Random random = new Random(5);
        byte[] data = new byte[200_000];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(random.Next(4) == 0 ? random.Next(256) : i % 7);
        }

        using MemoryStream zlibOut = new MemoryStream();
        using (ZLibStream z = new ZLibStream(zlibOut, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            z.Write(data, 0, data.Length);
        }

        using MemoryStream decoded = new MemoryStream();
        new DeflateFilter().Decode(new MemoryStream(zlibOut.ToArray()), decoded);

        decoded.ToArray().Should().Equal(data);
    }

    // A zlib stream whose one dynamic block gives four code-length codes all
    // of length 1: more one-bit codes than a prefix code can hold.
    internal static byte[] OverSubscribedStream()
    {
        ulong bits = 0;
        int count = 0;
        void Write(int value, int width)
        {
            bits |= (ulong)value << count;
            count += width;
        }

        Write(1, 1); // BFINAL
        Write(2, 2); // BTYPE = 10, dynamic Huffman
        Write(0, 5); // HLIT: 257 literal/length codes
        Write(0, 5); // HDIST: 1 distance code
        Write(0, 4); // HCLEN: 4 code-length codes
        for (int i = 0; i < 4; i++)
        {
            Write(1, 3);
        }

        List<byte> stream = new List<byte> { 0x78, 0x9C };
        while (count > 0)
        {
            stream.Add((byte)(bits & 0xFF));
            bits >>= 8;
            count -= 8;
        }

        stream.AddRange(new byte[8]);
        return stream.ToArray();
    }
}
