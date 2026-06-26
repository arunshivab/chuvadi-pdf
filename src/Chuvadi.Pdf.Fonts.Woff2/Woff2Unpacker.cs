// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  W3C WOFF2 Recommendation 2018-03-01
//        https://www.w3.org/TR/WOFF2/
// PHASE: Phase 3 — WOFF2 unpacker (decoder); inverse of Woff2Packer.
//
// Decodes a WOFF2 font into an sfnt (TrueType/glyf) byte array suitable for PDF
// font embedding. Reverses the transformed 'glyf'/'loca' encoding (triplet point
// coding, composite reconstruction, bbox bitmap) and reassembles a valid sfnt
// with corrected table checksums. Brotli decompression uses the BCL
// (System.IO.Compression), so no third-party package is required.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// Decodes a WOFF2 font into an sfnt (TrueType) byte array. The inverse of
/// <see cref="Woff2Packer"/>; intended for converting WOFF2 assets into a form
/// that can be embedded in a PDF.
/// </summary>
public static class Woff2Unpacker
{
    private const uint Woff2Signature = 0x774F4632; // 'wOF2'
    private const int HeaderSize = 48;
    private const int GlyfTransformHeaderSize = 36;

    // Component flags (sfnt composite glyph), per ISO/IEC 14496-22 / OpenType.
    private const int ArgsAreWords = 0x0001;
    private const int WeHaveAScale = 0x0008;
    private const int MoreComponents = 0x0020;
    private const int WeHaveXandYScale = 0x0040;
    private const int WeHaveTwoByTwo = 0x0080;
    private const int WeHaveInstructions = 0x0100;

    // The 63 WOFF2 "known" table tags (table-directory index → tag), per the
    // WOFF2 spec. Index 63 (0x3f) means an explicit 4-byte tag follows.
    private static readonly uint[] KnownTags =
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

    private static readonly uint GlyfTag = Tag("glyf");
    private static readonly uint LocaTag = Tag("loca");
    private static readonly uint HeadTag = Tag("head");

