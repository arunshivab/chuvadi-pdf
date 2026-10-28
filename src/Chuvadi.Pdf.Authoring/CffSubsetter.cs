// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Adobe Technical Note #5176 — The Compact Font Format Specification
//        §8 INDEX, §4 DICT data, §13 Charsets, §19 FDSelect, §18 CID-keyed fonts
//        PDF 32000-1:2008 §9.7.4.2 (CIDFontType0), §9.9 (FontFile3 /CIDFontType0C)

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Subsets a CFF font program (the <c>CFF </c> table of an OpenType font) and
/// rewrites it as a CID-keyed CFF for embedding as a <c>CIDFontType0</c> font
/// (<c>/FontFile3 /Subtype /CIDFontType0C</c>).
/// </summary>
/// <remarks>
/// <para>
/// Only the used glyphs are written, and the charset gives each its original
/// glyph id as its CID, so content streams keep using the source font's glyph
/// ids as CIDs (Identity-H). The output always has the
/// registry-ordering-supplement Adobe-Identity-0.
/// </para>
/// <para>
/// A name-keyed source becomes a CID font with one font dictionary holding the
/// original Private DICT; a CID-keyed source keeps its font dictionaries and
/// FDSelect. Subroutines keep their numbering (charstrings call them by
/// index); those no kept glyph calls are emptied to a bare <c>return</c>.
/// </para>
/// </remarks>
internal static class CffSubsetter
{
    // DICT operators (two-byte operators are 0x0C00 | second byte).
    private const int OpUniqueId = 13;
    private const int OpXuid = 14;
    private const int OpCharset = 15;
    private const int OpEncoding = 16;
    private const int OpCharStrings = 17;
    private const int OpPrivate = 18;
    private const int OpSubrs = 19;
    private const int OpSyntheticBase = 0x0C14;
    private const int OpRos = 0x0C1E;
    private const int OpCidFontVersion = 0x0C1F;
    private const int OpCidFontRevision = 0x0C20;
    private const int OpCidFontType = 0x0C21;
    private const int OpCidCount = 0x0C22;
    private const int OpUidBase = 0x0C23;
    private const int OpFdArray = 0x0C24;
    private const int OpFdSelect = 0x0C25;

    private const int StandardStrings = 391;

    /// <summary>
    /// Returns the CFF table of an OpenType font, or null when it has none.
    /// </summary>
    internal static byte[]? FindCffTable(byte[] sfnt)
    {
        ArgumentNullException.ThrowIfNull(sfnt);
        if (sfnt.Length < 12)
        {
            return null;
        }

        int numTables = U16(sfnt, 4);
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + (i * 16);
            if (record + 16 > sfnt.Length)
            {
                return null;
            }

            if (sfnt[record] == (byte)'C' && sfnt[record + 1] == (byte)'F' && sfnt[record + 2] == (byte)'F' && sfnt[record + 3] == (byte)' ')
            {
                int offset = (int)U32(sfnt, record + 8);
                int length = (int)U32(sfnt, record + 12);
                if (offset < 0 || length <= 0 || offset > sfnt.Length - length)
                {
                    return null;
                }

                return sfnt.AsSpan(offset, length).ToArray();
            }
        }

