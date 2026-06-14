// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Apple TrueType reference — sfnt directory, head/maxp/hhea/hmtx/loca/glyf.

using System;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Produces a reduced TrueType font program containing only the glyphs needed
/// to render a given set of glyph ids. Glyph numbering is preserved (used
/// glyphs keep their original ids; unused glyphs become empty), so an
/// Identity CID-to-GID mapping still applies. Non-rendering tables (cmap, post,
/// name, OS/2, GSUB/GPOS/GDEF, …) are dropped — for a CIDFontType2 with an
/// Identity CID-to-GID map the viewer never consults them, and the layout
/// tables in particular are large in complex-script fonts.
/// </summary>
internal static class TrueTypeSubsetter
{
    /// <summary>
    /// Returns a subset of <paramref name="font"/> retaining
    /// <paramref name="usedGlyphs"/> (plus glyph 0 and any composite components).
    /// </summary>
    public static byte[] Subset(byte[] font, IReadOnlyCollection<int> usedGlyphs)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(usedGlyphs);

        Dictionary<string, TableEntry> dir = ReadTableDirectory(font);
        if (!dir.TryGetValue("glyf", out TableEntry glyfEntry) ||
            !dir.TryGetValue("loca", out TableEntry locaEntry) ||
            !dir.TryGetValue("head", out TableEntry headEntry) ||
            !dir.TryGetValue("maxp", out TableEntry maxpEntry) ||
            !dir.TryGetValue("hhea", out TableEntry hheaEntry) ||
            !dir.TryGetValue("hmtx", out TableEntry hmtxEntry))
        {
            // Not a glyf-based font (or missing required tables) — embed as-is.
            return font;
        }

        bool longLoca = ReadU16(font, headEntry.Offset + 50) == 1;
        int numGlyphs = ReadU16(font, maxpEntry.Offset + 4);
        int[] loca = ReadLoca(font, locaEntry.Offset, numGlyphs, longLoca);

        SortedSet<int> keep = CloseOverComposites(font, glyfEntry.Offset, loca, numGlyphs, usedGlyphs);
        int maxGid = 0;
        foreach (int g in keep)
        {
            if (g > maxGid)
            {
                maxGid = g;
            }
        }

        int newNumGlyphs = maxGid + 1;

        // Rebuild glyf + loca (long format) for glyphs 0..maxGid.
        byte[] newGlyf;
        int[] newLoca = new int[newNumGlyphs + 1];
        using (System.IO.MemoryStream glyfOut = new System.IO.MemoryStream())
        {
            for (int gid = 0; gid < newNumGlyphs; gid++)
            {
                newLoca[gid] = (int)glyfOut.Length;
                if (keep.Contains(gid))
                {
                    int start = glyfEntry.Offset + loca[gid];
                    int len = loca[gid + 1] - loca[gid];
                    if (len > 0)
                    {
                        glyfOut.Write(font, start, len);
                        while ((glyfOut.Length & 1) != 0)
                        {
                            glyfOut.WriteByte(0); // pad to even
                        }
                    }
                }
            }

            newLoca[newNumGlyphs] = (int)glyfOut.Length;
            newGlyf = glyfOut.ToArray();
        }

        byte[] newLocaBytes = WriteLongLoca(newLoca);

        // hmtx: emit a full long-form table (advance + lsb) for 0..maxGid.
        int origNumHMetrics = ReadU16(font, hheaEntry.Offset + 34);
        byte[] newHmtx = RebuildHmtx(font, hmtxEntry.Offset, origNumHMetrics, newNumGlyphs);

        // Copy + patch head (indexToLocFormat = long), maxp (numGlyphs),
        // hhea (numberOfHMetrics).
        byte[] newHead = Slice(font, headEntry);
        WriteU16(newHead, 50, 1);
        WriteU32(newHead, 8, 0); // checkSumAdjustment recomputed after assembly

        byte[] newMaxp = Slice(font, maxpEntry);
        WriteU16(newMaxp, 4, newNumGlyphs);

