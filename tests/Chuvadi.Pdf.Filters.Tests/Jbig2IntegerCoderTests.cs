// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) Annex A.
// PHASE: Phase 2 — item 22.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Filters.Jbig2;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

public sealed class Jbig2IntegerCoderTests
{
    // Values spanning every range boundary (0/3/4/19/20/83/84/339/340/4435/4436),
    // both signs, a large magnitude, and the out-of-band value (null).
    private static readonly int?[] Values =
    {
        0, 1, -1, 2, 3, 4, -4, 5, 19, 20, -20, 83, 84, -84, 339, 340, -340,
        4435, 4436, -4436, 100000, -100000, 7, null, 42, -42, null, 0, 4436,
    };

    [Fact]
    public void IntegerCoder_RoundTripsSharedContext()
    {
        MQEncoder encoder = new MQEncoder();
        byte[] cxEncode = new byte[ArithmeticIntegerCoder.IntegerContextSize];
        foreach (int? value in Values)
        {
            ArithmeticIntegerCoder.Encode(encoder, cxEncode, value);
        }

        byte[] coded = encoder.Flush();

        MQDecoder decoder = new MQDecoder(coded, 0, coded.Length);
        byte[] cxDecode = new byte[ArithmeticIntegerCoder.IntegerContextSize];
        foreach (int? value in Values)
        {
            ArithmeticIntegerCoder.Decode(decoder, cxDecode).Should().Be(value);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(11)]
    public void IdCoder_RoundTrips(int symCodeLen)
    {
        Random rng = new Random(symCodeLen * 31);
        int count = 500;
        int max = 1 << symCodeLen;
        List<int> ids = new List<int>(count);
        for (int i = 0; i < count; i++) { ids.Add(rng.Next(max)); }

        MQEncoder encoder = new MQEncoder();
        byte[] cxEncode = new byte[1 << (symCodeLen + 1)];
        foreach (int id in ids)
        {
            ArithmeticIntegerCoder.EncodeId(encoder, cxEncode, symCodeLen, id);
        }

        byte[] coded = encoder.Flush();

        MQDecoder decoder = new MQDecoder(coded, 0, coded.Length);
        byte[] cxDecode = new byte[1 << (symCodeLen + 1)];
        foreach (int id in ids)
        {
            ArithmeticIntegerCoder.DecodeId(decoder, cxDecode, symCodeLen).Should().Be(id);
        }
    }

    [Fact]
    public void IntegerCoder_DistinguishesZeroFromOob()
    {
        MQEncoder encoder = new MQEncoder();
        byte[] cx = new byte[ArithmeticIntegerCoder.IntegerContextSize];
        ArithmeticIntegerCoder.Encode(encoder, cx, 0);
        ArithmeticIntegerCoder.Encode(encoder, cx, null);
        byte[] coded = encoder.Flush();

        MQDecoder decoder = new MQDecoder(coded, 0, coded.Length);
        byte[] cxDecode = new byte[ArithmeticIntegerCoder.IntegerContextSize];
        ArithmeticIntegerCoder.Decode(decoder, cxDecode).Should().Be(0);
        ArithmeticIntegerCoder.Decode(decoder, cxDecode).Should().BeNull();
    }
}
