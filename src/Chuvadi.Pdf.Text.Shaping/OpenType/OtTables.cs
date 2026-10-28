// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Readers for the OpenType common layout tables (Coverage, ClassDef, GDEF,
// the GSUB/GPOS script, feature and lookup lists), following HarfBuzz's
// semantics; see THIRD-PARTY-NOTICES.md.

using System.Collections.Generic;

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>Coverage and ClassDef lookups over raw font bytes.</summary>
internal static class OtCommon
{
    internal const int NotCovered = -1;

    /// <summary>Returns the coverage index of a glyph, or <see cref="NotCovered"/>.</summary>
    internal static int Coverage(byte[] d, int offset, int glyph)
    {
        if (offset <= 0 || offset + 4 > d.Length)
        {
            return NotCovered;
        }

        int format = OtReader.U16(d, offset);
        if (format == 1)
        {
            int count = OtReader.U16(d, offset + 2);
            int lo = 0;
            int hi = count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int g = OtReader.U16(d, offset + 4 + (mid * 2));
                if (g == glyph)
                {
                    return mid;
                }

                if (g < glyph)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return NotCovered;
        }

        if (format == 2)
        {
            int count = OtReader.U16(d, offset + 2);
            int lo = 0;
            int hi = count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int r = offset + 4 + (mid * 6);
                int start = OtReader.U16(d, r);
                int end = OtReader.U16(d, r + 2);
                if (glyph < start)
                {
                    hi = mid - 1;
                }
                else if (glyph > end)
                {
                    lo = mid + 1;
                }
                else
                {
                    return OtReader.U16(d, r + 4) + (glyph - start);
                }
            }
        }

        return NotCovered;
    }

    /// <summary>Returns the class of a glyph (0 when not listed or the table is absent).</summary>
    internal static int ClassOf(byte[] d, int offset, int glyph)
    {
        if (offset <= 0 || offset + 4 > d.Length)
        {
            return 0;
        }

        int format = OtReader.U16(d, offset);
        if (format == 1)
        {
            int start = OtReader.U16(d, offset + 2);
            int count = OtReader.U16(d, offset + 4);
            int i = glyph - start;
            return i >= 0 && i < count ? OtReader.U16(d, offset + 6 + (i * 2)) : 0;
        }

        if (format == 2)
        {
            int count = OtReader.U16(d, offset + 2);
            int lo = 0;
            int hi = count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int r = offset + 4 + (mid * 6);
                int start = OtReader.U16(d, r);
                int end = OtReader.U16(d, r + 2);
                if (glyph < start)
                {
                    hi = mid - 1;
                }
                else if (glyph > end)
                {
                    lo = mid + 1;
                }
                else
                {
                    return OtReader.U16(d, r + 4);
                }
            }
        }

        return 0;
    }

    /// <summary>Adds a coverage table's glyphs to a digest (HarfBuzz collect_coverage).</summary>
    internal static void CollectCoverage(byte[] d, int offset, ref SetDigest digest)
    {
        if (offset <= 0 || offset + 4 > d.Length)
        {
            return;
        }

        int format = OtReader.U16(d, offset);
        int count = OtReader.U16(d, offset + 2);
        if (format == 1)
        {
            for (int i = 0; i < count; i++)
            {
                digest.Add(OtReader.U16(d, offset + 4 + (i * 2)));
            }
        }
        else if (format == 2)
        {
            for (int i = 0; i < count; i++)
            {
                int r = offset + 4 + (i * 6);
                if (!digest.AddRange(OtReader.U16(d, r), OtReader.U16(d, r + 2)))
                {
                    return;
                }
            }
        }
    }
}

/// <summary>HarfBuzz's three-mask glyph set digest (shifts 4, 0, 6).</summary>
internal struct SetDigest
{
    private ulong _m0;
    private ulong _m1;
    private ulong _m2;

    internal void Add(int g)
    {
        _m0 |= 1UL << ((g >> 4) & 63);
        _m1 |= 1UL << (g & 63);
        _m2 |= 1UL << ((g >> 6) & 63);
    }

