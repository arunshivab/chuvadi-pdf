// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 (D-176) — the built-in encoding of an embedded simple font
//        program, the implicit base of a /Differences array without
//        /BaseEncoding (PDF 32000-1:2008 Table 114): a Type 1 program's
//        /Encoding (the cleartext part of /FontFile) or a CFF program's
//        Encoding (/FontFile3 /Type1C).
// SPEC:  PDF 32000-1:2008 §9.6.6.1, Table 114; Adobe Type 1 Font Format
//        §2.4; Adobe Technical Note #5176 (CFF) §12 and §13.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Fonts.Rendering;

namespace Chuvadi.Pdf.Fonts;

internal static class FontProgramEncoding
{
    private static readonly Regex Type1Entry = new Regex(@"dup\s+(\d+)\s*/([^\s/\[\]{}()<>]+)\s+put", RegexOptions.CultureInvariant);

    // A Type 1 program's built-in encoding, from its cleartext part: either
    // "/Encoding StandardEncoding def" or an array filled by
    // "dup <code> /<name> put" entries. Null when it cannot be read.
    internal static char[]? FromType1(byte[] program)
    {
        string text = Encoding.Latin1.GetString(program, 0, Math.Min(program.Length, 1 << 20));
        int at = text.IndexOf("/Encoding", StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        int eexec = text.IndexOf("eexec", at, StringComparison.Ordinal);
        string tail = text.Substring(at, (eexec < 0 ? text.Length : eexec) - at);
        if (Regex.IsMatch(tail, @"^/Encoding\s+StandardEncoding\s+def", RegexOptions.CultureInvariant))
        {
            return BuiltInEncodings.Standard.ToCharArray();
        }

        int end = tail.IndexOf(" def", StringComparison.Ordinal) is int e and >= 0 && tail.IndexOf("readonly", StringComparison.Ordinal) is int r and >= 0
            ? Math.Max(e, r)
            : tail.Length;
        char[] map = new char[256];
        bool any = false;
        foreach (Match m in Type1Entry.Matches(tail))
        {
            if (int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int code) && code < 256)
            {
                map[code] = PdfFontEncoding.GlyphNameToUnicode(m.Groups[2].Value);
                any = true;
            }
        }

        return any ? map : null;
    }