        return null;
    }

    /// <summary>
    /// Subsets <paramref name="cff"/> to <paramref name="usedGlyphs"/> (glyph 0
    /// is always kept) and returns a CID-keyed CFF program.
    /// </summary>
    /// <exception cref="InvalidDataException">The CFF data is malformed or not CFF version 1.</exception>
    internal static byte[] Subset(byte[] cff, IReadOnlySet<int> usedGlyphs)
    {
        ArgumentNullException.ThrowIfNull(cff);
        ArgumentNullException.ThrowIfNull(usedGlyphs);
        try
        {
            return SubsetCore(cff, usedGlyphs);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException)
        {
            throw new InvalidDataException("The CFF font program is truncated or inconsistent.", ex);
        }
    }

    private static byte[] SubsetCore(byte[] cff, IReadOnlySet<int> usedGlyphs)
    {
        if (cff.Length < 4 || cff[0] != 1)
        {
            throw new InvalidDataException("Only CFF version 1 font programs can be embedded.");
        }

        // ── Parse the source ────────────────────────────────────────────────
        int hdrSize = cff[2];
        Index nameIndex = Index.Read(cff, hdrSize);
        Index topIndex = Index.Read(cff, nameIndex.End);
        Index stringIndex = Index.Read(cff, topIndex.End);
        Index gsubrIndex = Index.Read(cff, stringIndex.End);
        if (topIndex.Count < 1 || nameIndex.Count < 1)
        {
            throw new InvalidDataException("The CFF font program has no font.");
        }

        List<DictEntry> top = ParseDict(cff, topIndex.Start(0), topIndex.Length(0));
        DictEntry charStringsEntry = Find(top, OpCharStrings)
            ?? throw new InvalidDataException("The CFF font has no CharStrings.");
        Index charStrings = Index.Read(cff, (int)charStringsEntry.Values[0]);
        int numGlyphs = charStrings.Count;
        if (numGlyphs < 1)
        {
            throw new InvalidDataException("The CFF font has no glyphs.");
        }

        bool isCid = Find(top, OpRos) is not null;

        // Font dictionaries: (kept raw entries, private dict bytes without
        // Subrs, local subr INDEX bytes or null).
        List<FontDict> fontDicts = new List<FontDict>();
        byte[] fdOfGlyph = new byte[numGlyphs];
        if (isCid)
        {
            DictEntry fdArrayEntry = Find(top, OpFdArray)
                ?? throw new InvalidDataException("The CID-keyed CFF font has no FDArray.");
            DictEntry fdSelectEntry = Find(top, OpFdSelect)
                ?? throw new InvalidDataException("The CID-keyed CFF font has no FDSelect.");
            Index fdArray = Index.Read(cff, (int)fdArrayEntry.Values[0]);
            if (fdArray.Count < 1 || fdArray.Count > 255)
            {
                throw new InvalidDataException("The CFF FDArray is empty or too large.");
            }

            for (int i = 0; i < fdArray.Count; i++)
            {
                List<DictEntry> fd = ParseDict(cff, fdArray.Start(i), fdArray.Length(i));
                fontDicts.Add(ReadFontDict(cff, fd));
            }

            ReadFdSelect(cff, (int)fdSelectEntry.Values[0], numGlyphs, fdArray.Count, fdOfGlyph);
        }
        else
        {
            fontDicts.Add(ReadFontDict(cff, top, keepEntries: false));
        }

        // ── Build the pieces whose size does not depend on offsets ──────────
        byte[] header = { 1, 0, 4, 4 };
        byte[] nameIndexBytes = cff.AsSpan(hdrSize, nameIndex.End - hdrSize).ToArray();

        List<byte[]> strings = new List<byte[]>(stringIndex.Count + 2);
        for (int i = 0; i < stringIndex.Count; i++)
        {
            strings.Add(cff.AsSpan(stringIndex.Start(i), stringIndex.Length(i)).ToArray());
        }

        int sidAdobe = StandardStrings + strings.Count;
        strings.Add(Encoding.ASCII.GetBytes("Adobe"));
        int sidIdentity = StandardStrings + strings.Count;
        strings.Add(Encoding.ASCII.GetBytes("Identity"));
        byte[] stringIndexBytes = Index.Build(strings);

        // Subroutines: find the ones the kept glyphs call and empty the rest
        // (their numbering must stay, so they become a bare "return"). When a
        // charstring cannot be followed safely, every subroutine is kept.
        SubrUsage usage = SubrUsage.Find(cff, charStrings, gsubrIndex, fontDicts, fdOfGlyph, usedGlyphs);
        byte[] gsubrBytes = usage.Complete
            ? usage.PruneGlobal(cff, gsubrIndex)
            : cff.AsSpan(stringIndex.End, gsubrIndex.End - stringIndex.End).ToArray();
        if (usage.Complete)
        {
            for (int i = 0; i < fontDicts.Count; i++)
            {
                fontDicts[i] = fontDicts[i].WithLocalSubrs(usage.PruneLocal(fontDicts[i].LocalSubrs, i));
            }
        }

        // Top DICT: ROS first (required for CID fonts), the source's
        // descriptive entries, then CIDCount and the four offsets as fixed
        // five-byte integers patched once the layout is known.
        MemoryStream topDict = new MemoryStream();
        WriteInt(topDict, sidAdobe);
        WriteInt(topDict, sidIdentity);
        WriteInt(topDict, 0);
        WriteOp(topDict, OpRos);
        foreach (DictEntry e in top)
        {
            if (!IsStructuralTopOp(e.Op))
            {
                topDict.Write(cff, e.RawStart, e.RawEnd - e.RawStart);
            }
        }

        WriteInt(topDict, numGlyphs);
        WriteOp(topDict, OpCidCount);
        int charsetPatch = (int)topDict.Length;
        WriteFixedInt(topDict, 0);
        WriteOp(topDict, OpCharset);
        int fdSelectPatch = (int)topDict.Length;
        WriteFixedInt(topDict, 0);
        WriteOp(topDict, OpFdSelect);
        int fdArrayPatch = (int)topDict.Length;
        WriteFixedInt(topDict, 0);
        WriteOp(topDict, OpFdArray);
        int charStringsPatch = (int)topDict.Length;
        WriteFixedInt(topDict, 0);
        WriteOp(topDict, OpCharStrings);
        byte[] topDictBytes = topDict.ToArray();
        int topIndexLength = Index.Build(new List<byte[]> { topDictBytes }).Length;

        // Only the kept glyphs are written; the charset gives each its CID,
        // which is its glyph id in the source font.
        List<int> kept = new List<int> { 0 };
        for (int gid = 1; gid < numGlyphs; gid++)
        {
            if (usedGlyphs.Contains(gid))
            {
                kept.Add(gid);
            }
        }

        byte[] charset = BuildCharset(kept);
        byte[] keptFds = new byte[kept.Count];
        List<byte[]> glyphs = new List<byte[]>(kept.Count);
        for (int k = 0; k < kept.Count; k++)
        {
            int gid = kept[k];
            keptFds[k] = fdOfGlyph[gid];
            glyphs.Add(cff.AsSpan(charStrings.Start(gid), charStrings.Length(gid)).ToArray());
        }

        byte[] fdSelect = BuildFdSelect(keptFds);
        byte[] charStringsBytes = Index.Build(glyphs);

        // ── Layout ──────────────────────────────────────────────────────────
        int offset = header.Length + nameIndexBytes.Length + topIndexLength + stringIndexBytes.Length + gsubrBytes.Length;
        int charsetOffset = offset;
        offset += charset.Length;
        int fdSelectOffset = offset;
        offset += fdSelect.Length;
        int charStringsOffset = offset;
        offset += charStringsBytes.Length;
        int fdArrayOffset = offset;

        // FD dicts carry Private [size offset] as fixed integers, so their
        // size is known before the private dicts are placed.
        int fdArrayLength = Index.Build(BuildFdDicts(cff, fontDicts, new int[fontDicts.Count])).Length;
        offset += fdArrayLength;
        int[] privateOffsets = new int[fontDicts.Count];
        List<byte[]> privates = new List<byte[]>(fontDicts.Count);
        for (int i = 0; i < fontDicts.Count; i++)
        {
            byte[] priv = BuildPrivate(fontDicts[i]);
            privateOffsets[i] = offset;
            privates.Add(priv);
            offset += priv.Length + (fontDicts[i].LocalSubrs?.Length ?? 0);
        }

        byte[] fdArrayBytes = Index.Build(BuildFdDicts(cff, fontDicts, privateOffsets));

        PatchFixedInt(topDictBytes, charsetPatch, charsetOffset);
        PatchFixedInt(topDictBytes, fdSelectPatch, fdSelectOffset);
        PatchFixedInt(topDictBytes, fdArrayPatch, fdArrayOffset);
        PatchFixedInt(topDictBytes, charStringsPatch, charStringsOffset);
        byte[] topIndexBytes = Index.Build(new List<byte[]> { topDictBytes });

        // ── Write ───────────────────────────────────────────────────────────
        MemoryStream output = new MemoryStream(offset);
        output.Write(header);
        output.Write(nameIndexBytes);
        output.Write(topIndexBytes);
        output.Write(stringIndexBytes);
        output.Write(gsubrBytes);
        output.Write(charset);
        output.Write(fdSelect);
        output.Write(charStringsBytes);
        output.Write(fdArrayBytes);
        for (int i = 0; i < fontDicts.Count; i++)
        {
            output.Write(privates[i]);
            if (fontDicts[i].LocalSubrs is byte[] subrs)
            {
                output.Write(subrs);
            }
        }

        return output.ToArray();
    }

    // Top DICT operators that the rewrite replaces or drops.
    private static bool IsStructuralTopOp(int op) => op switch
    {
        OpUniqueId or OpXuid or OpCharset or OpEncoding or OpCharStrings or OpPrivate
            or OpSyntheticBase or OpRos or OpCidFontVersion or OpCidFontRevision or OpCidFontType
            or OpCidCount or OpUidBase or OpFdArray or OpFdSelect => true,
        _ => false,
    };

    private static FontDict ReadFontDict(byte[] cff, List<DictEntry> dict, bool keepEntries = true)
    {
        List<(int Start, int End)> kept = new List<(int, int)>();
        if (keepEntries)
        {
            foreach (DictEntry e in dict)
            {
                if (e.Op != OpPrivate)
                {
                    kept.Add((e.RawStart, e.RawEnd));
                }
            }
        }

        DictEntry? priv = Find(dict, OpPrivate);
        if (priv is null || priv.Values.Count < 2)
        {
            // No Private DICT: give the font dictionary an empty one.
            return new FontDict(kept, new List<(int, int)>(), null);
        }

        int privSize = (int)priv.Values[0];
        int privOffset = (int)priv.Values[1];
        if (privSize < 0 || privOffset < 0 || privOffset > cff.Length - privSize)
        {
            throw new InvalidDataException("The CFF Private DICT lies outside the font.");
        }

        List<DictEntry> privEntries = ParseDict(cff, privOffset, privSize);
        List<(int, int)> privKept = new List<(int, int)>();
        byte[]? localSubrs = null;
        foreach (DictEntry e in privEntries)
        {
            if (e.Op == OpSubrs)
            {
                int subrsAt = privOffset + (int)e.Values[0];
                Index subrs = Index.Read(cff, subrsAt);
                localSubrs = cff.AsSpan(subrsAt, subrs.End - subrsAt).ToArray();
            }
            else
            {
                privKept.Add((e.RawStart, e.RawEnd));
            }
        }

        return new FontDict(kept, privKept, localSubrs) { Source = cff };
    }

    private static List<byte[]> BuildFdDicts(byte[] cff, List<FontDict> fontDicts, int[] privateOffsets)
    {
        List<byte[]> result = new List<byte[]>(fontDicts.Count);
        for (int i = 0; i < fontDicts.Count; i++)
        {
            MemoryStream fd = new MemoryStream();
            foreach ((int start, int end) in fontDicts[i].Entries)
            {
                fd.Write(cff, start, end - start);
            }

            WriteFixedInt(fd, BuildPrivate(fontDicts[i]).Length);
            WriteFixedInt(fd, privateOffsets[i]);
            WriteOp(fd, OpPrivate);
            result.Add(fd.ToArray());
        }

        return result;
    }

    private static byte[] BuildPrivate(FontDict fd)
    {
        MemoryStream priv = new MemoryStream();
        foreach ((int start, int end) in fd.PrivateEntries)
        {
            priv.Write(fd.Source!, start, end - start);
        }

        if (fd.LocalSubrs is not null)
        {
            // Local subrs follow the Private DICT directly: offset = its size.
            int size = (int)priv.Length + 6;
            WriteFixedInt(priv, size);
            WriteOp(priv, OpSubrs);
        }

        return priv.ToArray();
    }

    private static void ReadFdSelect(byte[] cff, int offset, int numGlyphs, int fdCount, byte[] fdOfGlyph)
    {
        int format = cff[offset];
        if (format == 0)
        {
            for (int gid = 0; gid < numGlyphs; gid++)
            {
                fdOfGlyph[gid] = CheckFd(cff[offset + 1 + gid], fdCount);
            }
        }
        else if (format == 3)
        {
            int nRanges = U16(cff, offset + 1);
            int pos = offset + 3;
            for (int r = 0; r < nRanges; r++)
            {
                int first = U16(cff, pos);
                byte fd = CheckFd(cff[pos + 2], fdCount);
                int next = U16(cff, pos + 3);
                for (int gid = first; gid < next && gid < numGlyphs; gid++)
                {
                    fdOfGlyph[gid] = fd;
                }

                pos += 3;
            }
        }
        else
        {
            throw new InvalidDataException($"Unsupported CFF FDSelect format {format}.");
        }
    }

    private static byte CheckFd(byte fd, int fdCount) =>
        fd < fdCount ? fd : throw new InvalidDataException("The CFF FDSelect names a font dictionary that does not exist.");

    // charset (glyph 0 is implicit): format 0 lists each CID; format 2 lists
    // runs of consecutive CIDs. Whichever is smaller is used.
    private static byte[] BuildCharset(List<int> kept)
    {
        MemoryStream format0 = new MemoryStream();
        format0.WriteByte(0);
        MemoryStream format2 = new MemoryStream();
        format2.WriteByte(2);
        int k = 1;
        while (k < kept.Count)
        {
            int first = kept[k];
            int run = 0;
            while (k + run + 1 < kept.Count && kept[k + run + 1] == first + run + 1 && run < 0xFFFF)
            {
                run++;
            }

            for (int r = 0; r <= run; r++)
            {
                format0.WriteByte((byte)((first + r) >> 8));
                format0.WriteByte((byte)(first + r));
            }

            format2.WriteByte((byte)(first >> 8));
            format2.WriteByte((byte)first);
            format2.WriteByte((byte)(run >> 8));
            format2.WriteByte((byte)run);
            k += run + 1;
        }

        return format2.Length < format0.Length ? format2.ToArray() : format0.ToArray();
    }

    // FDSelect format 3: one range per run of equal font dictionaries.
    private static byte[] BuildFdSelect(byte[] fdOfGlyph)
    {
        List<(int First, byte Fd)> ranges = new List<(int, byte)>();
        for (int gid = 0; gid < fdOfGlyph.Length; gid++)
        {
            if (gid == 0 || fdOfGlyph[gid] != fdOfGlyph[gid - 1])
            {
                ranges.Add((gid, fdOfGlyph[gid]));
            }
        }

        byte[] result = new byte[3 + (ranges.Count * 3) + 2];
        result[0] = 3;
        result[1] = (byte)(ranges.Count >> 8);
        result[2] = (byte)ranges.Count;
        int pos = 3;
        foreach ((int first, byte fd) in ranges)
        {
            result[pos] = (byte)(first >> 8);
            result[pos + 1] = (byte)first;
            result[pos + 2] = fd;
            pos += 3;
        }

        result[pos] = (byte)(fdOfGlyph.Length >> 8);
        result[pos + 1] = (byte)fdOfGlyph.Length;
        return result;
    }

    // ── DICT ────────────────────────────────────────────────────────────────

    private static DictEntry? Find(List<DictEntry> dict, int op)
    {
        foreach (DictEntry e in dict)
        {
            if (e.Op == op)
            {
                return e;
            }
        }

        return null;
    }

    private static List<DictEntry> ParseDict(byte[] d, int start, int length)
    {
        if (start < 0 || length < 0 || start > d.Length - length)
        {
            throw new InvalidDataException("A CFF DICT lies outside the font.");
        }

        List<DictEntry> entries = new List<DictEntry>();
        List<double> values = new List<double>();
        int end = start + length;
        int entryStart = start;
        int i = start;
        while (i < end)
        {
            int b = d[i];
            if (b <= 21)
            {
                int op = b;
                i++;
                if (b == 12)
                {
                    op = 0x0C00 | d[i];
                    i++;
                }

                entries.Add(new DictEntry(op, values, entryStart, i));
                values = new List<double>();
                entryStart = i;
            }
            else if (b == 28)
            {
                values.Add((short)((d[i + 1] << 8) | d[i + 2]));
                i += 3;
            }
            else if (b == 29)
            {
                values.Add((d[i + 1] << 24) | (d[i + 2] << 16) | (d[i + 3] << 8) | d[i + 4]);
                i += 5;
            }
            else if (b == 30)
            {
                i++;
                while (i < end)
                {
                    int v = d[i++];
                    if ((v & 0x0F) == 0x0F || (v >> 4) == 0x0F)
                    {
                        break;
                    }
                }

                values.Add(0); // reals are copied raw; their value is never needed here
            }
            else if (b >= 32 && b <= 246)
            {
                values.Add(b - 139);
                i++;
            }
            else if (b >= 247 && b <= 250)
            {
                values.Add(((b - 247) * 256) + d[i + 1] + 108);
                i += 2;
            }
            else if (b >= 251 && b <= 254)
            {
                values.Add((-(b - 251) * 256) - d[i + 1] - 108);
                i += 2;
            }
            else
            {
                throw new InvalidDataException($"Invalid CFF DICT byte {b}.");
            }
        }

        return entries;
    }

    private static void WriteOp(Stream s, int op)
    {
        if (op > 0xFF)
        {
            s.WriteByte(12);
        }

        s.WriteByte((byte)op);
    }

    private static void WriteInt(Stream s, int v)
    {
        if (v >= -107 && v <= 107)
        {
            s.WriteByte((byte)(v + 139));
        }
        else
        {
            WriteFixedInt(s, v);
        }
    }

    private static void WriteFixedInt(Stream s, int v)
    {
        s.WriteByte(29);
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static void PatchFixedInt(byte[] b, int at, int v)
    {
        b[at + 1] = (byte)(v >> 24);
        b[at + 2] = (byte)(v >> 16);
        b[at + 3] = (byte)(v >> 8);
        b[at + 4] = (byte)v;
    }

    private static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];

    private static uint U32(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

    private sealed class DictEntry
    {
        internal DictEntry(int op, List<double> values, int rawStart, int rawEnd)
        {
            Op = op;
            Values = values;
            RawStart = rawStart;
            RawEnd = rawEnd;
        }

        internal int Op { get; }

        internal List<double> Values { get; }

        // The operands and operator bytes in the source.
        internal int RawStart { get; }

        internal int RawEnd { get; }
    }

    private sealed class FontDict
    {
        internal FontDict(List<(int Start, int End)> entries, List<(int Start, int End)> privateEntries, byte[]? localSubrs)
        {
            Entries = entries;
            PrivateEntries = privateEntries;
            LocalSubrs = localSubrs;
        }

        internal List<(int Start, int End)> Entries { get; }

        internal List<(int Start, int End)> PrivateEntries { get; }

        internal byte[]? LocalSubrs { get; }

        internal byte[]? Source { get; init; }

        internal FontDict WithLocalSubrs(byte[]? localSubrs) =>
            new FontDict(Entries, PrivateEntries, localSubrs) { Source = Source };
    }

    // Which global and local subroutines the kept glyphs reach, found by
    // following each kept charstring's calls (Type 2 charstrings only).
    private sealed class SubrUsage
    {
        private const int MaxCallDepth = 10;

        // Charstring bytes followed in all, across every glyph and call; a
        // crafted font whose subroutines call each other many times over
        // would otherwise take exponential time. Past it nothing is pruned.
        private const long MaxWork = 50_000_000;

        private readonly byte[] _cff;
        private readonly Index _global;
        private readonly HashSet<int> _globalUsed = new HashSet<int>();
        private readonly List<HashSet<int>> _localUsed = new List<HashSet<int>>();
        private readonly List<Index?> _locals = new List<Index?>();
        private readonly List<byte[]?> _localBytes = new List<byte[]?>();

        // Per-charstring interpreter state.
        private readonly double[] _stack = new double[48];
        private int _sp;
        private int _stems;
        private bool _ended;
        private long _work;

        private SubrUsage(byte[] cff, Index global)
        {
            _cff = cff;
            _global = global;
        }

        // False when some charstring could not be followed; nothing is pruned then.
        internal bool Complete { get; private set; } = true;

        internal static SubrUsage Find(
            byte[] cff, Index charStrings, Index global, List<FontDict> fontDicts, byte[] fdOfGlyph, IReadOnlySet<int> usedGlyphs)
        {
            SubrUsage usage = new SubrUsage(cff, global);
            foreach (FontDict fd in fontDicts)
            {
                usage._localUsed.Add(new HashSet<int>());
                usage._localBytes.Add(fd.LocalSubrs);
                usage._locals.Add(fd.LocalSubrs is byte[] bytes ? Index.Read(bytes, 0) : null);
            }

            for (int gid = 0; gid < charStrings.Count && usage.Complete; gid++)
            {
                if (gid != 0 && !usedGlyphs.Contains(gid))
                {
                    continue;
                }

                usage._sp = 0;
                usage._stems = 0;
                usage._ended = false;
                if (!usage.Run(cff, charStrings.Start(gid), charStrings.Length(gid), fdOfGlyph[gid], 0))
                {
                    usage.Complete = false;
                }
            }

            return usage;
        }

        internal byte[] PruneGlobal(byte[] cff, Index global) => Prune(cff, global, _globalUsed);

        internal byte[]? PruneLocal(byte[]? localBytes, int fd) =>
            localBytes is null ? null : Prune(localBytes, Index.Read(localBytes, 0), _localUsed[fd]);

        private static byte[] Prune(byte[] source, Index index, HashSet<int> used)
        {
            List<byte[]> items = new List<byte[]>(index.Count);
            byte[] ret = { 11 };
            for (int i = 0; i < index.Count; i++)
            {
                items.Add(used.Contains(i) ? source.AsSpan(index.Start(i), index.Length(i)).ToArray() : ret);
            }

            return Index.Build(items);
        }

        private static int Bias(int count) => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

        // Follows one charstring (or subroutine). Returns false when it cannot
        // be followed safely (arithmetic on subroutine numbers, bad calls).
        private bool Run(byte[] d, int start, int length, int fd, int depth)
        {
            _work += length;
            if (depth > MaxCallDepth || _work > MaxWork)
            {
                return false;
            }

            int i = start;
            int end = start + length;
            while (i < end && !_ended)
            {
                int b = d[i];
                if (b == 28)
                {
                    if (i + 2 >= end)
                    {
                        return false;
                    }

                    Push((short)((d[i + 1] << 8) | d[i + 2]));
                    i += 3;
                }
                else if (b >= 32 && b <= 246)
                {
                    Push(b - 139);
                    i++;
                }
                else if (b >= 247 && b <= 250)
                {
                    Push(((b - 247) * 256) + d[i + 1] + 108);
                    i += 2;
                }
                else if (b >= 251 && b <= 254)
                {
                    Push((-(b - 251) * 256) - d[i + 1] - 108);
                    i += 2;
                }
                else if (b == 255)
                {
                    Push(((d[i + 1] << 24) | (d[i + 2] << 16) | (d[i + 3] << 8) | d[i + 4]) / 65536.0);
                    i += 5;
                }
                else if (b == 1 || b == 3 || b == 18 || b == 23)
                {
                    // hstem, vstem, hstemhm, vstemhm
                    _stems += _sp / 2;
                    _sp = 0;
                    i++;
                }
                else if (b == 19 || b == 20)
                {
                    // hintmask / cntrmask: pending arguments are an implicit vstem.
                    _stems += _sp / 2;
                    _sp = 0;
                    i += 1 + ((_stems + 7) / 8);
                }
                else if (b == 10 || b == 29)
                {
                    if (_sp < 1)
                    {
                        return false;
                    }

                    bool global = b == 29;
                    Index? subrs = global ? _global : _locals[fd];
                    byte[]? bytes = global ? _cff : _localBytes[fd];
                    double n = _stack[--_sp];
                    if (subrs is not Index index || bytes is null || n != Math.Floor(n))
                    {
                        return false;
                    }

                    int number = (int)n + Bias(index.Count);
                    if (number < 0 || number >= index.Count)
                    {
                        return false;
                    }

                    (global ? _globalUsed : _localUsed[fd]).Add(number);
                    if (!Run(bytes, index.Start(number), index.Length(number), fd, depth + 1))
                    {
                        return false;
                    }

                    i++;
                }
                else if (b == 11)
                {
                    // return
                    return true;
                }
                else if (b == 14)
                {
                    // endchar
                    _ended = true;
                    return true;
                }
                else if (b == 12)
                {
                    int op = i + 1 < end ? d[i + 1] : -1;
                    if (op < 34 || op > 37)
                    {
                        // Only the flex operators are expected; arithmetic and
                        // storage operators could compute a subroutine number.
                        return false;
                    }

                    _sp = 0;
                    i += 2;
                }
                else if (b <= 31)
                {
                    // Drawing operators: rmoveto, rlineto, curves, ...
                    _sp = 0;
                    i++;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        private void Push(double v)
        {
            if (_sp < _stack.Length)
            {
                _stack[_sp++] = v;
            }
        }
    }

    // A CFF INDEX: count, offset size and item positions.
    private readonly struct Index
    {
        private readonly byte[] _d;
        private readonly int _offsets;
        private readonly int _offSize;
        private readonly int _dataBase;

        private Index(byte[] d, int count, int offsets, int offSize, int dataBase, int end)
        {
            _d = d;
            Count = count;
            _offsets = offsets;
            _offSize = offSize;
            _dataBase = dataBase;
            End = end;
        }

        internal int Count { get; }

        internal int End { get; }

        internal static Index Read(byte[] d, int at)
        {
            if (at < 0 || at > d.Length - 2)
            {
                throw new InvalidDataException("A CFF INDEX lies outside the font.");
            }

            int count = U16(d, at);
            if (count == 0)
            {
                return new Index(d, 0, 0, 1, 0, at + 2);
            }

            int offSize = d[at + 2];
            if (offSize < 1 || offSize > 4)
            {
                throw new InvalidDataException("A CFF INDEX has an invalid offset size.");
            }

            int offsets = at + 3;
            int dataBase = offsets + ((count + 1) * offSize) - 1;
            Index index = new Index(d, count, offsets, offSize, dataBase, 0);
            int end = dataBase + index.Offset(count);
            if (end > d.Length || end < dataBase)
            {
                throw new InvalidDataException("A CFF INDEX runs past the end of the font.");
            }

            return new Index(d, count, offsets, offSize, dataBase, end);
        }

        internal int Start(int i) => _dataBase + Offset(i);

        internal int Length(int i)
        {
            int length = Offset(i + 1) - Offset(i);
            return length >= 0 ? length : throw new InvalidDataException("A CFF INDEX has decreasing offsets.");
        }

        internal static byte[] Build(List<byte[]> items)
        {
            if (items.Count == 0)
            {
                return new byte[] { 0, 0 };
            }

            long total = 1;
            foreach (byte[] item in items)
            {
                total += item.Length;
            }

            int offSize = total <= 0xFF ? 1 : total <= 0xFFFF ? 2 : total <= 0xFFFFFF ? 3 : 4;
            MemoryStream s = new MemoryStream();
            s.WriteByte((byte)(items.Count >> 8));
            s.WriteByte((byte)items.Count);
            s.WriteByte((byte)offSize);
            long offset = 1;
            WriteOffset(s, offset, offSize);
            foreach (byte[] item in items)
            {
                offset += item.Length;
                WriteOffset(s, offset, offSize);
            }

            foreach (byte[] item in items)
            {
                s.Write(item);
            }

            return s.ToArray();
        }

        private static void WriteOffset(Stream s, long v, int size)
        {
            for (int k = size - 1; k >= 0; k--)
            {
                s.WriteByte((byte)(v >> (8 * k)));
            }
        }

        private int Offset(int i)
        {
            int v = 0;
            int at = _offsets + (i * _offSize);
            for (int k = 0; k < _offSize; k++)
            {
                v = (v << 8) | _d[at + k];
            }

            return v;
        }
    }
}