    internal bool AddRange(int a, int b)
    {
        if (_m0 == ulong.MaxValue && _m1 == ulong.MaxValue && _m2 == ulong.MaxValue)
        {
            return false;
        }

        _m0 = AddRangeMask(_m0, a, b, 4);
        _m1 = AddRangeMask(_m1, a, b, 0);
        _m2 = AddRangeMask(_m2, a, b, 6);
        return true;
    }

    internal readonly bool MayIntersect(in SetDigest o) =>
        (_m0 & o._m0) != 0 && (_m1 & o._m1) != 0 && (_m2 & o._m2) != 0;

    internal readonly bool MayHave(int g) =>
        (_m0 & (1UL << ((g >> 4) & 63))) != 0 &&
        (_m1 & (1UL << (g & 63))) != 0 &&
        (_m2 & (1UL << ((g >> 6) & 63))) != 0;

    private static ulong AddRangeMask(ulong mask, int a, int b, int shift)
    {
        uint ua = (uint)a;
        uint ub = (uint)b;
        if ((ub >> shift) - (ua >> shift) >= 63)
        {
            return ulong.MaxValue;
        }

        ulong ma = 1UL << (int)((ua >> shift) & 63);
        ulong mb = 1UL << (int)((ub >> shift) & 63);
        return mask | (mb + (mb - ma) - (mb < ma ? 1UL : 0UL));
    }
}

/// <summary>The GDEF table: glyph classes, mark attachment classes and mark glyph sets.</summary>
internal sealed class GdefTable
{
    private readonly byte[] _d;
    private readonly int _classDef;
    private readonly int _markAttachClassDef;
    private readonly List<int> _markSets = new List<int>();

    internal GdefTable(byte[] d, int offset)
    {
        _d = d;
        if (offset < 0 || offset + 12 > d.Length)
        {
            return;
        }

        int major = OtReader.U16(d, offset);
        int minor = OtReader.U16(d, offset + 2);
        if (major != 1)
        {
            return;
        }

        int cd = OtReader.U16(d, offset + 4);
        int mac = OtReader.U16(d, offset + 10);
        _classDef = cd != 0 ? offset + cd : 0;
        _markAttachClassDef = mac != 0 ? offset + mac : 0;
        if (minor >= 2 && offset + 14 <= d.Length)
        {
            int ms = OtReader.U16(d, offset + 12);
            if (ms != 0)
            {
                int setsOff = offset + ms;
                int count = OtReader.U16(d, setsOff + 2);
                for (int i = 0; i < count; i++)
                {
                    _markSets.Add(setsOff + (int)(uint)OtReader.U32(d, setsOff + 4 + (i * 4)));
                }
            }
        }
    }

    /// <summary>Gets whether the font assigns glyph classes.</summary>
    internal bool HasGlyphClasses => _classDef != 0;

    /// <summary>Returns the glyph props for a glyph (class flags, mark attachment class in the high byte).</summary>
    internal int GetGlyphProps(int glyph)
    {
        int klass = OtCommon.ClassOf(_d, _classDef, glyph);
        return klass switch
        {
            1 => GlyphProps.BaseGlyph,
            2 => GlyphProps.Ligature,
            3 => GlyphProps.Mark | (OtCommon.ClassOf(_d, _markAttachClassDef, glyph) << 8),
            _ => 0,
        };
    }

    internal bool MarkSetCovers(int setIndex, int glyph) =>
        setIndex < _markSets.Count && OtCommon.Coverage(_d, _markSets[setIndex], glyph) != OtCommon.NotCovered;

    /// <summary>Clears the mark filtering set flag when the set index is invalid.</summary>
    internal int SanitizeLookupProps(int lookupProps)
    {
        if ((lookupProps & LookupFlag.UseMarkFilteringSet) != 0 && (lookupProps >>> 16) >= _markSets.Count)
        {
            lookupProps &= ~LookupFlag.UseMarkFilteringSet;
        }

        return lookupProps;
    }
}