    // A CFF program's built-in encoding (Top DICT operator 16): predefined
    // StandardEncoding (0), ExpertEncoding (1, not supported: null) or a
    // custom format 0 or 1 table with supplements, codes mapped through the
    // charset's glyph names. Null when it cannot be read.
    internal static char[]? FromCff(byte[] cff)
    {
        try
        {
            return new CffReader(cff).BuiltIn();
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private sealed class CffReader
    {
        private readonly byte[] _d;

        internal CffReader(byte[] data)
        {
            _d = data;
        }

        internal char[]? BuiltIn()
        {
            int p = _d[2];
            p = SkipIndex(p);
            (int topStart, int topEnd, int next) = FirstEntry(p);
            List<byte[]> strings = ReadIndex(next, out _);
            Dictionary<int, double> top = ParseDict(topStart, topEnd);
            int charset = top.TryGetValue(15, out double cs) ? (int)cs : 0;
            int encoding = top.TryGetValue(16, out double en) ? (int)en : 0;
            if (top.ContainsKey(1230))
            {
                return null; // CID-keyed: no simple encoding.
            }

            if (encoding == 0)
            {
                return BuiltInEncodings.Standard.ToCharArray();
            }

            if (encoding == 1)
            {
                return null;
            }

            int charStrings = top.TryGetValue(17, out double chs) ? (int)chs : 0;
            int glyphs = Count(charStrings);
            int[] gidSid = Charset(charset, glyphs);
            char[] map = new char[256];
            int q = encoding;
            int format = _d[q++];
            if ((format & 0x7F) == 0)
            {
                int n = _d[q++];
                for (int i = 1; i <= n; i++)
                {
                    Set(map, _d[q++], i < glyphs ? gidSid[i] : -1, strings);
                }
            }
            else if ((format & 0x7F) == 1)
            {
                int ranges = _d[q++];
                int gid = 1;
                for (int r = 0; r < ranges; r++)
                {
                    int first = _d[q++];
                    int left = _d[q++];
                    for (int c = first; c <= first + left && c < 256; c++, gid++)
                    {
                        Set(map, c, gid < glyphs ? gidSid[gid] : -1, strings);
                    }
                }
            }
            else
            {
                return null;
            }

            if ((format & 0x80) != 0)
            {
                int sups = _d[q++];
                for (int s = 0; s < sups; s++)
                {
                    int code = _d[q++];
                    int sid = (_d[q] << 8) | _d[q + 1];
                    q += 2;
                    Set(map, code, sid, strings);
                }
            }

            return map;
        }

        private static void Set(char[] map, int code, int sid, List<byte[]> strings)
        {
            if (sid < 0 || code > 255)
            {
                return;
            }

            string? name = sid < CffStandardStrings.Count
                ? CffStandardStrings.Get(sid)
                : sid - CffStandardStrings.Count < strings.Count ? Encoding.Latin1.GetString(strings[sid - CffStandardStrings.Count]) : null;
            if (name is not null)
            {
                map[code] = PdfFontEncoding.GlyphNameToUnicode(name);
            }
        }

        private int[] Charset(int offset, int glyphs)
        {
            int[] sids = new int[Math.Max(glyphs, 1)];
            if (offset == 0)
            {
                for (int g = 1; g < glyphs; g++)
                {
                    sids[g] = g; // ISOAdobe: SID = GID.
                }

                return sids;
            }

            if (offset < 3)
            {
                return sids; // Expert charsets: names unknown here.
            }

            int format = _d[offset];
            int p = offset + 1;
            int gid = 1;
            while (gid < glyphs)
            {
                int first = (_d[p] << 8) | _d[p + 1];
                p += 2;
                if (format == 0)
                {
                    sids[gid++] = first;
                    continue;
                }

                int left = format == 1 ? _d[p++] : (_d[p] << 8) | _d[p + 1];
                if (format == 2)
                {
                    p += 2;
                }

                for (int i = 0; i <= left && gid < glyphs; i++)
                {
                    sids[gid++] = first + i;
                }
            }

            return sids;
        }

        private int Count(int index) => (_d[index] << 8) | _d[index + 1];

        private int SkipIndex(int p)
        {
            _ = ReadIndex(p, out int next);
            return next;
        }

        private (int Start, int End, int Next) FirstEntry(int p)
        {
            int count = Count(p);
            int offSize = _d[p + 2];
            int offsets = p + 3;
            int data = offsets + ((count + 1) * offSize) - 1;
            int o0 = Offset(offsets, offSize);
            int o1 = Offset(offsets + offSize, offSize);
            int last = Offset(offsets + (count * offSize), offSize);
            return (data + o0, data + o1, data + last);
        }

        private List<byte[]> ReadIndex(int p, out int next)
        {
            List<byte[]> items = new List<byte[]>();
            int count = Count(p);
            if (count == 0)
            {
                next = p + 2;
                return items;
            }

            int offSize = _d[p + 2];
            int offsets = p + 3;
            int data = offsets + ((count + 1) * offSize) - 1;
            for (int i = 0; i < count; i++)
            {
                int a = Offset(offsets + (i * offSize), offSize);
                int b = Offset(offsets + ((i + 1) * offSize), offSize);
                items.Add(_d.AsSpan(data + a, b - a).ToArray());
            }

            next = data + Offset(offsets + (count * offSize), offSize);
            return items;
        }

        private int Offset(int p, int size)
        {
            int v = 0;
            for (int i = 0; i < size; i++)
            {
                v = (v << 8) | _d[p + i];
            }

            return v;
        }

        // The Top DICT's operators with their last operand (enough for
        // charset, Encoding, CharStrings and ROS).
        private Dictionary<int, double> ParseDict(int p, int end)
        {
            Dictionary<int, double> dict = new Dictionary<int, double>();
            double last = 0;
            while (p < end)
            {
                int b0 = _d[p];
                if (b0 <= 21)
                {
                    int op = b0 == 12 ? 1200 + _d[++p] : b0;
                    p++;
                    dict[op] = last;
                    continue;
                }

                if (b0 == 28)
                {
                    last = (short)((_d[p + 1] << 8) | _d[p + 2]);
                    p += 3;
                }
                else if (b0 == 29)
                {
                    last = (_d[p + 1] << 24) | (_d[p + 2] << 16) | (_d[p + 3] << 8) | _d[p + 4];
                    p += 5;
                }
                else if (b0 == 30)
                {
                    p++;
                    while (p < end && (_d[p] & 0x0F) != 0x0F && (_d[p] >> 4) != 0x0F)
                    {
                        p++;
                    }

                    p++;
                    last = 0;
                }
                else if (b0 >= 32 && b0 <= 246)
                {
                    last = b0 - 139;
                    p++;
                }
                else if (b0 >= 247 && b0 <= 250)
                {
                    last = ((b0 - 247) * 256) + _d[p + 1] + 108;
                    p += 2;
                }
                else if (b0 >= 251 && b0 <= 254)
                {
                    last = (-(b0 - 251) * 256) - _d[p + 1] - 108;
                    p += 2;
                }
                else
                {
                    p++;
                }
            }

            return dict;
        }
    }
}
