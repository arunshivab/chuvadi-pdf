// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) Annex A — arithmetic integer decoding (A.2) and
//        the IAID symbol-ID decoding procedure (A.3).
// PHASE: Phase 2 — item 22, JBIG2 decode (symbol dictionary + text region).
//
// Each logical integer field in a symbol dictionary or text region (IADH, IADW,
// IAEX, IAAI, IADT, IAFS, IADS, IAIT, IARI, IARDW, IARDH, IARDX, IARDY) is decoded
// by the same procedure against its own 512-entry context array. IAID decodes a
// symbol identifier of a fixed bit length. Matching encoders make the coders
// round-trippable for validation and for the future encoder (item 23).

namespace Chuvadi.Pdf.Filters.Jbig2;

/// <summary>
/// The arithmetic integer and symbol-ID coders of ITU-T T.88 Annex A. An integer
/// value may be "out of band" (OOB), represented here as a null result.
/// </summary>
internal static class ArithmeticIntegerCoder
{
    /// <summary>Context-array size for an integer field (A.2).</summary>
    internal const int IntegerContextSize = 512;

    // Range prefixes: (number of value bits, value offset) selected by a unary-ish
    // prefix of 0..5 one-bits (T.88 A.2).
    private static readonly int[] RangeBits = { 2, 4, 6, 8, 12, 32 };
    private static readonly int[] RangeOffset = { 0, 4, 20, 84, 340, 4436 };

    /// <summary>
    /// Decodes one integer field (T.88 A.2) using the given context array.
    /// </summary>
    /// <param name="mq">The arithmetic decoder.</param>
    /// <param name="cx">A 512-entry context array for this field.</param>
    /// <returns>The decoded value, or null for the out-of-band value.</returns>
    internal static int? Decode(MQDecoder mq, byte[] cx)
    {
        int prev = 1;

        int Bit()
        {
            int d = mq.Decode(cx, prev);
            prev = prev < 256 ? (prev << 1) | d : ((((prev << 1) | d) & 511) | 256);
            return d;
        }

        int sign = Bit();

        int rangeIndex = 0;
        while (rangeIndex < RangeBits.Length - 1 && Bit() == 1)
        {
            rangeIndex++;
        }

        int value = 0;
        for (int i = 0; i < RangeBits[rangeIndex]; i++)
        {
            value = (value << 1) | Bit();
        }

        value += RangeOffset[rangeIndex];

        if (sign == 0)
        {
            return value;
        }

        if (value > 0)
        {
            return -value;
        }

        return null; // sign set with zero magnitude → OOB.
    }

    /// <summary>
    /// Encodes one integer field — the inverse of <see cref="Decode"/>.
    /// </summary>
    /// <param name="mq">The arithmetic encoder.</param>
    /// <param name="cx">A 512-entry context array for this field.</param>
    /// <param name="value">The value to encode, or null for out-of-band.</param>
    internal static void Encode(MQEncoder mq, byte[] cx, int? value)
    {
        int prev = 1;

        void Bit(int d)
        {
            mq.Encode(cx, prev, d);
            prev = prev < 256 ? (prev << 1) | d : ((((prev << 1) | d) & 511) | 256);
        }

        int sign;
        int magnitude;
        if (value is null)
        {
            sign = 1;
            magnitude = 0;
        }
        else
        {
            sign = value.Value < 0 ? 1 : 0;
            magnitude = value.Value < 0 ? -value.Value : value.Value;
        }

        Bit(sign);

        int rangeIndex = 0;
        while (rangeIndex < RangeBits.Length - 1
            && magnitude - RangeOffset[rangeIndex] >= (1 << RangeBits[rangeIndex]))
        {
            rangeIndex++;
        }

        for (int i = 0; i < rangeIndex; i++)
        {
            Bit(1);
        }

        if (rangeIndex < RangeBits.Length - 1)
        {
            Bit(0);
        }

        int bits = RangeBits[rangeIndex];
        int adjusted = magnitude - RangeOffset[rangeIndex];
        for (int i = bits - 1; i >= 0; i--)
        {
            Bit((adjusted >> i) & 1);
        }
    }

    /// <summary>
    /// Decodes a symbol identifier of <paramref name="symCodeLen"/> bits (T.88 A.3).
    /// </summary>
    /// <param name="mq">The arithmetic decoder.</param>
    /// <param name="cx">A context array of at least 2^(symCodeLen+1) entries.</param>
    /// <param name="symCodeLen">The symbol-ID code length in bits.</param>
    /// <returns>The decoded symbol identifier.</returns>
    internal static int DecodeId(MQDecoder mq, byte[] cx, int symCodeLen)
    {
        int prev = 1;
        for (int i = 0; i < symCodeLen; i++)
        {
            int d = mq.Decode(cx, prev);
            prev = (prev << 1) | d;
        }

        return prev - (1 << symCodeLen);
    }

    /// <summary>
    /// Encodes a symbol identifier — the inverse of <see cref="DecodeId"/>.
    /// </summary>
    /// <param name="mq">The arithmetic encoder.</param>
    /// <param name="cx">A context array of at least 2^(symCodeLen+1) entries.</param>
    /// <param name="symCodeLen">The symbol-ID code length in bits.</param>
    /// <param name="id">The symbol identifier to encode.</param>
    internal static void EncodeId(MQEncoder mq, byte[] cx, int symCodeLen, int id)
    {
        int prev = 1;
        for (int i = symCodeLen - 1; i >= 0; i--)
        {
            int d = (id >> i) & 1;
            mq.Encode(cx, prev, d);
            prev = (prev << 1) | d;
        }
    }
}
