// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// The property lookups and the special cases follow HarfBuzz (hb-unicode.hh,
// hb-ucd.cc); see THIRD-PARTY-NOTICES.md. The tables are generated from
// HarfBuzz's own Unicode data by tools/shaping-data.

using System;

namespace Chuvadi.Pdf.Text.Shaping.Unicode;

/// <summary>
/// Unicode character properties used by the shaper: general category,
/// (modified) combining class, canonical decomposition and composition,
/// default-ignorable code points, space fallback types and the Indic
/// syllabic/positional categories.
/// </summary>
internal static class UnicodeData
{
    internal const int Control = 0;
    internal const int Format = 1;
    internal const int Unassigned = 2;
    internal const int PrivateUse = 3;
    internal const int Surrogate = 4;
    internal const int LowercaseLetter = 5;
    internal const int ModifierLetter = 6;
    internal const int OtherLetter = 7;
    internal const int TitlecaseLetter = 8;
    internal const int UppercaseLetter = 9;
    internal const int SpacingMark = 10;
    internal const int EnclosingMark = 11;
    internal const int NonSpacingMark = 12;
    internal const int DecimalNumber = 13;
    internal const int LetterNumber = 14;
    internal const int OtherNumber = 15;
    internal const int ConnectPunctuation = 16;
    internal const int DashPunctuation = 17;
    internal const int ClosePunctuation = 18;
    internal const int FinalPunctuation = 19;
    internal const int InitialPunctuation = 20;
    internal const int OtherPunctuation = 21;
    internal const int OpenPunctuation = 22;
    internal const int CurrencySymbol = 23;
    internal const int ModifierSymbol = 24;
    internal const int MathSymbol = 25;
    internal const int OtherSymbol = 26;
    internal const int LineSeparator = 27;
    internal const int ParagraphSeparator = 28;
    internal const int SpaceSeparator = 29;

    // Space fallback types (hb_unicode_funcs_t::space_t).
    internal const int NotSpace = 0;
    internal const int SpaceEm = 1;
    internal const int SpaceEm2 = 2;
    internal const int SpaceEm3 = 3;
    internal const int SpaceEm4 = 4;
    internal const int SpaceEm5 = 5;
    internal const int SpaceEm6 = 6;
    internal const int SpaceEm16 = 16;
    internal const int Space4Em18 = 17;
    internal const int Space = 18;
    internal const int SpaceFigure = 19;
    internal const int SpacePunctuation = 20;
    internal const int SpaceNarrow = 21;

    private const int SBase = 0xAC00;
    private const int LBase = 0x1100;
    private const int VBase = 0x1161;
    private const int TBase = 0x11A7;
    private const int LCount = 19;
    private const int VCount = 21;
    private const int TCount = 28;
    private const int NCount = VCount * TCount;
    private const int SCount = LCount * NCount;

    private static readonly RunTable GcTable = new RunTable(Convert.FromBase64String(UnicodeTables.GeneralCategory), 4);
    private static readonly RunTable CcTable = new RunTable(Convert.FromBase64String(UnicodeTables.CombiningClass), 4);
    private static readonly RunTable IndicTable = new RunTable(Convert.FromBase64String(UnicodeTables.Indic), 5);
    private static readonly DecompositionTable Decomp = new DecompositionTable(Convert.FromBase64String(UnicodeTables.Decompositions));

    /// <summary>Returns whether a general category is a mark (Mc, Me or Mn).</summary>
    internal static bool IsMarkCategory(int generalCategory) =>
        generalCategory is SpacingMark or EnclosingMark or NonSpacingMark;

    /// <summary>Returns whether a general category is a letter (Ll, Lm, Lo, Lt or Lu).</summary>
    internal static bool IsLetterCategory(int generalCategory) =>
        generalCategory is >= LowercaseLetter and <= UppercaseLetter;

    /// <summary>Returns the general category of a code point (HarfBuzz enum order).</summary>
    internal static int GeneralCategory(int u) =>
        (uint)u > 0x10FFFF ? Unassigned : GcTable.Value(u, 0);

    /// <summary>
    /// Returns the modified combining class HarfBuzz sorts marks by (the
    /// canonical class, adjusted for Hebrew, Arabic, Thai, Lao, Tibetan and
    /// Telugu, and with three code point exceptions).
    /// </summary>
    internal static int ModifiedCombiningClass(int u)
    {
        if (u == 0x1A60 || u == 0x0FC6)
        {
            return 254;
        }

        if (u == 0x0F39)
        {
            return 127;
        }

        return (uint)u > 0x10FFFF ? 0 : CcTable.Value(u, 0);
    }

    /// <summary>
    /// Returns the Indic syllabic category (low byte) and positional category
    /// (high byte) HarfBuzz assigns to a code point.
    /// </summary>
    internal static int IndicCategories(int u)
    {
        if ((uint)u > 0x10FFFF)
        {
            return 14 << 8;
        }

        return IndicTable.Value(u, 0) | (IndicTable.Value(u, 1) << 8);
    }

    /// <summary>Canonical one-step decomposition, as HarfBuzz's <c>decompose</c>.</summary>
    internal static bool Decompose(int ab, out int a, out int b)
    {
        a = ab;
        b = 0;
        int si = ab - SBase;
        if (si >= 0 && si < SCount)
        {
            int ti = si % TCount;
            if (ti != 0)
            {
                a = ab - ti;
                b = TBase + ti;
            }
            else
            {
                a = LBase + (si / NCount);
                b = VBase + ((si % NCount) / TCount);
            }

            return true;
        }

        return Decomp.TryDecompose(ab, out a, out b);
    }

