// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  W3C WOFF2 Recommendation 2018-03-01
//        https://www.w3.org/TR/WOFF2/
// PHASE: Phase 2.1 — WOFF2 packer

using System;
using System.Collections.Generic;
using System.IO;

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// Packs a TrueType / OpenType font into the WOFF2 container format.
/// </summary>
/// <remarks>
/// <para>
/// WOFF2 = WOFF header + table directory + Brotli-compressed concatenated
/// table bodies. Phase 2.1 v1 uses <see cref="BrotliStoredEncoder"/> which
/// emits stored (uncompressed) Brotli blocks — the resulting WOFF2 file is
/// valid for every conforming WOFF2 decoder (including all modern browsers)
/// but does not realize compression gains. Phase 2.2 will add a real Brotli
/// compressor.
/// </para>
/// <para>
/// The transformed-glyf and transformed-loca optimizations are not applied
/// in v1: tables are passed through verbatim. This is also spec-compliant
/// (the transform is optional per the spec).
/// </para>
/// </remarks>
[System.Obsolete("Deprecated: no longer used by Chuvadi; for the LiPi Sans faces use Chuvadi.Fonts (FontPack). Removed at the Pdf move.")]
public static class Woff2Packer
{
    private const uint Woff2Signature = 0x774F4632;   // 'wOF2'
    private const uint TrueTypeFlavor = 0x00010000;
    private const uint OtfFlavor = 0x4F54544F;        // 'OTTO'

    /// <summary>Packs a TrueType/OpenType font into a WOFF2 byte stream.</summary>
    public static byte[] Pack(byte[] sfntFont)
    {
        ArgumentNullException.ThrowIfNull(sfntFont);
        if (sfntFont.Length < 12) { throw new ArgumentException("Font data too short.", nameof(sfntFont)); }

        // Parse sfnt header
        uint flavor = ReadUInt32BE(sfntFont, 0);
        if (flavor != TrueTypeFlavor && flavor != OtfFlavor && flavor != 0x74727565)   // 'true'
        {
            throw new ArgumentException("Not a valid sfnt-based font.", nameof(sfntFont));
        }
        ushort numTables = (ushort)((sfntFont[4] << 8) | sfntFont[5]);

        // Read table records
        List<TableRecord> records = new();
        int dirOff = 12;
        for (int i = 0; i < numTables; i++)
        {
            int rec = dirOff + i * 16;
            uint tag = ReadUInt32BE(sfntFont, rec);
            _ = ReadUInt32BE(sfntFont, rec + 4);   // checksum
            uint offset = ReadUInt32BE(sfntFont, rec + 8);
            uint length = ReadUInt32BE(sfntFont, rec + 12);
            records.Add(new TableRecord(tag, offset, length));
        }
        records.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        // Concatenate table bodies in stored order
        using MemoryStream rawBodies = new();
        foreach (TableRecord r in records)
        {
            if (r.Offset + r.Length > sfntFont.Length) { continue; }
            rawBodies.Write(sfntFont, (int)r.Offset, (int)r.Length);
        }
        byte[] compressed = BrotliStoredEncoder.Encode(rawBodies.ToArray());

        // Build WOFF2 output
        using MemoryStream output = new();

        // Header (48 bytes)
        WriteUInt32BE(output, Woff2Signature);
        WriteUInt32BE(output, flavor);
        int lengthPos = (int)output.Position;
        WriteUInt32BE(output, 0);                // length — fill later
        WriteUInt16BE(output, numTables);
        WriteUInt16BE(output, 0);                // reserved
        WriteUInt32BE(output, (uint)rawBodies.Length); // totalSfntSize
        WriteUInt32BE(output, (uint)compressed.Length); // totalCompressedSize
        WriteUInt16BE(output, 1);                // majorVersion
        WriteUInt16BE(output, 0);                // minorVersion
        WriteUInt32BE(output, 0);                // metaOffset
        WriteUInt32BE(output, 0);                // metaLength
        WriteUInt32BE(output, 0);                // metaOrigLength
        WriteUInt32BE(output, 0);                // privOffset
        WriteUInt32BE(output, 0);                // privLength

        // Table directory: per-table 4-bit flags + variable-length encoded sizes
        foreach (TableRecord r in records)
        {
            // Flags byte: bits 0-5 = known-tag index (63 = "use 4-byte tag")
            // bits 6-7 = transform version (0 = no transform)
            int knownIdx = WoffKnownTags.IndexOf(r.Tag);
            byte flags = (byte)((knownIdx >= 0 ? knownIdx : 63) & 0x3F);
            output.WriteByte(flags);
            if (knownIdx < 0)
            {
                WriteUInt32BE(output, r.Tag);
            }
            // origLength as Base128 varint
            WriteBase128(output, r.Length);
            // For tables with transform version 0 (no transform), transformLength is not
            // present per spec.
        }

        // Compressed body
        output.Write(compressed, 0, compressed.Length);

        // Pad to 4-byte boundary
        while (output.Length % 4 != 0) { output.WriteByte(0); }

        // Patch in total length
        long total = output.Length;
        output.Position = lengthPos;
        WriteUInt32BE(output, (uint)total);

        return output.ToArray();
    }