/// <summary>Lookup flag bits.</summary>
internal static class LookupFlag
{
    internal const int RightToLeft = 0x0001;
    internal const int IgnoreBaseGlyphs = 0x0002;
    internal const int IgnoreLigatures = 0x0004;
    internal const int IgnoreMarks = 0x0008;
    internal const int IgnoreFlags = 0x000E;
    internal const int UseMarkFilteringSet = 0x0010;
    internal const int MarkAttachmentType = 0xFF00;
}

/// <summary>A parsed lookup: its type, props and subtable offsets (extensions resolved).</summary>
internal sealed class OtLookup
{
    internal OtLookup(int type, int props, int[] subtables, SetDigest digest)
    {
        LookupType = type;
        Props = props;
        Subtables = subtables;
        Digest = digest;
    }

    internal int LookupType { get; }

    /// <summary>Gets the lookup flag, with the mark filtering set index in the high 16 bits.</summary>
    internal int Props { get; }

    internal int[] Subtables { get; }

    internal SetDigest Digest { get; }
}

/// <summary>A GSUB or GPOS table: script/language/feature selection and lookups.</summary>
internal sealed class OtLayoutTable
{
    private readonly byte[] _d;
    private readonly int _scriptList;
    private readonly int _featureList;
    private readonly int _lookupList;
    private readonly OtLookup?[] _lookups;
    private readonly bool _isGsub;

    internal OtLayoutTable(byte[] d, int offset, bool isGsub)
    {
        _d = d;
        _isGsub = isGsub;
        if (offset < 0 || offset + 10 > d.Length || OtReader.U16(d, offset) != 1)
        {
            _lookups = System.Array.Empty<OtLookup?>();
            return;
        }

        _scriptList = offset + OtReader.U16(d, offset + 4);
        _featureList = offset + OtReader.U16(d, offset + 6);
        _lookupList = offset + OtReader.U16(d, offset + 8);
        Present = true;
        _lookups = new OtLookup?[OtReader.U16(d, _lookupList)];
        if (OtReader.U16(d, offset + 2) >= 1 && offset + 14 <= d.Length)
        {
            int fv = (int)(uint)OtReader.U32(d, offset + 10);
            if (fv != 0)
            {
                _featureSubstitutions = FindFeatureVariations(offset + fv);
            }
        }
    }

    // FeatureTableSubstitution offset of the first FeatureVariations record whose
    // conditions hold at the default instance (all coordinates 0), or 0.
    private readonly int _featureSubstitutions;

    private int FindFeatureVariations(int fv)
    {
        byte[] d = _d;
        if (fv + 8 > d.Length || OtReader.U16(d, fv) != 1)
        {
            return 0;
        }

        int count = (int)(uint)OtReader.U32(d, fv + 4);
        for (int i = 0; i < count; i++)
        {
            int rec = fv + 8 + (i * 8);
            int conditionSet = (int)(uint)OtReader.U32(d, rec);
            int substitution = (int)(uint)OtReader.U32(d, rec + 4);
            if (conditionSet == 0 || ConditionSetHolds(fv + conditionSet))
            {
                return substitution == 0 ? 0 : fv + substitution;
            }
        }

        return 0;
    }

    private bool ConditionSetHolds(int set)
    {
        int count = OtReader.U16(_d, set);
        for (int i = 0; i < count; i++)
        {
            int c = (int)(uint)OtReader.U32(_d, set + 2 + (i * 4));
            if (c != 0 && !ConditionHolds(set + c, 64))
            {
                return false;
            }
        }

        return true;
    }

