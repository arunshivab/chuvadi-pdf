// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) Annex E.3.3–E.3.9 — arithmetic encoding procedures
//        (ENCODE, CODEMPS, CODELPS, RENORME, BYTEOUT, FLUSH, SETBITS).
// PHASE: Phase 2 — items 22/23, JBIG2 encode (and round-trip validation of 22).

using System.Collections.Generic;

namespace Chuvadi.Pdf.Filters.Jbig2;

/// <summary>
/// The MQ arithmetic encoder defined in ITU-T T.88 Annex E — the exact inverse
/// of <see cref="MQDecoder"/>. Decisions are coded against adaptive contexts
/// (the same packed (state &lt;&lt; 1) | mps byte the decoder uses); carry
/// propagation and bit-stuffing follow the BYTEOUT/FLUSH conventions so the
/// produced bytes decode back identically.
/// </summary>
internal sealed class MQEncoder
{
    // Index 0 holds the "byte before the stream start" sentinel the BYTEOUT
    // carry logic reads; real output begins at index 1 and is returned by Flush.
    private readonly List<byte> _out = new() { 0 };
    private int _bp;
    private uint _c;
    private uint _a;
    private int _ct;

    /// <summary>Initialises the encoder (INITENC, T.88 E.3.5).</summary>
    internal MQEncoder()
    {
        _bp = 0;
        _c = 0;
        _a = 0x8000;
        _ct = 12;
    }

    /// <summary>
    /// Encodes one binary decision against the context at <paramref name="index"/>
    /// in <paramref name="cx"/>, updating that context's adaptive state in place.
    /// </summary>
    /// <param name="cx">Context-state array; each entry is (state &lt;&lt; 1) | mps.</param>
    /// <param name="index">Index of the active context within <paramref name="cx"/>.</param>
    /// <param name="d">The decision to encode, 0 or 1.</param>
    internal void Encode(byte[] cx, int index, int d)
    {
        int state = cx[index] >> 1;
        int mps = cx[index] & 1;
        uint qe = ArithmeticTables.Qe[state];

        _a -= qe;

        if (d == mps)
        {
            // CODEMPS (T.88 E.3.3).
            if ((_a & 0x8000) == 0)
            {
                if (_a < qe) { _a = qe; }
                else { _c += qe; }
                state = ArithmeticTables.Nmps[state];
                Renorm();
            }
            else
            {
                _c += qe;
            }
        }
        else
        {
            // CODELPS (T.88 E.3.4).
            if (_a < qe) { _c += qe; }
            else { _a = qe; }
            if (ArithmeticTables.Switch[state] == 1) { mps = 1 - mps; }
            state = ArithmeticTables.Nlps[state];
            Renorm();
        }

        cx[index] = (byte)((state << 1) | mps);
    }

    /// <summary>
    /// Flushes the final bytes (SETBITS + two BYTEOUTs, T.88 E.3.8/E.3.9) and
    /// returns the complete arithmetic-coded byte stream.
    /// </summary>
    /// <returns>The coded bytes, ready to feed to <see cref="MQDecoder"/>.</returns>
    internal byte[] Flush()
    {
        // SETBITS (T.88 E.3.9).
        uint tempc = _c + _a;
        _c |= 0xFFFF;
        if (_c >= tempc) { _c -= 0x8000; }

        _c <<= _ct;
        ByteOut();
        _c <<= _ct;
        ByteOut();

        // Drop the leading sentinel; the coded stream is bytes [1.._bp].
        int count = _bp;
        byte[] result = new byte[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = _out[i + 1];
        }

        return result;
    }

    // RENORME (T.88 E.3.7).
    private void Renorm()
    {
        do
        {
            if (_ct == 0) { ByteOut(); }
            _a <<= 1;
            _c <<= 1;
            _ct -= 1;
        }
        while ((_a & 0x8000) == 0);
    }

    // BYTEOUT (T.88 E.3.6). _bp always indexes the last element of _out.
    private void ByteOut()
    {
        if (_out[_bp] == 0xFF)
        {
            _bp += 1;
            _out.Add((byte)(_c >> 20));
            _c &= 0xFFFFF;
            _ct = 7;
        }
        else
        {
            if (_c < 0x8000000u)
            {
                _bp += 1;
                _out.Add((byte)(_c >> 19));
                _c &= 0x7FFFF;
                _ct = 8;
            }
            else
            {
                _out[_bp] = (byte)(_out[_bp] + 1);
                if (_out[_bp] == 0xFF)
                {
                    _c &= 0x7FFFFFF;
                    _bp += 1;
                    _out.Add((byte)(_c >> 20));
                    _c &= 0xFFFFF;
                    _ct = 7;
                }
                else
                {
                    _bp += 1;
                    _out.Add((byte)(_c >> 19));
                    _c &= 0x7FFFF;
                    _ct = 8;
                }
            }
        }
    }
}