    /// <summary>Canonical composition of a pair, as HarfBuzz's <c>compose</c> (exclusions honoured).</summary>
    internal static bool Compose(int a, int b, out int ab)
    {
        ab = 0;
        if (a >= LBase && a < LBase + LCount && b >= VBase && b < VBase + VCount)
        {
            ab = SBase + ((((a - LBase) * VCount) + (b - VBase)) * TCount);
            return true;
        }

        int si = a - SBase;
        if (si >= 0 && si < SCount && si % TCount == 0 && b > TBase && b < TBase + TCount)
        {
            ab = a + (b - TBase);
            return true;
        }

        return Decomp.TryCompose(a, b, out ab);
    }

    /// <summary>HarfBuzz's Default_Ignorable test (with its Hangul filler exceptions).</summary>
    internal static bool IsDefaultIgnorable(int ch)
    {
        int plane = ch >> 16;
        if (plane == 0)
        {
            return (ch >> 8) switch
            {
                0x00 => ch == 0x00AD,
                0x03 => ch == 0x034F,
                0x06 => ch == 0x061C,
                0x17 => ch is >= 0x17B4 and <= 0x17B5,
                0x18 => ch is >= 0x180B and <= 0x180F,
                0x20 => ch is (>= 0x200B and <= 0x200F) or (>= 0x202A and <= 0x202E) or (>= 0x2060 and <= 0x206F),
                0xFE => ch is (>= 0xFE00 and <= 0xFE0F) or 0xFEFF,
                0xFF => ch is >= 0xFFF0 and <= 0xFFF8,
                _ => false,
            };
        }

        return plane switch
        {
            0x01 => ch is >= 0x1D173 and <= 0x1D17A,
            0x0E => ch is >= 0xE0000 and <= 0xE0FFF,
            _ => false,
        };
    }

    /// <summary>Returns whether a code point is a variation selector (VS1–VS256).</summary>
    internal static bool IsVariationSelector(int u) =>
        u is (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF);

    /// <summary>Returns whether a code point is a regional indicator.</summary>
    internal static bool IsRegionalIndicator(int u) => u is >= 0x1F1E6 and <= 0x1F1FF;

    /// <summary>The space fallback type of a space separator (HarfBuzz's <c>space_fallback_type</c>).</summary>
    internal static int SpaceFallbackType(int u) => u switch
    {
        0x0020 => Space,
        0x00A0 => Space,
        0x2000 => SpaceEm2,
        0x2001 => SpaceEm,
        0x2002 => SpaceEm2,
        0x2003 => SpaceEm,
        0x2004 => SpaceEm3,
        0x2005 => SpaceEm4,
        0x2006 => SpaceEm6,
        0x2007 => SpaceFigure,
        0x2008 => SpacePunctuation,
        0x2009 => SpaceEm5,
        0x200A => SpaceEm16,
        0x202F => SpaceNarrow,
        0x205F => Space4Em18,
        0x3000 => SpaceEm,
        _ => NotSpace,
    };

    // Runs of fixed-size records: start (3 bytes BE) followed by value bytes.
    private sealed class RunTable
    {
        private readonly int[] _starts;
        private readonly byte[] _values;
        private readonly int _valueBytes;

        internal RunTable(byte[] data, int recordSize)
        {
            int count = data.Length / recordSize;
            _valueBytes = recordSize - 3;
            _starts = new int[count];
            _values = new byte[count * _valueBytes];
            for (int i = 0; i < count; i++)
            {
                int o = i * recordSize;
                _starts[i] = (data[o] << 16) | (data[o + 1] << 8) | data[o + 2];
                Array.Copy(data, o + 3, _values, i * _valueBytes, _valueBytes);
            }
        }

        internal int Value(int u, int field)
        {
            int lo = 0;
            int hi = _starts.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_starts[mid] <= u)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return _values[(lo * _valueBytes) + field];
        }
    }

    private sealed class DecompositionTable
    {
        private readonly int[] _composed;
        private readonly int[] _a;
        private readonly int[] _b;
        private readonly System.Collections.Generic.Dictionary<long, int> _compose =
            new System.Collections.Generic.Dictionary<long, int>();

        internal DecompositionTable(byte[] data)
        {
            int count = data.Length / 10;
            _composed = new int[count];
            _a = new int[count];
            _b = new int[count];
            for (int i = 0; i < count; i++)
            {
                int o = i * 10;
                _composed[i] = Read3(data, o);
                _a[i] = Read3(data, o + 3);
                _b[i] = Read3(data, o + 6);
                if (data[o + 9] != 0)
                {
                    _compose[Key(_a[i], _b[i])] = _composed[i];
                }
            }
        }

        internal bool TryDecompose(int ab, out int a, out int b)
        {
            int i = Array.BinarySearch(_composed, ab);
            if (i < 0)
            {
                a = ab;
                b = 0;
                return false;
            }

            a = _a[i];
            b = _b[i];
            return true;
        }

        internal bool TryCompose(int a, int b, out int ab) => _compose.TryGetValue(Key(a, b), out ab);

        private static long Key(int a, int b) => ((long)a << 21) | (uint)b;

        private static int Read3(byte[] d, int o) => (d[o] << 16) | (d[o + 1] << 8) | d[o + 2];
    }
}