    // Conditions evaluated at the default instance: every axis coordinate is 0
    // and every variation delta is 0.
    private bool ConditionHolds(int c, int depth)
    {
        if (depth == 0)
        {
            return false;
        }

        byte[] d = _d;
        switch (OtReader.U16(d, c))
        {
            case 1:
                return OtReader.I16(d, c + 4) <= 0 && 0 <= OtReader.I16(d, c + 6);
            case 2:
                return OtReader.I16(d, c + 2) > 0;
            case 3:
            case 4:
                {
                    bool and = OtReader.U16(d, c) == 3;
                    int n = OtReader.U8(d, c + 2);
                    for (int i = 0; i < n; i++)
                    {
                        int o = OtReader.U24(d, c + 3 + (i * 3));
                        bool v = o == 0 || ConditionHolds(c + o, depth - 1);
                        if (and && !v)
                        {
                            return false;
                        }

                        if (!and && v)
                        {
                            return true;
                        }
                    }

                    return and;
                }

            case 5:
                {
                    int o = OtReader.U24(d, c + 2);
                    return !(o == 0 || ConditionHolds(c + o, depth - 1));
                }

            default:
                return false;
        }
    }

    // The feature table to use for a feature index (a FeatureVariations substitute when one applies).
    private int FeatureTableOffset(int featureIndex)
    {
        if (_featureSubstitutions != 0)
        {
            int count = OtReader.U16(_d, _featureSubstitutions + 4);
            for (int i = 0; i < count; i++)
            {
                int rec = _featureSubstitutions + 6 + (i * 6);
                if (OtReader.U16(_d, rec) == featureIndex)
                {
                    return _featureSubstitutions + (int)(uint)OtReader.U32(_d, rec + 2);
                }
            }
        }

        return _featureList + OtReader.U16(_d, _featureList + 2 + (featureIndex * 6) + 4);
    }

    internal bool Present { get; }

    internal byte[] Data => _d;

    internal int LookupCount => _lookups.Length;