    private sealed class TableEntry
    {
        public uint Tag { get; init; }
        public int TransformVersion { get; init; }
        public int OrigLength { get; init; }
        public int TransformLength { get; init; }
        public bool Transformed { get; init; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Decodes a WOFF2 font into an sfnt (TrueType) byte array.
    /// </summary>
    /// <param name="woff2">The WOFF2 font bytes.</param>
    /// <returns>The decoded sfnt (TrueType) font bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="woff2"/> is null.</exception>
    /// <exception cref="InvalidDataException">The input is not a supported WOFF2 font.</exception>
    public static byte[] Unpack(byte[] woff2)
    {
        ArgumentNullException.ThrowIfNull(woff2);
        if (woff2.Length < HeaderSize)
        {
            throw new InvalidDataException("WOFF2 data too short for a header.");
        }

        int p = 0;
        uint signature = ReadU32(woff2, ref p);
        if (signature != Woff2Signature)
        {
            throw new InvalidDataException("Not a WOFF2 font (bad signature).");
        }

        uint flavor = ReadU32(woff2, ref p);
        ReadU32(woff2, ref p);                 // length
        int numTables = ReadU16(woff2, ref p);
        ReadU16(woff2, ref p);                 // reserved
        ReadU32(woff2, ref p);                 // totalSfntSize
        int totalCompressedSize = (int)ReadU32(woff2, ref p);
        ReadU16(woff2, ref p);                 // majorVersion
        ReadU16(woff2, ref p);                 // minorVersion
        ReadU32(woff2, ref p);                 // metaOffset
        ReadU32(woff2, ref p);                 // metaLength
        ReadU32(woff2, ref p);                 // metaOrigLength
        ReadU32(woff2, ref p);                 // privOffset
        ReadU32(woff2, ref p);                 // privLength

        List<TableEntry> tables = new List<TableEntry>(numTables);
        for (int i = 0; i < numTables; i++)
        {
            int flags = woff2[p++];
            int tagIndex = flags & 0x3f;
            int transformVersion = (flags >> 6) & 0x3;
            uint tag = tagIndex == 0x3f ? ReadU32(woff2, ref p) : KnownTags[tagIndex];

            int origLength = ReadBase128(woff2, ref p);
            bool hasTransform = (tag == GlyfTag || tag == LocaTag) && transformVersion == 0;
            int transformLength = hasTransform ? ReadBase128(woff2, ref p) : origLength;

            tables.Add(new TableEntry
            {
                Tag = tag,
                TransformVersion = transformVersion,
                OrigLength = origLength,
                TransformLength = transformLength,
                Transformed = hasTransform,
            });
        }

        byte[] compressed = new byte[totalCompressedSize];
        Array.Copy(woff2, p, compressed, 0, totalCompressedSize);
        byte[] decompressed = BrotliDecompress(compressed);

        // Slice each table's (possibly transformed) data out of the stream.
        int sp = 0;
        foreach (TableEntry t in tables)
        {
            int size = t.Transformed ? t.TransformLength : t.OrigLength;
            byte[] data = new byte[size];
            Array.Copy(decompressed, sp, data, 0, size);
            sp += size;
            t.Data = data;
        }

        // Reconstruct transformed glyf -> (glyf, loca).
        TableEntry? glyf = tables.Find(t => t.Tag == GlyfTag);
        TableEntry? loca = tables.Find(t => t.Tag == LocaTag);
        if (glyf is { Transformed: true })
        {
            (byte[] glyfData, byte[] locaData) = ReconstructGlyf(glyf.Data);
            glyf.Data = glyfData;
            if (loca is { } locaEntry)
            {
                locaEntry.Data = locaData;
            }
        }

        return AssembleSfnt(flavor, tables);
    }

    // ── Transformed glyf reconstruction ──────────────────────────────────────

    private static (byte[] Glyf, byte[] Loca) ReconstructGlyf(byte[] data)
    {
        int p = 0;
        ReadU16(data, ref p);                       // version (reserved, 0x0000)
        ReadU16(data, ref p);                       // optionFlags
        int numGlyphs = ReadU16(data, ref p);
        ReadU16(data, ref p);                       // indexFormat (we emit long)
        int nContourSize = (int)ReadU32(data, ref p);
        int nPointsSize = (int)ReadU32(data, ref p);
        int flagSize = (int)ReadU32(data, ref p);
        int glyphSize = (int)ReadU32(data, ref p);
        int compositeSize = (int)ReadU32(data, ref p);
        int bboxSize = (int)ReadU32(data, ref p);
        int instructionSize = (int)ReadU32(data, ref p);

        int off = GlyfTransformHeaderSize;
        byte[] nContourStream = Slice(data, ref off, nContourSize);
        byte[] nPointsStream = Slice(data, ref off, nPointsSize);
        byte[] flagStream = Slice(data, ref off, flagSize);
        byte[] glyphStream = Slice(data, ref off, glyphSize);
        byte[] compositeStream = Slice(data, ref off, compositeSize);
        byte[] bboxStreamFull = Slice(data, ref off, bboxSize);
        byte[] instructionStream = Slice(data, ref off, instructionSize);

        // bboxStream = bboxBitmap (one bit per glyph, padded to 4 bytes) + values.
        int bboxBitmapSize = ((numGlyphs + 31) >> 5) << 2;
        byte[] bboxBitmap = new byte[bboxBitmapSize];
        Array.Copy(bboxStreamFull, 0, bboxBitmap, 0, bboxBitmapSize);

        int nContourPos = 0;
        int nPointsPos = 0;
        int flagPos = 0;
        int glyphPos = 0;
        int compositePos = 0;
        int bboxPos = bboxBitmapSize;     // bbox values begin after the bitmap
        int instrPos = 0;

        List<byte[]> glyphs = new List<byte[]>(numGlyphs);
        for (int gid = 0; gid < numGlyphs; gid++)
        {
            short nContours = ReadI16(nContourStream, ref nContourPos);
            bool hasBBox = (bboxBitmap[gid >> 3] & (0x80 >> (gid & 7))) != 0;

            if (nContours == 0)
            {
                glyphs.Add(Array.Empty<byte>());
                continue;
            }

            if (nContours > 0)
            {
                glyphs.Add(ReconstructSimpleGlyph(
                    nContours, hasBBox,
                    nPointsStream, ref nPointsPos,
                    flagStream, ref flagPos,
                    glyphStream, ref glyphPos,
                    bboxStreamFull, ref bboxPos,
                    instructionStream, ref instrPos));
            }
            else
            {
                glyphs.Add(ReconstructCompositeGlyph(
                    compositeStream, ref compositePos,
                    glyphStream, ref glyphPos,
                    bboxStreamFull, ref bboxPos,
                    instructionStream, ref instrPos));
            }
        }

        // Build glyf table (2-byte aligned entries) and a long-format loca.
        using MemoryStream glyfMs = new MemoryStream();
        uint[] offsets = new uint[numGlyphs + 1];
        for (int i = 0; i < numGlyphs; i++)
        {
            offsets[i] = (uint)glyfMs.Length;
            glyfMs.Write(glyphs[i], 0, glyphs[i].Length);
            if ((glyphs[i].Length & 1) != 0)
            {
                glyfMs.WriteByte(0);
            }
        }

        offsets[numGlyphs] = (uint)glyfMs.Length;

        byte[] locaData = new byte[(numGlyphs + 1) * 4];
        for (int i = 0; i <= numGlyphs; i++)
        {
            WriteU32(locaData, i * 4, offsets[i]);
        }

        return (glyfMs.ToArray(), locaData);
    }

    private static byte[] ReconstructSimpleGlyph(
        int nContours, bool hasBBox,
        byte[] nPointsStream, ref int nPointsPos,
        byte[] flagStream, ref int flagPos,
        byte[] glyphStream, ref int glyphPos,
        byte[] bboxStream, ref int bboxPos,
        byte[] instructionStream, ref int instrPos)
    {
        int[] endPts = new int[nContours];
        int nPoints = 0;
        for (int c = 0; c < nContours; c++)
        {
            int count = Read255UShort(nPointsStream, ref nPointsPos);
            nPoints += count;
            endPts[c] = nPoints - 1;
        }

        int[] xs = new int[nPoints];
        int[] ys = new int[nPoints];
        bool[] onCurve = new bool[nPoints];
        int x = 0;
        int y = 0;
        for (int i = 0; i < nPoints; i++)
        {
            int flag = flagStream[flagPos++];
            onCurve[i] = (flag >> 7) == 0;
            flag &= 0x7F;

            int dx;
            int dy;
            if (flag < 10)
            {
                dx = 0;
                dy = WithSign(flag, ((flag & 14) << 7) + glyphStream[glyphPos]);
                glyphPos += 1;
            }
            else if (flag < 20)
            {
                dx = WithSign(flag, (((flag - 10) & 14) << 7) + glyphStream[glyphPos]);
                dy = 0;
                glyphPos += 1;
            }
            else if (flag < 84)
            {
                int b0 = flag - 20;
                int b1 = glyphStream[glyphPos];
                dx = WithSign(flag, 1 + (b0 & 0x30) + (b1 >> 4));
                dy = WithSign(flag >> 1, 1 + ((b0 & 0x0C) << 2) + (b1 & 0x0F));
                glyphPos += 1;
            }
            else if (flag < 120)
            {
                int b0 = flag - 84;
                dx = WithSign(flag, 1 + ((b0 / 12) << 8) + glyphStream[glyphPos]);
                dy = WithSign(flag >> 1, 1 + (((b0 % 12) >> 2) << 8) + glyphStream[glyphPos + 1]);
                glyphPos += 2;
            }
            else if (flag < 124)
            {
                int b1 = glyphStream[glyphPos + 1];
                dx = WithSign(flag, (glyphStream[glyphPos] << 4) + (b1 >> 4));
                dy = WithSign(flag >> 1, ((b1 & 0x0F) << 8) + glyphStream[glyphPos + 2]);
                glyphPos += 3;
            }
            else
            {
                dx = WithSign(flag, (glyphStream[glyphPos] << 8) + glyphStream[glyphPos + 1]);
                dy = WithSign(flag >> 1, (glyphStream[glyphPos + 2] << 8) + glyphStream[glyphPos + 3]);
                glyphPos += 4;
            }

            x += dx;
            y += dy;
            xs[i] = x;
            ys[i] = y;
        }

        int instructionLength = Read255UShort(glyphStream, ref glyphPos);
        byte[] instructions = Slice(instructionStream, ref instrPos, instructionLength);

        int xMin;
        int yMin;
        int xMax;
        int yMax;
        if (hasBBox)
        {
            xMin = ReadI16(bboxStream, ref bboxPos);
            yMin = ReadI16(bboxStream, ref bboxPos);
            xMax = ReadI16(bboxStream, ref bboxPos);
            yMax = ReadI16(bboxStream, ref bboxPos);
        }
        else
        {
            xMin = int.MaxValue;
            yMin = int.MaxValue;
            xMax = int.MinValue;
            yMax = int.MinValue;
            for (int i = 0; i < nPoints; i++)
            {
                if (xs[i] < xMin) { xMin = xs[i]; }
                if (ys[i] < yMin) { yMin = ys[i]; }
                if (xs[i] > xMax) { xMax = xs[i]; }
                if (ys[i] > yMax) { yMax = ys[i]; }
            }
        }

        using MemoryStream ms = new MemoryStream();
        WriteI16(ms, (short)nContours);
        WriteI16(ms, (short)xMin);
        WriteI16(ms, (short)yMin);
        WriteI16(ms, (short)xMax);
        WriteI16(ms, (short)yMax);
        for (int c = 0; c < nContours; c++)
        {
            WriteU16(ms, endPts[c]);
        }

        WriteU16(ms, instructionLength);
        ms.Write(instructions, 0, instructions.Length);

        // Long form: one flag byte per point (on-curve bit only), then 16-bit
        // signed x deltas, then 16-bit signed y deltas. Always valid.
        for (int i = 0; i < nPoints; i++)
        {
            ms.WriteByte((byte)(onCurve[i] ? 0x01 : 0x00));
        }

        int prev = 0;
        for (int i = 0; i < nPoints; i++)
        {
            WriteI16(ms, (short)(xs[i] - prev));
            prev = xs[i];
        }

        prev = 0;
        for (int i = 0; i < nPoints; i++)
        {
            WriteI16(ms, (short)(ys[i] - prev));
            prev = ys[i];
        }

        return ms.ToArray();
    }

    private static byte[] ReconstructCompositeGlyph(
        byte[] compositeStream, ref int compositePos,
        byte[] glyphStream, ref int glyphPos,
        byte[] bboxStream, ref int bboxPos,
        byte[] instructionStream, ref int instrPos)
    {
        // Composite glyphs always carry an explicit bbox.
        int xMin = ReadI16(bboxStream, ref bboxPos);
        int yMin = ReadI16(bboxStream, ref bboxPos);
        int xMax = ReadI16(bboxStream, ref bboxPos);
        int yMax = ReadI16(bboxStream, ref bboxPos);

        int start = compositePos;
        bool haveInstructions = false;
        bool more = true;
        while (more)
        {
            int flags = ReadU16(compositeStream, ref compositePos);
            ReadU16(compositeStream, ref compositePos); // glyphIndex
            compositePos += (flags & ArgsAreWords) != 0 ? 4 : 2;
            if ((flags & WeHaveAScale) != 0)
            {
                compositePos += 2;
            }
            else if ((flags & WeHaveXandYScale) != 0)
            {
                compositePos += 4;
            }
            else if ((flags & WeHaveTwoByTwo) != 0)
            {
                compositePos += 8;
            }

            haveInstructions |= (flags & WeHaveInstructions) != 0;
            more = (flags & MoreComponents) != 0;
        }

        int componentBytes = compositePos - start;

        using MemoryStream ms = new MemoryStream();
        WriteI16(ms, -1);
        WriteI16(ms, (short)xMin);
        WriteI16(ms, (short)yMin);
        WriteI16(ms, (short)xMax);
        WriteI16(ms, (short)yMax);
        ms.Write(compositeStream, start, componentBytes);

        if (haveInstructions)
        {
            int instructionLength = Read255UShort(glyphStream, ref glyphPos);
            byte[] instructions = Slice(instructionStream, ref instrPos, instructionLength);
            WriteU16(ms, instructionLength);
            ms.Write(instructions, 0, instructions.Length);
        }

        return ms.ToArray();
    }

    // ── sfnt reassembly ──────────────────────────────────────────────────────

    private static byte[] AssembleSfnt(uint flavor, List<TableEntry> tables)
    {
        // head.indexToLocFormat must say "long" since we emit a long loca.
        TableEntry? head = tables.Find(t => t.Tag == HeadTag);
        if (head is not null && head.Data.Length >= 52)
        {
            WriteU16(head.Data, 50, 1);
        }

        tables.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        int numTables = tables.Count;

        int entrySelector = 0;
        int searchRange = 16;
        while (searchRange * 2 <= numTables * 16)
        {
            searchRange *= 2;
            entrySelector++;
        }

        int rangeShift = numTables * 16 - searchRange;

        int directorySize = 12 + numTables * 16;
        int offset = directorySize;
        foreach (TableEntry t in tables)
        {
            offset += Align4(t.Data.Length);
        }

        byte[] sfnt = new byte[offset];
        int p = 0;
        WriteU32(sfnt, p, flavor); p += 4;
        WriteU16(sfnt, p, numTables); p += 2;
        WriteU16(sfnt, p, searchRange); p += 2;
        WriteU16(sfnt, p, entrySelector); p += 2;
        WriteU16(sfnt, p, rangeShift); p += 2;

        int dataOffset = directorySize;
        foreach (TableEntry t in tables)
        {
            uint checksum = TableChecksum(t.Data);
            WriteU32(sfnt, p, t.Tag); p += 4;
            WriteU32(sfnt, p, checksum); p += 4;
            WriteU32(sfnt, p, (uint)dataOffset); p += 4;
            WriteU32(sfnt, p, (uint)t.Data.Length); p += 4;

            Array.Copy(t.Data, 0, sfnt, dataOffset, t.Data.Length);
            dataOffset += Align4(t.Data.Length);
        }

        // head.checkSumAdjustment = 0xB1B0AFBA - checksum(entire file).
        if (head is not null)
        {
            int headOffset = FindTableOffset(sfnt, HeadTag);
            if (headOffset >= 0 && headOffset + 12 <= sfnt.Length)
            {
                WriteU32(sfnt, headOffset + 8, 0);
                uint fileChecksum = TableChecksum(sfnt);
                WriteU32(sfnt, headOffset + 8, 0xB1B0AFBA - fileChecksum);
            }
        }

        return sfnt;
    }

    private static int FindTableOffset(byte[] sfnt, uint tag)
    {
        int numTables = ReadU16At(sfnt, 4);
        int dir = 12;
        for (int i = 0; i < numTables; i++)
        {
            uint t = ReadU32At(sfnt, dir);
            if (t == tag)
            {
                return (int)ReadU32At(sfnt, dir + 8);
            }

            dir += 16;
        }

        return -1;
    }

    private static uint TableChecksum(byte[] data)
    {
        uint sum = 0;
        int n = data.Length;
        int i = 0;
        for (; i + 4 <= n; i += 4)
        {
            sum += ReadU32At(data, i);
        }

        if (i < n)
        {
            uint last = 0;
            for (int k = 0; k < 4; k++)
            {
                last <<= 8;
                if (i + k < n)
                {
                    last |= data[i + k];
                }
            }

            sum += last;
        }

        return sum;
    }

    // ── primitives ───────────────────────────────────────────────────────────

    private static int WithSign(int flag, int baseval) => (flag & 1) != 0 ? baseval : -baseval;

    private static byte[] BrotliDecompress(byte[] compressed)
    {
        using MemoryStream input = new MemoryStream(compressed);
        using BrotliStream brotli = new BrotliStream(input, CompressionMode.Decompress);
        using MemoryStream output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }

    private static int ReadBase128(byte[] data, ref int p)
    {
        int result = 0;
        for (int i = 0; i < 5; i++)
        {
            int b = data[p++];
            if (i == 0 && b == 0x80)
            {
                throw new InvalidDataException("UIntBase128 with leading zero.");
            }

            if ((result & 0xFE000000) != 0)
            {
                throw new InvalidDataException("UIntBase128 overflow.");
            }

            result = (result << 7) | (b & 0x7f);
            if ((b & 0x80) == 0)
            {
                return result;
            }
        }

        throw new InvalidDataException("UIntBase128 too long.");
    }

    private static int Read255UShort(byte[] data, ref int p)
    {
        int code = data[p++];
        if (code == 253)
        {
            int hi = data[p++];
            int lo = data[p++];
            return (hi << 8) | lo;
        }

        if (code == 254)
        {
            return data[p++] + 253 * 2;
        }

        if (code == 255)
        {
            return data[p++] + 253;
        }

        return code;
    }

    private static byte[] Slice(byte[] data, ref int p, int length)
    {
        byte[] result = new byte[length];
        Array.Copy(data, p, result, 0, length);
        p += length;
        return result;
    }

    private static int Align4(int n) => (n + 3) & ~3;

    private static uint Tag(string s)
        => ((uint)s[0] << 24) | ((uint)s[1] << 16) | ((uint)s[2] << 8) | s[3];

    private static ushort ReadU16(byte[] data, ref int p)
    {
        ushort v = (ushort)((data[p] << 8) | data[p + 1]);
        p += 2;
        return v;
    }

    private static short ReadI16(byte[] data, ref int p) => (short)ReadU16(data, ref p);

    private static uint ReadU32(byte[] data, ref int p)
    {
        uint v = ((uint)data[p] << 24) | ((uint)data[p + 1] << 16) | ((uint)data[p + 2] << 8) | data[p + 3];
        p += 4;
        return v;
    }

    private static int ReadU16At(byte[] data, int p) => (data[p] << 8) | data[p + 1];

    private static uint ReadU32At(byte[] data, int p)
        => ((uint)data[p] << 24) | ((uint)data[p + 1] << 16) | ((uint)data[p + 2] << 8) | data[p + 3];

    private static void WriteU16(byte[] data, int p, int v)
    {
        data[p] = (byte)((v >> 8) & 0xff);
        data[p + 1] = (byte)(v & 0xff);
    }

    private static void WriteU32(byte[] data, int p, uint v)
    {
        data[p] = (byte)((v >> 24) & 0xff);
        data[p + 1] = (byte)((v >> 16) & 0xff);
        data[p + 2] = (byte)((v >> 8) & 0xff);
        data[p + 3] = (byte)(v & 0xff);
    }

    private static void WriteU16(Stream s, int v)
    {
        s.WriteByte((byte)((v >> 8) & 0xff));
        s.WriteByte((byte)(v & 0xff));
    }

    private static void WriteI16(Stream s, short v) => WriteU16(s, (ushort)v);
}
