// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7.5.2 (predefined CMaps, Table 118),
//        §9.7.6.2 (CMap mapping), §9.10.2 (mapping CIDs to Unicode)
// PHASE: 3.35.0 (E1, D-352) — the predefined CJK CMaps, from tables
//        generated from one pinned release of Adobe's cmap-resources
//        (tools/gen_cjk_cmaps.py; notice in THIRD-PARTY-NOTICES.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Chuvadi.Pdf.Fonts;

/// <summary>
/// A predefined CMap (PDF 32000-1:2008 §9.7.5.2): the mapping a Type 0
/// font's <c>/Encoding</c> name selects, from character codes to CIDs.
/// Covers <c>Identity-H</c> and <c>Identity-V</c> and the CJK CMaps of the
/// Adobe-GB1, Adobe-CNS1, Adobe-Japan1 and Adobe-Korea1 collections
/// (for example <c>90ms-RKSJ-H</c>, <c>UniGB-UTF16-H</c>, <c>KSCms-UHC-H</c>).
/// </summary>
public sealed class PredefinedCMap
{
    private const string ResourceName = "Chuvadi.Pdf.Fonts.cjk-cmaps.bin";

    private static readonly Lazy<Dictionary<string, Raw>> Table = new Lazy<Dictionary<string, Raw>>(Load);
    private static readonly ConcurrentDictionary<string, PredefinedCMap?> Cache =
        new ConcurrentDictionary<string, PredefinedCMap?>(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Dictionary<int, string>?> CidToUnicodeCache =
        new ConcurrentDictionary<string, Dictionary<int, string>?>(StringComparer.Ordinal);

    // Per byte count (index 1..4): codespace ranges and cid ranges, the
    // latter sorted by low code; a usecmap parent's ranges come first in
    // the search order after the CMap's own.
    private readonly List<(int Lo, int Hi)>[] _codespace;
    private readonly Range[][] _ranges;
    private readonly PredefinedCMap? _parent;
    private readonly UnicodeForm _form;

    private PredefinedCMap(string name, bool vertical, List<(int Lo, int Hi)>[] codespace, Range[][] ranges, PredefinedCMap? parent, UnicodeForm form)
    {
        Name = name;
        IsVertical = vertical;
        _codespace = codespace;
        _ranges = ranges;
        _parent = parent;
        _form = form;
    }

    private enum UnicodeForm
    {
        None,
        Utf16,
        Utf8,
        Utf32,
    }

    /// <summary>Gets the CMap's name, for example <c>UniJIS-UTF16-H</c>.</summary>
    public string Name { get; }

    /// <summary>Gets whether the CMap is for vertical writing (<c>/WMode 1</c>; names ending in <c>-V</c>).</summary>
    public bool IsVertical { get; }

    /// <summary>
    /// Gets whether the character codes are Unicode (the <c>Uni…-UCS2</c>,
    /// <c>-UTF16</c>, <c>-UTF8</c> and <c>-UTF32</c> CMaps), so a code's
    /// text is the code itself.
    /// </summary>
    public bool IsUnicode => _form != UnicodeForm.None || Name.Contains("-UCS2-", StringComparison.Ordinal);

    /// <summary>
    /// Returns the predefined CMap with the given name, or null when there is
    /// none by that name.
    /// </summary>
    /// <param name="name">The CMap name (the <c>/Encoding</c> name of a Type 0 font).</param>
    /// <returns>The CMap, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public static PredefinedCMap? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Cache.GetOrAdd(name, Build);
    }

