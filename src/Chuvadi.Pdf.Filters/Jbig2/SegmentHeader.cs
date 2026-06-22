// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) §7.2 — segment header syntax.
// PHASE: Phase 2 — item 22, JBIG2 decode.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Filters.Jbig2;

/// <summary>
/// A parsed JBIG2 segment header (ITU-T T.88 §7.2): the segment's number, type,
/// the segments it refers to, its page association, and the offset and length of
/// its data within the stream.
/// </summary>
internal sealed class SegmentHeader
{
    /// <summary>Segment number (§7.2.2).</summary>
    internal uint Number { get; init; }

    /// <summary>Segment type (low 6 bits of the flags byte, §7.2.3).</summary>
    internal int Type { get; init; }

    /// <summary>Page this segment is associated with (§7.2.6).</summary>
    internal uint PageAssociation { get; init; }

    /// <summary>Numbers of the segments this segment refers to (§7.2.5).</summary>
    internal IReadOnlyList<uint> ReferredTo { get; init; } = Array.Empty<uint>();

    /// <summary>Offset of the segment's data within the stream.</summary>
    internal int DataStart { get; init; }

    /// <summary>Length of the segment's data in bytes (§7.2.7).</summary>
    internal uint DataLength { get; init; }

    // Segment type codes (T.88 §7.3) used by the PR-1 decoder.
    internal const int TypeSymbolDictionary = 0;
    internal const int TypeIntermediateTextRegion = 4;
    internal const int TypeImmediateTextRegion = 6;
    internal const int TypeImmediateLosslessTextRegion = 7;
    internal const int TypePatternDictionary = 16;
    internal const int TypeIntermediateGenericRegion = 36;
    internal const int TypeImmediateGenericRegion = 38;
    internal const int TypeImmediateLosslessGenericRegion = 39;
    internal const int TypePageInformation = 48;
    internal const int TypeEndOfPage = 49;
    internal const int TypeEndOfStripe = 50;
    internal const int TypeEndOfFile = 51;

    /// <summary>
    /// Reads one segment header (not its data) from <paramref name="reader"/>,
    /// leaving the reader positioned at the start of the segment data.
    /// </summary>
    /// <param name="reader">The stream reader positioned at a segment header.</param>
    /// <returns>The parsed header.</returns>
    internal static SegmentHeader Read(Jbig2Reader reader)
    {
        uint number = reader.ReadUInt32();

        int flags = reader.ReadByte();
        int type = flags & 0x3F;
        bool pageAssociationIsFourBytes = (flags & 0x40) != 0;

        // Referred-to segment count and retention flags (§7.2.4).
        int refByte = reader.ReadByte();
        int refCount = refByte >> 5;
        if (refCount == 7)
        {
            // Long form: count is in the low 29 bits of a 4-byte value whose top
            // 3 bits are the 0b111 just read; then ceil((count+1)/8) retain bytes.
            reader.Position -= 1;
            uint longCount = reader.ReadUInt32() & 0x1FFFFFFF;
            refCount = (int)longCount;
            int retainBytes = (refCount + 8) / 8;
            reader.Position += retainBytes;
        }

        // Referred-to segment numbers (§7.2.5): width depends on this segment's number.
        int refSize = number <= 256 ? 1 : number <= 65536 ? 2 : 4;
        List<uint> referred = new List<uint>(refCount);
        for (int i = 0; i < refCount; i++)
        {
            uint refNumber = refSize switch
            {
                1 => (uint)reader.ReadByte(),
                2 => (uint)reader.ReadUInt16(),
                _ => reader.ReadUInt32(),
            };
            referred.Add(refNumber);
        }

        // Page association (§7.2.6).
        uint page = pageAssociationIsFourBytes ? reader.ReadUInt32() : (uint)reader.ReadByte();

        // Segment data length (§7.2.7).
        uint dataLength = reader.ReadUInt32();

        return new SegmentHeader
        {
            Number = number,
            Type = type,
            PageAssociation = page,
            ReferredTo = referred,
            DataStart = reader.Position,
            DataLength = dataLength,
        };
    }
}
