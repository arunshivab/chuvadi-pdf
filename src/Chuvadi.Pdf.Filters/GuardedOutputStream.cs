// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.24 — embedding API (A3, A4)

using System;
using System.IO;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Filters;

// A decode output buffer that checks the ambient PdfWorkScope (cancellation,
// the decoded-size limit) and charges its growth against the memory limit
// each time it grows by another mebibyte, so a filter writing incrementally
// stops early instead of filling memory.
internal sealed class GuardedOutputStream : MemoryStream
{
    private const long CheckInterval = 1 << 20;
    private long _nextCheck = CheckInterval;

    // Bytes already charged against the memory limit.
    internal long Charged { get; private set; }

    public override void Write(byte[] buffer, int offset, int count)
    {
        base.Write(buffer, offset, count);
        Check();
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        base.Write(buffer);
        Check();
    }

    public override void WriteByte(byte value)
    {
        base.WriteByte(value);
        Check();
    }

    private void Check()
    {
        if (Length >= _nextCheck)
        {
            PdfWorkScope.CheckDecodedSize(Length);
            PdfWorkScope.ChargeMemory(Length - Charged);
            Charged = Length;
            _nextCheck = Length + CheckInterval;
        }
    }
}