    /// <summary>
    /// Reads the next character code from <paramref name="bytes"/> (§9.7.6.2):
    /// the shortest byte sequence that falls in a codespace range. Returns the
    /// number of bytes it takes (at least 1 when <paramref name="bytes"/> is
    /// not empty), and its CID (0, the .notdef CID, when the code is not mapped).
    /// </summary>
    /// <param name="bytes">The string bytes, from the next code on.</param>
    /// <param name="code">The code, its bytes packed big-endian.</param>
    /// <param name="cid">The CID the code selects.</param>
    /// <returns>The number of bytes the code takes; 0 only for empty input.</returns>
    public int Next(ReadOnlySpan<byte> bytes, out int code, out int cid)
    {
        code = 0;
        cid = 0;
        if (bytes.IsEmpty)
        {
            return 0;
        }

        if (_form == UnicodeForm.Utf8 || _form == UnicodeForm.Utf32)
        {
            return NextUnicode(bytes, out code, out cid);
        }

        int length = CodeLength(bytes);
        for (int k = 0; k < length; k++)
        {
            code = (code << 8) | bytes[k];
        }

        cid = Lookup(length, code);
        return length;
    }

    /// <summary>
    /// The text of a CID in a character collection (§9.10.2): through the
    /// collection's Unicode CMaps (for Adobe-Japan1, UniJIS-UTF16-H first,
    /// then the half-width, vertical and later ones), read backwards. Returns null for a CID with no Unicode value, or an
    /// ordering other than GB1, CNS1, Japan1 and Korea1.
    /// </summary>
    /// <param name="ordering">The <c>/CIDSystemInfo</c> <c>/Ordering</c>, for example "Japan1".</param>
    /// <param name="cid">The CID.</param>
    /// <returns>The text, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ordering"/> is null.</exception>
    public static string? CidToUnicode(string ordering, int cid)
    {
        ArgumentNullException.ThrowIfNull(ordering);
        Dictionary<int, string>? map = CidToUnicodeCache.GetOrAdd(ordering, BuildCidToUnicode);
        return map is not null && map.TryGetValue(cid, out string? text) ? text : null;
    }

