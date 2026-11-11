// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType specification — cmap (formats 4 and 12)
//        https://docs.microsoft.com/typography/opentype/spec/cmap
// PHASE: Phase 2.1 — v2.1.6
//        Extracted verbatim from TrueTypeFontPatch (v2.1.5) so the
//        CFF-to-OpenType wrapper (OpenTypeFontBuilder) can synthesise a cmap
//        table. Pure extraction: byte-for-byte identical output to the prior
//        in-place BuildCmapTable implementation. The code-point filtering and
//        sorting previously performed by the caller are folded in here so
//        every consumer gets the same behaviour.

using System;
using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Builds a complete OpenType <c>cmap</c> table containing a single Windows
/// Unicode subtable — format 4 for Basic Multilingual Plane code points, or
/// format 12 when any code point lies above U+FFFF.
/// </summary>
internal static class CmapSubtableBuilder
{
    /// <summary>
    /// Builds a <c>cmap</c> table mapping Unicode code points to glyph indices.
    /// Entries with a negative code point, or a glyph index outside the
    /// unsigned 16-bit range, are skipped; the remaining mappings still produce
    /// a valid table. Mappings are emitted in ascending code-point order.
    /// </summary>
    /// <param name="unicodeToGid">Map from Unicode code point to glyph index.</param>
    /// <returns>The complete <c>cmap</c> table bytes: 4-byte header, one 8-byte encoding record, and the subtable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="unicodeToGid"/> is null.</exception>
    internal static byte[] BuildCmapTable(IReadOnlyDictionary<int, int> unicodeToGid)
    {
        ArgumentNullException.ThrowIfNull(unicodeToGid);

        // Filter to valid entries and sort so segments build deterministically.
        // Glyph indices are uint16; out-of-range entries are silently skipped.
        SortedDictionary<int, int> mappings = new();
        foreach (KeyValuePair<int, int> kv in unicodeToGid)
        {
            if (kv.Key < 0 || kv.Value < 0 || kv.Value > 0xFFFF)
            {
                continue;
            }

            mappings[kv.Key] = kv.Value;
        }

        // cmap header: 4 bytes (version + numTables) + 8 bytes per EncodingRecord.
        // We emit a single EncodingRecord (Windows, Unicode BMP or full).
        bool needsFormat12 = false;
        foreach (int cp in mappings.Keys)
        {
            if (cp > 0xFFFF)
            {
                needsFormat12 = true;
                break;
            }
        }

        byte[] subtable;
        int encodingId;
        if (needsFormat12)
        {
            subtable = BuildFormat12Subtable(mappings);
            encodingId = 10; // Windows / Unicode UCS-4
        }
        else
        {
            subtable = BuildFormat4Subtable(mappings);
            encodingId = 1;  // Windows / Unicode BMP
        }

        // 4-byte header + one 8-byte EncodingRecord + subtable body.
        using MemoryStream ms = new();
        WriteUInt16BE(ms, 0);                 // version
        WriteUInt16BE(ms, 1);                 // numTables
        WriteUInt16BE(ms, 3);                 // platformID = Windows
        WriteUInt16BE(ms, encodingId);
        WriteUInt32BE(ms, 12u);               // subtableOffset (4 header + 8 record)
        ms.Write(subtable, 0, subtable.Length);
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a <c>cmap</c> table with one (3,0) Microsoft Symbol subtable that
    /// maps each single-byte character code <c>c</c> (as <c>0xF000 + c</c>) to a
    /// glyph index: the one cmap a symbolic simple TrueType font in PDF/A-1
    /// may carry (ISO 19005-1 §6.3.7; PDF 32000-1:2008 §9.6.6.4). 3.33.0.
    /// </summary>
    /// <param name="codeToGid">Map from character code (0-255) to glyph index.</param>
    /// <returns>The complete <c>cmap</c> table bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="codeToGid"/> is null.</exception>
    internal static byte[] BuildSymbolCmapTable(IReadOnlyDictionary<int, int> codeToGid)
    {
        ArgumentNullException.ThrowIfNull(codeToGid);

        SortedDictionary<int, int> mappings = new();
        foreach (KeyValuePair<int, int> kv in codeToGid)
        {
            if (kv.Key < 0 || kv.Key > 0xFF || kv.Value <= 0 || kv.Value > 0xFFFF)
            {
                continue;
            }

            mappings[0xF000 + kv.Key] = kv.Value;
        }

        byte[] subtable = BuildFormat4Subtable(mappings);
        using MemoryStream ms = new();
        WriteUInt16BE(ms, 0);                 // version
        WriteUInt16BE(ms, 1);                 // numTables
        WriteUInt16BE(ms, 3);                 // platformID = Windows
        WriteUInt16BE(ms, 0);                 // encodingID = Symbol
        WriteUInt32BE(ms, 12u);               // subtableOffset (4 header + 8 record)
        ms.Write(subtable, 0, subtable.Length);
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a cmap format-4 (segment mapping to delta values) subtable.
    /// One single-codepoint segment per mapping plus the mandatory 0xFFFF
    /// sentinel. Verbose but minimal and correct.
    /// </summary>
    private static byte[] BuildFormat4Subtable(SortedDictionary<int, int> mappings)
    {
        // segments: one per mapping + final sentinel (0xFFFF -> glyph 0).
        List<int> endCodes = new();
        List<int> startCodes = new();
        List<int> idDeltas = new();

        foreach (KeyValuePair<int, int> kv in mappings)
        {
            int cp = kv.Key;
            int gid = kv.Value;
            endCodes.Add(cp);
            startCodes.Add(cp);
            // idDelta is added modulo 65536 to the code point to produce GID.
            idDeltas.Add((gid - cp) & 0xFFFF);
        }

        // Mandatory terminating segment: startCode = endCode = 0xFFFF.
        endCodes.Add(0xFFFF);
        startCodes.Add(0xFFFF);
        idDeltas.Add(1);  // 0xFFFF + 1 mod 65536 = 0 = .notdef

        int segCount = endCodes.Count;
        int segCountX2 = segCount * 2;

        // searchRange = 2 x 2^floor(log2(segCount))
        int searchRange = 2;
        int entrySelector = 0;
        while (searchRange * 2 <= segCountX2)
        {
            searchRange *= 2;
            entrySelector++;
        }
        int rangeShift = segCountX2 - searchRange;

        // Layout: 14-byte header
        //       + endCodes (2*segCount)
        //       + reservedPad (2)
        //       + startCodes (2*segCount)
        //       + idDeltas (2*segCount)
        //       + idRangeOffsets (2*segCount, all zero)
        //       + glyphIdArray (0 bytes - unused since idRangeOffsets are zero)
        int length = 14 + (2 * segCount) + 2 + (2 * segCount) + (2 * segCount) + (2 * segCount);

        using MemoryStream ms = new();
        WriteUInt16BE(ms, 4);              // format
        WriteUInt16BE(ms, length);          // length
        WriteUInt16BE(ms, 0);              // language
        WriteUInt16BE(ms, segCountX2);
        WriteUInt16BE(ms, searchRange);
        WriteUInt16BE(ms, entrySelector);
        WriteUInt16BE(ms, rangeShift);

        foreach (int v in endCodes) { WriteUInt16BE(ms, v); }
        WriteUInt16BE(ms, 0);              // reservedPad
        foreach (int v in startCodes) { WriteUInt16BE(ms, v); }
        foreach (int v in idDeltas) { WriteUInt16BE(ms, v); }
        for (int i = 0; i < segCount; i++) { WriteUInt16BE(ms, 0); } // idRangeOffsets

        return ms.ToArray();
    }

    /// <summary>
    /// Builds a cmap format-12 (segmented coverage) subtable. Used when any
    /// code point exceeds 0xFFFF.
    /// </summary>
    private static byte[] BuildFormat12Subtable(SortedDictionary<int, int> mappings)
    {
        // Group contiguous (startCharCode, endCharCode, startGlyphID) runs
        // where consecutive code points map to consecutive glyph indices.
        // For sparse symbol fonts this collapses to one group per entry,
        // which is the worst case but still correct.
        List<(int Start, int End, int StartGid)> groups = new();
        foreach (KeyValuePair<int, int> kv in mappings)
        {
            int cp = kv.Key;
            int gid = kv.Value;
            if (groups.Count > 0)
            {
                (int Start, int End, int StartGid) last = groups[^1];
                if (cp == last.End + 1 && gid == last.StartGid + (last.End - last.Start + 1))
                {
                    groups[^1] = (last.Start, cp, last.StartGid);
                    continue;
                }
            }

            groups.Add((cp, cp, gid));
        }

        // Header (16 bytes) + 12 bytes per group.
        uint length = (uint)(16 + groups.Count * 12);

        using MemoryStream ms = new();
        WriteUInt16BE(ms, 12);             // format
        WriteUInt16BE(ms, 0);              // reserved
        WriteUInt32BE(ms, length);
        WriteUInt32BE(ms, 0);              // language
        WriteUInt32BE(ms, (uint)groups.Count);

        foreach ((int start, int end, int startGid) in groups)
        {
            WriteUInt32BE(ms, (uint)start);
            WriteUInt32BE(ms, (uint)end);
            WriteUInt32BE(ms, (uint)startGid);
        }

        return ms.ToArray();
    }

    private static void WriteUInt16BE(MemoryStream ms, int value)
    {
        ms.WriteByte((byte)((value >> 8) & 0xFF));
        ms.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteUInt32BE(MemoryStream ms, uint value)
    {
        ms.WriteByte((byte)((value >> 24) & 0xFF));
        ms.WriteByte((byte)((value >> 16) & 0xFF));
        ms.WriteByte((byte)((value >> 8) & 0xFF));
        ms.WriteByte((byte)(value & 0xFF));
    }
}