        byte[] newHhea = Slice(font, hheaEntry);
        WriteU16(newHhea, 34, newNumGlyphs);

        Dictionary<string, byte[]> tables = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["head"] = newHead,
            ["hhea"] = newHhea,
            ["maxp"] = newMaxp,
            ["hmtx"] = newHmtx,
            ["loca"] = newLocaBytes,
            ["glyf"] = newGlyf,
        };

        foreach (string optional in new[] { "cvt ", "fpgm", "prep", "gasp" })
        {
            if (dir.TryGetValue(optional, out TableEntry e))
            {
                tables[optional] = Slice(font, e);
            }
        }

        return Assemble(tables);
    }

    // ── Glyph closure ─────────────────────────────────────────────────────

    private static SortedSet<int> CloseOverComposites(
        byte[] font, int glyfBase, int[] loca, int numGlyphs, IReadOnlyCollection<int> seed)
    {
        SortedSet<int> keep = new SortedSet<int> { 0 };
        Queue<int> work = new Queue<int>();
        foreach (int g in seed)
        {
            if (g > 0 && g < numGlyphs && keep.Add(g))
            {
                work.Enqueue(g);
            }
        }

        work.Enqueue(0);

        while (work.Count > 0)
        {
            int gid = work.Dequeue();
            int start = glyfBase + loca[gid];
            int len = loca[gid + 1] - loca[gid];
            if (len < 10)
            {
                continue; // empty glyph
            }

            short contours = (short)ReadU16(font, start);
            if (contours >= 0)
            {
                continue; // simple glyph — no components
            }

            // Composite: walk components, collecting referenced glyph ids.
            int p = start + 10;
            while (true)
            {
                int flags = ReadU16(font, p);
                int component = ReadU16(font, p + 2);
                p += 4;

                if (component < numGlyphs && keep.Add(component))
                {
                    work.Enqueue(component);
                }

                p += (flags & 0x0001) != 0 ? 4 : 2;        // ARG_1_AND_2_ARE_WORDS
                if ((flags & 0x0008) != 0)
                {
                    p += 2;                                 // WE_HAVE_A_SCALE
                }
                else if ((flags & 0x0040) != 0)
                {
                    p += 4;                                 // X_AND_Y_SCALE
                }
                else if ((flags & 0x0080) != 0)
                {
                    p += 8;                                 // 2x2 transform
                }

                if ((flags & 0x0020) == 0)
                {
                    break;                                  // no MORE_COMPONENTS
                }
            }
        }

        return keep;
    }

    // ── Table I/O ─────────────────────────────────────────────────────────

    private static byte[] RebuildHmtx(byte[] font, int hmtxOffset, int origNumHMetrics, int count)
    {
        byte[] result = new byte[count * 4];
        int lastAdvance = origNumHMetrics > 0 ? ReadU16(font, hmtxOffset) : 0;
        for (int gid = 0; gid < count; gid++)
        {
            int advance;
            int lsb;
            if (gid < origNumHMetrics)
            {
                advance = ReadU16(font, hmtxOffset + (gid * 4));
                lsb = ReadU16(font, hmtxOffset + (gid * 4) + 2);
                lastAdvance = advance;
            }
            else
            {
                advance = lastAdvance;
                int tail = hmtxOffset + (origNumHMetrics * 4) + ((gid - origNumHMetrics) * 2);
                lsb = tail + 1 < font.Length ? ReadU16(font, tail) : 0;
            }

            WriteU16(result, gid * 4, advance);
            WriteU16(result, (gid * 4) + 2, lsb);
        }

        return result;
    }

    private static int[] ReadLoca(byte[] font, int offset, int numGlyphs, bool longFormat)
    {
        int[] loca = new int[numGlyphs + 1];
        for (int i = 0; i <= numGlyphs; i++)
        {
            loca[i] = longFormat
                ? (int)ReadU32(font, offset + (i * 4))
                : ReadU16(font, offset + (i * 2)) * 2;
        }

        return loca;
    }

    private static byte[] WriteLongLoca(int[] loca)
    {
        byte[] bytes = new byte[loca.Length * 4];
        for (int i = 0; i < loca.Length; i++)
        {
            WriteU32(bytes, i * 4, (uint)loca[i]);
        }

        return bytes;
    }

    private static byte[] Assemble(Dictionary<string, byte[]> tables)
    {
        List<string> tags = new List<string>(tables.Keys);
        tags.Sort(StringComparer.Ordinal);
        int n = tags.Count;

        int headerLen = 12 + (n * 16);
        int offset = headerLen;
        Dictionary<string, int> offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string tag in tags)
        {
            offsets[tag] = offset;
            offset += Align4(tables[tag].Length);
        }

        byte[] output = new byte[offset];

        // Offset table.
        WriteU32(output, 0, 0x00010000);
        WriteU16(output, 4, n);
        int entrySelector = (int)Math.Floor(Math.Log2(n));
        int searchRange = (1 << entrySelector) * 16;
        WriteU16(output, 6, searchRange);
        WriteU16(output, 8, entrySelector);
        WriteU16(output, 10, (n * 16) - searchRange);

        int dirPos = 12;
        foreach (string tag in tags)
        {
            byte[] data = tables[tag];
            Encoding.ASCII.GetBytes(tag, 0, 4, output, dirPos);
            WriteU32(output, dirPos + 4, TableChecksum(data));
            WriteU32(output, dirPos + 8, (uint)offsets[tag]);
            WriteU32(output, dirPos + 12, (uint)data.Length);
            Array.Copy(data, 0, output, offsets[tag], data.Length);
            dirPos += 16;
        }

        // checkSumAdjustment in head.
        if (offsets.TryGetValue("head", out int headOff))
        {
            uint total = TableChecksum(output);
            WriteU32(output, headOff + 8, unchecked(0xB1B0AFBA - total));
        }

        return output;
    }

    private static uint TableChecksum(byte[] data)
    {
        uint sum = 0;
        int i = 0;
        for (; i + 3 < data.Length; i += 4)
        {
            sum = unchecked(sum + ReadU32(data, i));
        }

        if (i < data.Length)
        {
            uint tail = 0;
            for (int b = 0; b < 4; b++)
            {
                tail = (tail << 8) | (uint)(i + b < data.Length ? data[i + b] : 0);
            }

            sum = unchecked(sum + tail);
        }

        return sum;
    }

    private static Dictionary<string, TableEntry> ReadTableDirectory(byte[] font)
    {
        Dictionary<string, TableEntry> dir = new Dictionary<string, TableEntry>(StringComparer.Ordinal);
        int numTables = ReadU16(font, 4);
        int rec = 12;
        for (int i = 0; i < numTables; i++)
        {
            string tag = Encoding.ASCII.GetString(font, rec, 4);
            dir[tag] = new TableEntry((int)ReadU32(font, rec + 8), (int)ReadU32(font, rec + 12));
            rec += 16;
        }

        return dir;
    }

    private static byte[] Slice(byte[] font, TableEntry e)
    {
        byte[] copy = new byte[e.Length];
        Array.Copy(font, e.Offset, copy, 0, e.Length);
        return copy;
    }

    private static int Align4(int n) => (n + 3) & ~3;

    private static int ReadU16(byte[] b, int o) => (b[o] << 8) | b[o + 1];

    private static uint ReadU32(byte[] b, int o) =>
        ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

    private static void WriteU16(byte[] b, int o, int v)
    {
        b[o] = (byte)((v >> 8) & 0xFF);
        b[o + 1] = (byte)(v & 0xFF);
    }

    private static void WriteU32(byte[] b, int o, uint v)
    {
        b[o] = (byte)((v >> 24) & 0xFF);
        b[o + 1] = (byte)((v >> 16) & 0xFF);
        b[o + 2] = (byte)((v >> 8) & 0xFF);
        b[o + 3] = (byte)(v & 0xFF);
    }

    private readonly struct TableEntry
    {
        public TableEntry(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public int Offset { get; }

        public int Length { get; }
    }
}