    /// <summary>
    /// The text of a code of a Unicode CMap (see <see cref="IsUnicode"/>), or
    /// null for any other CMap or bytes that are not valid in its form.
    /// </summary>
    /// <param name="codeBytes">The code's bytes, as <see cref="Next"/> took them.</param>
    /// <returns>The text, or null.</returns>
    public string? CodeToUnicode(ReadOnlySpan<byte> codeBytes)
    {
        if (!IsUnicode || codeBytes.IsEmpty)
        {
            return null;
        }

        try
        {
            return _form switch
            {
                UnicodeForm.Utf8 => new UTF8Encoding(false, true).GetString(codeBytes),
                UnicodeForm.Utf32 => new UTF32Encoding(true, false, true).GetString(codeBytes),
                _ => new UnicodeEncoding(true, false, true).GetString(codeBytes),
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // ── Decoding ──────────────────────────────────────────────────────────

    private int CodeLength(ReadOnlySpan<byte> bytes)
    {
        int shortest = 0;
        for (int n = 1; n <= 4 && n <= bytes.Length; n++)
        {
            if (InCodespace(n, bytes, ref shortest))
            {
                return n;
            }
        }

        // No codespace range holds it: take as many bytes as the shortest
        // range (§9.7.6.3), at least one.
        return Math.Clamp(shortest == 0 ? 1 : shortest, 1, bytes.Length);
    }

    // Whether the first n bytes fall in a codespace range of n bytes, each
    // byte within the range's bytes (§9.7.6.2). Notes the shortest range.
    private bool InCodespace(int n, ReadOnlySpan<byte> bytes, ref int shortest)
    {
        for (PredefinedCMap? map = this; map is not null; map = map._parent)
        {
            foreach ((int lo, int hi) in map._codespace[n])
            {
                if (shortest == 0 || n < shortest)
                {
                    shortest = n;
                }

                bool inside = true;
                for (int k = 0; k < n && inside; k++)
                {
                    int shift = 8 * (n - 1 - k);
                    int b = bytes[k];
                    inside = b >= ((lo >> shift) & 0xFF) && b <= ((hi >> shift) & 0xFF);
                }

                if (inside)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private int Lookup(int length, int code)
    {
        for (PredefinedCMap? map = this; map is not null; map = map._parent)
        {
            Range[] ranges = map._ranges[length];
            int lo = 0;
            int hi = ranges.Length - 1;
            int found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (ranges[mid].Lo <= code)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            // Ranges do not normally overlap; look back a little for one
            // that holds the code when they do.
            for (int i = found; i >= 0 && i > found - 8; i--)
            {
                if (code >= ranges[i].Lo && code <= ranges[i].Hi)
                {
                    return ranges[i].Cid + (code - ranges[i].Lo);
                }
            }
        }

        return 0;
    }

    // UTF-8 and UTF-32 forms: the code is read as a Unicode scalar and looked
    // up in the UTF-16 table, whose codes are UTF-16BE.
    private int NextUnicode(ReadOnlySpan<byte> bytes, out int code, out int cid)
    {
        int length;
        int scalar;
        if (_form == UnicodeForm.Utf32)
        {
            length = Math.Min(4, bytes.Length);
            scalar = 0;
            for (int k = 0; k < length; k++)
            {
                scalar = (scalar << 8) | bytes[k];
            }
        }
        else
        {
            byte lead = bytes[0];
            length = lead < 0x80 ? 1 : lead >= 0xF0 ? 4 : lead >= 0xE0 ? 3 : lead >= 0xC0 ? 2 : 1;
            length = Math.Min(length, bytes.Length);
            scalar = length == 1 ? lead : lead & (0x7F >> length);
            for (int k = 1; k < length; k++)
            {
                scalar = (scalar << 6) | (bytes[k] & 0x3F);
            }
        }

        code = 0;
        for (int k = 0; k < length; k++)
        {
            code = (code << 8) | bytes[k];
        }

        if (scalar < 0x10000)
        {
            cid = Lookup(2, scalar);
        }
        else if (scalar <= 0x10FFFF)
        {
            int v = scalar - 0x10000;
            int utf16 = ((0xD800 | (v >> 10)) << 16) | (0xDC00 | (v & 0x3FF));
            cid = Lookup(4, utf16);
        }
        else
        {
            cid = 0;
        }

        return length;
    }

    // ── Construction ──────────────────────────────────────────────────────

    private static PredefinedCMap? Build(string name)
    {
        if (name is "Identity-H" or "Identity-V")
        {
            List<(int Lo, int Hi)>[] cs = EmptyCodespace();
            cs[2].Add((0x0000, 0xFFFF));
            Range[][] ranges = EmptyRanges();
            ranges[2] = new[] { new Range(0, 0xFFFF, 0) };
            return new PredefinedCMap(name, name.EndsWith("-V", StringComparison.Ordinal), cs, ranges, null, UnicodeForm.None);
        }

        UnicodeForm form = UnicodeForm.None;
        string tableName = name;
        if (name.Contains("-UTF8-", StringComparison.Ordinal) || name.Contains("-UTF32-", StringComparison.Ordinal))
        {
            string utf16 = name.Replace("-UTF8-", "-UTF16-", StringComparison.Ordinal).Replace("-UTF32-", "-UTF16-", StringComparison.Ordinal);
            if (!Table.Value.ContainsKey(name) && Table.Value.ContainsKey(utf16))
            {
                form = name.Contains("-UTF8-", StringComparison.Ordinal) ? UnicodeForm.Utf8 : UnicodeForm.Utf32;
                tableName = utf16;
            }
        }
        else if (name.Contains("-UTF16-", StringComparison.Ordinal))
        {
            form = UnicodeForm.Utf16;
        }

        if (!Table.Value.TryGetValue(tableName, out Raw? raw))
        {
            return null;
        }

        PredefinedCMap? parent = raw.UseCMap.Length > 0 && raw.UseCMap != tableName ? Get(raw.UseCMap) : null;
        List<(int Lo, int Hi)>[] codespace = EmptyCodespace();
        foreach ((int bytes, int lo, int hi) in raw.Codespace)
        {
            if (bytes is >= 1 and <= 4)
            {
                codespace[bytes].Add((lo, hi));
            }
        }

        List<Range>[] lists = new List<Range>[5];
        for (int i = 0; i < 5; i++)
        {
            lists[i] = new List<Range>();
        }

        // Later entries win: keep the last of equal low codes.
        foreach ((int bytes, int lo, int hi, int cid) in raw.Ranges)
        {
            if (bytes is >= 1 and <= 4)
            {
                lists[bytes].Add(new Range(lo, hi, cid));
            }
        }

        Range[][] sorted = new Range[5][];
        for (int i = 0; i < 5; i++)
        {
            Range[] array = lists[i].ToArray();
            int[] order = new int[array.Length];
            for (int k = 0; k < order.Length; k++)
            {
                order[k] = k;
            }

            Array.Sort(order, (a, b) => array[a].Lo != array[b].Lo ? array[a].Lo.CompareTo(array[b].Lo) : a.CompareTo(b));
            Range[] result = new Range[array.Length];
            for (int k = 0; k < order.Length; k++)
            {
                result[k] = array[order[k]];
            }

            sorted[i] = result;
        }

        bool vertical = raw.WMode == 1 || (parent?.IsVertical ?? false) || name.EndsWith("-V", StringComparison.Ordinal);
        return new PredefinedCMap(name, vertical, codespace, sorted, parent, form);
    }

    private static Dictionary<int, string>? BuildCidToUnicode(string ordering)
    {
        string? prefix = ordering switch
        {
            "GB1" => "UniGB-",
            "CNS1" => "UniCNS-",
            "Japan1" => "UniJIS-",
            "Korea1" => "UniKS-",
            _ => null,
        };
        if (prefix is null)
        {
            return null;
        }

        // The collection's Unicode CMaps read backwards: first the main
        // UTF-16 one, then the others (half-width, vertical, later
        // editions), each adding the CIDs not yet mapped; within a CMap the
        // first Unicode value of a CID wins, except that a compatibility
        // form gives way to any other character.
        List<string> tables = new List<string>();
        string main = prefix + "UTF16-H";
        if (Table.Value.ContainsKey(main))
        {
            tables.Add(main);
        }

        List<string> others = new List<string>();
        foreach (string name in Table.Value.Keys)
        {
            if (name != main
                && (name.StartsWith(prefix, StringComparison.Ordinal) || (prefix == "UniJIS-" && name.StartsWith("UniJIS2004-", StringComparison.Ordinal)))
                && (name.Contains("-UTF16-", StringComparison.Ordinal) || name.Contains("-UCS2-", StringComparison.Ordinal)))
            {
                others.Add(name);
            }
        }

        others.Sort(StringComparer.Ordinal);
        tables.AddRange(others);

        Dictionary<int, string> map = new Dictionary<int, string>();
        foreach (string table in tables)
        {
            Raw raw = Table.Value[table];
            foreach ((int bytes, int lo, int hi, int cid) in raw.Ranges)
            {
                for (long wide = (uint)lo; wide <= (uint)hi; wide++)
                {
                    int code = (int)wide;
                    int target = cid + (int)(wide - (uint)lo);
                    if (map.TryGetValue(target, out string? existing) && !IsCompatibilityForm(existing))
                    {
                        continue;
                    }

                    string? text = bytes switch
                    {
                        2 when code is < 0xD800 or > 0xDFFF => ((char)code).ToString(),
                        4 when ((uint)code >> 16) is >= 0xD800 and <= 0xDBFF && (code & 0xFFFF) is >= 0xDC00 and <= 0xDFFF =>
                            char.ConvertFromUtf32(0x10000 + (int)((((uint)code >> 16) - 0xD800) << 10) + ((code & 0xFFFF) - 0xDC00)),
                        _ => null,
                    };
                    if (text is not null && (existing is null || !IsCompatibilityForm(text)))
                    {
                        map[target] = text;
                    }
                }
            }
        }

        return map;
    }

    // Code points a CID shares with a preferred character: Kangxi and CJK
    // radicals, CJK compatibility ideographs and vertical presentation
    // forms. A CID mapped from both reads as the other (日, not ⽇).
    private static bool IsCompatibilityForm(string text)
    {
        int cp = char.ConvertToUtf32(text, 0);
        return cp is (>= 0x2E80 and <= 0x2FDF) or (>= 0xF900 and <= 0xFAFF) or (>= 0x2F800 and <= 0x2FA1F)
            or (>= 0xFE10 and <= 0xFE1F) or (>= 0xFE30 and <= 0xFE4F);
    }

    private static List<(int Lo, int Hi)>[] EmptyCodespace()
    {
        List<(int Lo, int Hi)>[] cs = new List<(int Lo, int Hi)>[5];
        for (int i = 0; i < 5; i++)
        {
            cs[i] = new List<(int Lo, int Hi)>();
        }

        return cs;
    }

    private static Range[][] EmptyRanges()
    {
        Range[][] ranges = new Range[5][];
        for (int i = 0; i < 5; i++)
        {
            ranges[i] = Array.Empty<Range>();
        }

        return ranges;
    }

    private static Dictionary<string, Raw> Load()
    {
        Dictionary<string, Raw> table = new Dictionary<string, Raw>(StringComparer.Ordinal);
        using Stream? resource = typeof(PredefinedCMap).Assembly.GetManifestResourceStream(ResourceName);
        if (resource is null)
        {
            return table;
        }

        using ZLibStream inflate = new ZLibStream(resource, CompressionMode.Decompress);
        using MemoryStream buffer = new MemoryStream();
        inflate.CopyTo(buffer);
        byte[] data = buffer.ToArray();
        int p = 0;
        if (data.Length < 5 || data[0] != (byte)'C' || data[1] != (byte)'V' || data[2] != (byte)'C' || data[3] != (byte)'M' || data[4] != 1)
        {
            return table;
        }

        p = 5;
        int count = (int)VarInt(data, ref p);
        for (int e = 0; e < count; e++)
        {
            string name = Ascii(data, ref p);
            string useCMap = Ascii(data, ref p);
            int wmode = data[p++];
            int csCount = (int)VarInt(data, ref p);
            (int, int, int)[] codespace = new (int, int, int)[csCount];
            for (int i = 0; i < csCount; i++)
            {
                int bytes = data[p++];
                int lo = (int)VarInt(data, ref p);
                int hi = (int)VarInt(data, ref p);
                codespace[i] = (bytes, lo, hi);
            }

            int rangeCount = (int)VarInt(data, ref p);
            (int, int, int, int)[] ranges = new (int, int, int, int)[rangeCount];
            long prevLo = 0;
            long prevCid = 0;
            for (int i = 0; i < rangeCount; i++)
            {
                int bytes = data[p++];
                long lo = prevLo + ZigZag(data, ref p);
                long hi = lo + (long)VarInt(data, ref p);
                long cid = prevCid + ZigZag(data, ref p);
                ranges[i] = (bytes, (int)lo, (int)hi, (int)cid);
                prevLo = lo;
                prevCid = cid;
            }

            table[name] = new Raw(useCMap, wmode, codespace, ranges);
        }

        return table;
    }

    private static ulong VarInt(byte[] data, ref int p)
    {
        ulong value = 0;
        int shift = 0;
        while (true)
        {
            byte b = data[p++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
        }
    }

    private static long ZigZag(byte[] data, ref int p)
    {
        ulong v = VarInt(data, ref p);
        return (long)(v >> 1) ^ -(long)(v & 1);
    }

    private static string Ascii(byte[] data, ref int p)
    {
        int length = (int)VarInt(data, ref p);
        string s = Encoding.ASCII.GetString(data, p, length);
        p += length;
        return s;
    }

    private readonly record struct Range(int Lo, int Hi, int Cid);

    private sealed record Raw(string UseCMap, int WMode, (int Bytes, int Lo, int Hi)[] Codespace, (int Bytes, int Lo, int Hi, int Cid)[] Ranges);
}
