// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) Annex E — MQ arithmetic coder.
// PHASE: Phase 2 — items 22/23.
//
// These tests prove the encoder and decoder are exact inverses across adaptive
// contexts (internal consistency). Bit-exact conformance against an independently
// produced JBIG2 stream is validated separately, once a reference file is in the
// Fixtures set — mirroring the CCITT approach.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Filters.Jbig2;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

public sealed class MQCoderTests
{
    [Theory]
    [InlineData(1, 2000, 1)]
    [InlineData(16, 5000, 7)]
    [InlineData(256, 20000, 123)]
    [InlineData(512, 50000, 4242)]
    public void RoundTrip_RecoversEveryBit(int contextCount, int bitCount, int seed)
    {
        Random rng = new Random(seed);
        int[] bits = new int[bitCount];
        int[] contexts = new int[bitCount];

        // Skew each decision toward 0 so contexts adapt strongly toward an MPS,
        // exercising the NMPS path, renormalisation, and the conditional exchange.
        for (int i = 0; i < bitCount; i++)
        {
            contexts[i] = rng.Next(contextCount);
            bits[i] = rng.NextDouble() < 0.88 ? 0 : 1;
        }

        byte[] coded = Encode(bits, contexts, contextCount);

        MQDecoder decoder = new MQDecoder(coded, 0, coded.Length);
        byte[] cx = new byte[contextCount];
        for (int i = 0; i < bitCount; i++)
        {
            decoder.Decode(cx, contexts[i]).Should().Be(bits[i], "bit {0} must round-trip", i);
        }
    }

    [Fact]
    public void RoundTrip_AllOnesSingleContext_Recovers()
    {
        int[] bits = new int[1024];
        int[] contexts = new int[1024];
        for (int i = 0; i < bits.Length; i++) { bits[i] = 1; }

        byte[] coded = Encode(bits, contexts, 1);

        MQDecoder decoder = new MQDecoder(coded, 0, coded.Length);
        byte[] cx = new byte[1];
        for (int i = 0; i < bits.Length; i++)
        {
            decoder.Decode(cx, 0).Should().Be(1);
        }
    }

    [Fact]
    public void RoundTrip_AlternatingBits_Recovers()
    {
        int[] bits = new int[2048];
        int[] contexts = new int[2048];
        for (int i = 0; i < bits.Length; i++) { bits[i] = i & 1; }

        byte[] coded = Encode(bits, contexts, 4);
        coded.Length.Should().BeGreaterThan(0);

        MQDecoder decoder = new MQDecoder(coded, 0, coded.Length);
        byte[] cx = new byte[4];
        List<int> recovered = new List<int>(bits.Length);
        for (int i = 0; i < bits.Length; i++)
        {
            recovered.Add(decoder.Decode(cx, contexts[i]));
        }

        recovered.Should().Equal(bits);
    }

    private static byte[] Encode(int[] bits, int[] contexts, int contextCount)
    {
        MQEncoder encoder = new MQEncoder();
        byte[] cx = new byte[contextCount];
        for (int i = 0; i < bits.Length; i++)
        {
            encoder.Encode(cx, contexts[i], bits[i]);
        }

        return encoder.Flush();
    }
}