    /// <summary>Finds a script by tag; returns its index or -1.</summary>
    internal int FindScript(string tag)
    {
        if (!Present)
        {
            return -1;
        }

        int count = OtReader.U16(_d, _scriptList);
        for (int i = 0; i < count; i++)
        {
            if (TagIs(_scriptList + 2 + (i * 6), tag))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Finds a language system of a script by tag; returns its index or -1 (default).</summary>
    internal int FindLanguage(int scriptIndex, string tag)
    {
        int script = ScriptOffset(scriptIndex);
        if (script < 0)
        {
            return -1;
        }

        int count = OtReader.U16(_d, script + 2);
        for (int i = 0; i < count; i++)
        {
            if (TagIs(script + 4 + (i * 6), tag))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Returns the LangSys offset for a script and language index (-1 = default), or -1 when absent.</summary>
    internal int LangSysOffset(int scriptIndex, int languageIndex)
    {
        int script = ScriptOffset(scriptIndex);
        if (script < 0)
        {
            return -1;
        }

        if (languageIndex < 0)
        {
            int def = OtReader.U16(_d, script);
            return def != 0 ? script + def : -1;
        }

        return script + OtReader.U16(_d, script + 4 + (languageIndex * 6) + 4);
    }

    /// <summary>The required feature index of a LangSys, or -1.</summary>
    internal int RequiredFeature(int langSys)
    {
        if (langSys < 0)
        {
            return -1;
        }

        int r = OtReader.U16(_d, langSys + 2);
        return r == 0xFFFF ? -1 : r;
    }

    /// <summary>Maps feature tags to feature indices for a LangSys (earlier entries win).</summary>
    internal Dictionary<string, int> FeatureMap(int langSys)
    {
        Dictionary<string, int> map = new Dictionary<string, int>(System.StringComparer.Ordinal);
        if (langSys < 0)
        {
            return map;
        }

        int count = OtReader.U16(_d, langSys + 4);
        for (int i = count - 1; i >= 0; i--)
        {
            int fi = OtReader.U16(_d, langSys + 6 + (i * 2));
            map[FeatureTag(fi)] = fi;
        }

        return map;
    }

    /// <summary>Finds a feature by tag anywhere in the FeatureList; returns its index or -1.</summary>
    internal int FindFeatureAnywhere(string tag)
    {
        if (!Present)
        {
            return -1;
        }

        int count = OtReader.U16(_d, _featureList);
        for (int i = 0; i < count; i++)
        {
            if (TagIs(_featureList + 2 + (i * 6), tag))
            {
                return i;
            }
        }

        return -1;
    }

    internal string FeatureTag(int featureIndex)
    {
        int r = _featureList + 2 + (featureIndex * 6);
        return new string(new[] { (char)OtReader.U8(_d, r), (char)OtReader.U8(_d, r + 1), (char)OtReader.U8(_d, r + 2), (char)OtReader.U8(_d, r + 3) });
    }

    /// <summary>The lookup indices of a feature.</summary>
    internal List<int> FeatureLookups(int featureIndex)
    {
        List<int> list = new List<int>();
        if (featureIndex < 0 || featureIndex >= OtReader.U16(_d, _featureList))
        {
            return list;
        }

        int f = FeatureTableOffset(featureIndex);
        int count = OtReader.U16(_d, f + 2);
        for (int i = 0; i < count; i++)
        {
            list.Add(OtReader.U16(_d, f + 4 + (i * 2)));
        }

        return list;
    }

    /// <summary>Returns a parsed lookup (cached), or null when out of range.</summary>
    internal OtLookup? GetLookup(int index)
    {
        if (index < 0 || index >= _lookups.Length)
        {
            return null;
        }

        OtLookup? l = _lookups[index];
        if (l is null)
        {
            l = ParseLookup(index);
            _lookups[index] = l;
        }

        return l;
    }

    private OtLookup ParseLookup(int index)
    {
        int l = _lookupList + OtReader.U16(_d, _lookupList + 2 + (index * 2));
        int type = OtReader.U16(_d, l);
        int flag = OtReader.U16(_d, l + 2);
        int count = OtReader.U16(_d, l + 4);
        int extensionType = _isGsub ? 7 : 9;
        int[] subtables = new int[count];
        int realType = type;
        for (int i = 0; i < count; i++)
        {
            int st = l + OtReader.U16(_d, l + 6 + (i * 2));
            if (type == extensionType)
            {
                realType = OtReader.U16(_d, st + 2);
                st += (int)(uint)OtReader.U32(_d, st + 4);
            }

            subtables[i] = st;
        }

        int props = flag;
        if ((flag & LookupFlag.UseMarkFilteringSet) != 0)
        {
            props |= OtReader.U16(_d, l + 6 + (count * 2)) << 16;
        }

        SetDigest digest = default;
        foreach (int st in subtables)
        {
            OtCommon.CollectCoverage(_d, SubtableCoverage(realType, st), ref digest);
        }

        return new OtLookup(realType, props, subtables, digest);
    }

    // The coverage HarfBuzz's get_coverage() returns for a subtable.
    private int SubtableCoverage(int type, int st)
    {
        int format = OtReader.U16(_d, st);
        bool context = _isGsub ? type == 5 : type == 7;
        bool chain = _isGsub ? type == 6 : type == 8;
        if (context && format == 3)
        {
            return OtReader.U16(_d, st + 2) == 0 ? 0 : st + OtReader.U16(_d, st + 6);
        }

        if (chain && format == 3)
        {
            int backtrack = OtReader.U16(_d, st + 2);
            int input = st + 4 + (backtrack * 2);
            return OtReader.U16(_d, input) == 0 ? 0 : st + OtReader.U16(_d, input + 2);
        }

        return st + OtReader.U16(_d, st + 2);
    }

    private int ScriptOffset(int scriptIndex)
    {
        if (!Present || scriptIndex < 0 || scriptIndex >= OtReader.U16(_d, _scriptList))
        {
            return -1;
        }

        return _scriptList + OtReader.U16(_d, _scriptList + 2 + (scriptIndex * 6) + 4);
    }

    private bool TagIs(int offset, string tag) =>
        OtReader.U8(_d, offset) == (byte)tag[0] && OtReader.U8(_d, offset + 1) == (byte)tag[1] &&
        OtReader.U8(_d, offset + 2) == (byte)tag[2] && OtReader.U8(_d, offset + 3) == (byte)tag[3];
}
