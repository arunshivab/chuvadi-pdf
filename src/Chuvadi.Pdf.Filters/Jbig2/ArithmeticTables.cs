// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) Annex E, Table E.1 — MQ-coder probability estimation.
//        Identical to the state machine used by JPEG 2000 (ITU-T T.800 Annex C).
// PHASE: Phase 2 — items 22/23, JBIG2 decode/encode (shared arithmetic coder).

namespace Chuvadi.Pdf.Filters.Jbig2;

/// <summary>
/// The MQ arithmetic coder's probability-estimation state machine (ITU-T T.88
/// Table E.1). Each of the 47 states carries the LPS probability estimate
/// <c>Qe</c> (a 16-bit sub-interval), the next state after an MPS renormalisation
/// (<c>Nmps</c>) and after an LPS renormalisation (<c>Nlps</c>), and a
/// <c>Switch</c> flag that toggles the MPS sense when the symbol probabilities are
/// near-equal. The table is shared verbatim by the decoder and the encoder.
/// </summary>
internal static class ArithmeticTables
{
    /// <summary>The number of states in the probability-estimation machine.</summary>
    internal const int StateCount = 47;

    /// <summary>LPS sub-interval estimate <c>Qe</c> for each state.</summary>
    internal static readonly ushort[] Qe =
    {
        0x5601, 0x3401, 0x1801, 0x0AC1, 0x0521, 0x0221, 0x5601, 0x5401,
        0x4801, 0x3801, 0x3001, 0x2401, 0x1C01, 0x1601, 0x5601, 0x5401,
        0x5101, 0x4801, 0x3801, 0x3401, 0x3001, 0x2801, 0x2401, 0x2201,
        0x1C01, 0x1801, 0x1601, 0x1401, 0x1201, 0x1101, 0x0AC1, 0x09C1,
        0x08A1, 0x0521, 0x0441, 0x02A1, 0x0221, 0x0141, 0x0111, 0x0085,
        0x0049, 0x0025, 0x0015, 0x0009, 0x0005, 0x0001, 0x5601,
    };

    /// <summary>Next state after a most-probable-symbol renormalisation.</summary>
    internal static readonly byte[] Nmps =
    {
        1, 2, 3, 4, 5, 38, 7, 8, 9, 10, 11, 12, 13, 29, 15, 16,
        17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
        33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 45, 46,
    };

    /// <summary>Next state after a least-probable-symbol renormalisation.</summary>
    internal static readonly byte[] Nlps =
    {
        1, 6, 9, 12, 29, 33, 6, 14, 14, 14, 17, 18, 20, 21, 14, 14,
        15, 16, 17, 18, 19, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29,
        30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 46,
    };

    /// <summary>MPS-sense toggle flag (1 = exchange MPS on LPS renormalisation).</summary>
    internal static readonly byte[] Switch =
    {
        1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    };
}