    /// <summary>True if WOFF2 packing is fully effective. Phase 2.1 = false (stored Brotli).</summary>
    public static bool ProducesCompressedOutput => false;

    private static void WriteUInt32BE(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static void WriteUInt16BE(Stream s, int v)
    {
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static uint ReadUInt32BE(byte[] data, int offset)
        => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
         | ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static void WriteBase128(Stream s, uint value)
    {
        // 7-bit groups, MSB first, last byte has high bit clear.
        Span<byte> buf = stackalloc byte[5];
        int len = 0;
        do
        {
            buf[len++] = (byte)(value & 0x7F);
            value >>= 7;
        } while (value != 0);

        for (int i = len - 1; i > 0; i--)
        {
            s.WriteByte((byte)(buf[i] | 0x80));
        }
        s.WriteByte(buf[0]);
    }

    private readonly record struct TableRecord(uint Tag, uint Offset, uint Length);
}

/// <summary>WOFF2-specified known-table tags by index.</summary>
internal static class WoffKnownTags
{
    private static readonly uint[] Tags =
    {
        Tag("cmap"), Tag("head"), Tag("hhea"), Tag("hmtx"),
        Tag("maxp"), Tag("name"), Tag("OS/2"), Tag("post"),
        Tag("cvt "), Tag("fpgm"), Tag("glyf"), Tag("loca"),
        Tag("prep"), Tag("CFF "), Tag("VORG"), Tag("EBDT"),
        Tag("EBLC"), Tag("gasp"), Tag("hdmx"), Tag("kern"),
        Tag("LTSH"), Tag("PCLT"), Tag("VDMX"), Tag("vhea"),
        Tag("vmtx"), Tag("BASE"), Tag("GDEF"), Tag("GPOS"),
        Tag("GSUB"), Tag("EBSC"), Tag("JSTF"), Tag("MATH"),
        Tag("CBDT"), Tag("CBLC"), Tag("COLR"), Tag("CPAL"),
        Tag("SVG "), Tag("sbix"), Tag("acnt"), Tag("avar"),
        Tag("bdat"), Tag("bloc"), Tag("bsln"), Tag("cvar"),
        Tag("fdsc"), Tag("feat"), Tag("fmtx"), Tag("fvar"),
        Tag("gvar"), Tag("hsty"), Tag("just"), Tag("lcar"),
        Tag("mort"), Tag("morx"), Tag("opbd"), Tag("prop"),
        Tag("trak"), Tag("Zapf"), Tag("Silf"), Tag("Glat"),
        Tag("Gloc"), Tag("Feat"), Tag("Sill"),
    };

    private static uint Tag(string s)
        => ((uint)s[0] << 24) | ((uint)s[1] << 16) | ((uint)s[2] << 8) | s[3];

    internal static int IndexOf(uint tag)
    {
        for (int i = 0; i < Tags.Length; i++) { if (Tags[i] == tag) { return i; } }
        return -1;
    }
}
