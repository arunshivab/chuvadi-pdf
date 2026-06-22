// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) Annex E.3.2 — arithmetic decoding procedures
//        (INITDEC, DECODE, MPS_EXCHANGE, LPS_EXCHANGE, RENORMD, BYTEIN).
// PHASE: Phase 2 — item 22, JBIG2 decode.

using System;

namespace Chuvadi.Pdf.Filters.Jbig2;

/// <summary>
/// The MQ arithmetic decoder defined in ITU-T T.88 Annex E. It consumes a byte
/// span and produces a stream of binary decisions, each decoded against an
/// adaptive context. A context is a single byte packing the probability state
/// index in bits 1–7 and the most-probable-symbol sense in bit 0; callers own
/// the context array (JBIG2 templates address thousands of contexts) and pass
/// the index of the active context to <see cref="Decode"/>.
/// </summary>
internal sealed class MQDecoder
{
    private readonly byte[] _data;
    private readonly int _end;
    private int _bp;
    private uint _c;
    private uint _a;
    private int _ct;

    /// <summary>
    /// Initialises a decoder over <paramref name="data"/> in the half-open byte
    /// range [<paramref name="start"/>, <paramref name="end"/>) and runs INITDEC.
    /// </summary>
    /// <param name="data">The buffer holding the arithmetic-coded bytes.</param>
    /// <param name="start">Inclusive start offset of the coded range.</param>
    /// <param name="end">Exclusive end offset of the coded range.</param>
    internal MQDecoder(byte[] data, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _end = end;
        _bp = start;

        // INITDEC (T.88 E.3.5).
        _c = (uint)ByteAt(_bp) << 16;
        ByteIn();
        _c <<= 7;
        _ct -= 7;
        _a = 0x8000;
    }

    /// <summary>
    /// Decodes one binary decision against the context stored at
    /// <paramref name="index"/> in <paramref name="cx"/>, updating that context's
    /// adaptive state in place.
    /// </summary>
    /// <param name="cx">Context-state array; each entry is (state &lt;&lt; 1) | mps.</param>
    /// <param name="index">Index of the active context within <paramref name="cx"/>.</param>
    /// <returns>The decoded bit, 0 or 1.</returns>
    internal int Decode(byte[] cx, int index)
    {
        int state = cx[index] >> 1;
        int mps = cx[index] & 1;
        uint qe = ArithmeticTables.Qe[state];

        _a -= qe;
        int d;

        if ((_c >> 16) < qe)
        {
            // LPS_EXCHANGE (T.88 E.3.4).
            if (_a < qe)
            {
                _a = qe;
                d = mps;
                state = ArithmeticTables.Nmps[state];
            }
            else
            {
                _a = qe;
                d = 1 - mps;
                if (ArithmeticTables.Switch[state] == 1) { mps = 1 - mps; }
                state = ArithmeticTables.Nlps[state];
            }

            Renorm();
        }
        else
        {
            _c -= qe << 16;

            if ((_a & 0x8000) == 0)
            {
                // MPS_EXCHANGE (T.88 E.3.3).
                if (_a < qe)
                {
                    d = 1 - mps;
                    if (ArithmeticTables.Switch[state] == 1) { mps = 1 - mps; }
                    state = ArithmeticTables.Nlps[state];
                }
                else
                {
                    d = mps;
                    state = ArithmeticTables.Nmps[state];
                }

                Renorm();
            }
            else
            {
                cx[index] = (byte)((state << 1) | mps);
                return mps;
            }
        }

        cx[index] = (byte)((state << 1) | mps);
        return d;
    }

    // RENORMD (T.88 E.3.6).
    private void Renorm()
    {
        do
        {
            if (_ct == 0) { ByteIn(); }
            _a <<= 1;
            _c <<= 1;
            _ct -= 1;
        }
        while ((_a & 0x8000) == 0);
    }

    // BYTEIN (T.88 E.3.7). Past the end of the coded range the decoder behaves as
    // though reading 0xFF, the standard terminating convention.
    private void ByteIn()
    {
        if (ByteAt(_bp) == 0xFF)
        {
            if (ByteAt(_bp + 1) > 0x8F)
            {
                _c += 0xFF00;
                _ct = 8;
            }
            else
            {
                _bp += 1;
                _c += (uint)ByteAt(_bp) << 9;
                _ct = 7;
            }
        }
        else
        {
            _bp += 1;
            _c += (uint)ByteAt(_bp) << 8;
            _ct = 8;
        }
    }

    private int ByteAt(int index)
    {
        return index >= 0 && index < _end ? _data[index] : 0xFF;
    }
}
