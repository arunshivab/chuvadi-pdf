// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Port of HarfBuzz's Indic shaper (hb-ot-shaper-indic.cc,
// hb-ot-shaper-indic-machine.rl, hb-ot-shaper-syllabic.cc and the Indic
// part of hb-ot-shaper-vowel-constraints.cc); see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Text.Shaping.OpenType;
using Chuvadi.Pdf.Text.Shaping.Unicode;

namespace Chuvadi.Pdf.Text.Shaping.Indic;

/// <summary>The Indic shaping engine: syllables, initial and final reordering, masks.</summary>
internal sealed class IndicShaper
{
    // Categories (indic_syllable_machine_ex_*).
    internal const int CatX = 0;
    internal const int CatC = 1;
    internal const int CatV = 2;
    internal const int CatN = 3;
    internal const int CatH = 4;
    internal const int CatZwnj = 5;
    internal const int CatZwj = 6;
    internal const int CatM = 7;
    internal const int CatSM = 8;
    internal const int CatA = 9;
    internal const int CatPlaceholder = 10;
    internal const int CatDottedCircle = 11;
    internal const int CatRS = 12;
    internal const int CatMPst = 13;
    internal const int CatRepha = 14;
    internal const int CatRa = 15;
    internal const int CatCM = 16;
    internal const int CatSymbol = 17;
    internal const int CatCS = 18;
    internal const int CatSMPst = 57;

    // Positions.
    internal const int PosStart = 0;
    internal const int PosRaToBecomeReph = 1;
    internal const int PosPreM = 2;
    internal const int PosPreC = 3;
    internal const int PosBaseC = 4;
    internal const int PosAfterMain = 5;
    internal const int PosAboveC = 6;
    internal const int PosBeforeSub = 7;
    internal const int PosBelowC = 8;
    internal const int PosAfterSub = 9;
    internal const int PosBeforePost = 10;
    internal const int PosPostC = 11;
    internal const int PosAfterPost = 12;
    internal const int PosSmvd = 13;
    internal const int PosEnd = 14;

    // Syllable types.
    private const int ConsonantSyllable = 0;
    private const int VowelSyllable = 1;
    private const int StandaloneCluster = 2;
    private const int BrokenCluster = 4;

    // Feature indices into the mask array.
    private const int Rphf = 2;
    private const int Pref = 4;
    private const int Blwf = 5;
    private const int Abvf = 6;
    private const int Half = 7;
    private const int Pstf = 8;
    private const int Init = 11;

    private const int MaxSyllableLength = 64;

    private static readonly (string Tag, FeatureFlags Flags)[] Features =
    {
        ("nukt", FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        ("akhn", FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        ("rphf", FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        ("rkrf", FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        ("pref", FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        ("blwf", FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        ("abvf", FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        ("half", FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        ("pstf", FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        ("vatu", FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        ("cjct", FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        ("init", FeatureFlags.ManualJoiners | FeatureFlags.PerSyllable),
        ("pres", FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        ("abvs", FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        ("blws", FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        ("psts", FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
        ("haln", FeatureFlags.GlobalManualJoiners | FeatureFlags.PerSyllable),
    };

    private const int BasicFeatures = 11;

    private static readonly (CatPattern Pattern, int SyllableType)[] Grammar = BuildGrammar();
    private static readonly Dfa[] Machines = Array.ConvertAll(Grammar, g => g.Pattern.Compile().ToDfa());

    private readonly LipiScript _script;
    private readonly Config _config;
    private readonly uint[] _maskArray = new uint[Features.Length];
    private List<LookupMap> _rphf = new List<LookupMap>();
    private List<LookupMap> _pref = new List<LookupMap>();
    private List<LookupMap> _blwf = new List<LookupMap>();
    private List<LookupMap> _pstf = new List<LookupMap>();
    private List<LookupMap> _vatu = new List<LookupMap>();
    private bool _zeroContext;
    private int _viramaGlyph;

    internal IndicShaper(LipiScript script)
    {
        _script = script;
        _config = ConfigFor(script);
    }

    internal bool IsOldSpec { get; private set; }

    /// <summary>Adds the Indic stages and features (collect_features + override_features).</summary>
    internal void CollectFeatures(OtMapBuilder map)
    {
        // Do this before any lookups have been applied.
        map.AddGsubPause((plan, buffer) => SetupSyllables(buffer));
        map.EnableFeature("locl", FeatureFlags.PerSyllable);
        map.EnableFeature("ccmp", FeatureFlags.PerSyllable);
        map.AddGsubPause((plan, buffer) => InitialReordering(plan, buffer));
        int i = 0;
        for (; i < BasicFeatures; i++)
        {
            map.AddFeature(Features[i].Tag, Features[i].Flags);
            map.AddGsubPause(null);
        }

        map.AddGsubPause((plan, buffer) => FinalReordering(buffer));
        for (; i < Features.Length; i++)
        {
            map.AddFeature(Features[i].Tag, Features[i].Flags);
        }
    }

    internal static void OverrideFeatures(OtMapBuilder map)
    {
        map.DisableFeature("liga");
        map.AddGsubPause(null);
    }

    /// <summary>Creates the plan data (data_create_indic).</summary>
    internal void CreateData(ShapePlan plan)
    {
        IsOldSpec = _config.HasOldSpec && !(plan.Map.ChosenScript[0] ?? string.Empty).EndsWith('2');
        _zeroContext = !IsOldSpec && _script != LipiScript.Malayalam;
        _rphf = plan.Map.GetStageLookups(0, plan.Map.GetFeatureStage(0, "rphf"));
        _pref = plan.Map.GetStageLookups(0, plan.Map.GetFeatureStage(0, "pref"));
        _blwf = plan.Map.GetStageLookups(0, plan.Map.GetFeatureStage(0, "blwf"));
        _pstf = plan.Map.GetStageLookups(0, plan.Map.GetFeatureStage(0, "pstf"));
        _vatu = plan.Map.GetStageLookups(0, plan.Map.GetFeatureStage(0, "vatu"));
        for (int i = 0; i < Features.Length; i++)
        {
            _maskArray[i] = (Features[i].Flags & FeatureFlags.Global) != 0 ? 0 : plan.Map.GetOneMask(Features[i].Tag);
        }

        _viramaGlyph = _config.Virama != 0 ? plan.NominalGlyph(_config.Virama) : 0;
    }

    /// <summary>Sets the Indic category and position of each character (setup_masks_indic).</summary>
    internal static void SetupMasks(ShapeBuffer buffer)
    {
        for (int i = 0; i < buffer.Len; i++)
        {
            int type = UnicodeData.IndicCategories(buffer.Info[i].Codepoint);
            buffer.Info[i].ShaperCategory = type & 0xFF;
            buffer.Info[i].ShaperPosition = type >> 8;
        }
    }

    internal static bool Decompose(int ab, out int a, out int b)
    {
        if (ab is 0x0931 or 0x09DC or 0x09DD or 0x0B94)
        {
            a = ab;
            b = 0;
            return false;
        }

        return UnicodeData.Decompose(ab, out a, out b);
    }

    internal static bool Compose(int a, int b, out int ab)
    {
        // Avoid recomposing split matras.
        if (UnicodeData.IsMarkCategory(UnicodeData.GeneralCategory(a)))
        {
            ab = 0;
            return false;
        }

        // Composition-exclusion exceptions that we want to recompose.
        if (a == 0x09AF && b == 0x09BC)
        {
            ab = 0x09DF;
            return true;
        }

        return UnicodeData.Compose(a, b, out ab);
    }

    /// <summary>Inserts dotted circles inside vowel sequences that look like other vowels.</summary>
    internal void PreprocessText(ShapeBuffer buffer)
    {
        buffer.ClearOutput();
        int count = buffer.Len;
        for (buffer.Idx = 0; buffer.Idx + 1 < count;)
        {
            bool matched = false;
            int cur = buffer.Cur().Codepoint;
            int next = buffer.Cur(1).Codepoint;
            switch (_script)
            {
                case LipiScript.Devanagari:
                    switch (cur)
                    {
                        case 0x0905:
                            matched = next is 0x093A or 0x093B or 0x093E or 0x0945 or 0x0946 or 0x0949 or 0x094A or
                                0x094B or 0x094C or 0x094F or 0x0956 or 0x0957;
                            break;
                        case 0x0906:
                            matched = next is 0x093A or 0x0945 or 0x0946 or 0x0947 or 0x0948;
                            break;
                        case 0x0909:
                            matched = next == 0x0941;
                            break;
                        case 0x090F:
                            matched = next is 0x0945 or 0x0946 or 0x0947;
                            break;
                        case 0x0930:
                            if (next == 0x094D && buffer.Idx + 2 < count && buffer.Cur(2).Codepoint == 0x0907)
                            {
                                buffer.NextGlyph();
                                matched = true;
                            }

                            break;
                    }

                    break;
                case LipiScript.Bengali:
                    matched = (cur == 0x0985 && next == 0x09BE) || (cur == 0x098B && next == 0x09C3) ||
                        (cur == 0x098C && next == 0x09E2);
                    break;
                case LipiScript.Gurmukhi:
                    matched = (cur == 0x0A05 && next is 0x0A3E or 0x0A48 or 0x0A4C) ||
                        (cur == 0x0A72 && next is 0x0A3F or 0x0A40 or 0x0A47) ||
                        (cur == 0x0A73 && next is 0x0A41 or 0x0A42 or 0x0A4B);
                    break;
                case LipiScript.Gujarati:
                    matched = (cur == 0x0A85 && next is 0x0ABE or 0x0AC5 or 0x0AC7 or 0x0AC8 or 0x0AC9 or 0x0ACB or 0x0ACC) ||
                        (cur == 0x0AC5 && next == 0x0ABE);
                    break;
                case LipiScript.Odia:
                    matched = (cur == 0x0B05 && next == 0x0B3E) || (cur is 0x0B0F or 0x0B13 && next == 0x0B57);
                    break;
                case LipiScript.Tamil:
                    matched = cur == 0x0B85 && next == 0x0BC2;
                    break;
                case LipiScript.Telugu:
                    matched = (cur == 0x0C12 && next is 0x0C4C or 0x0C55) || (cur is 0x0C3F or 0x0C46 or 0x0C4A && next == 0x0C55);
                    break;
                case LipiScript.Kannada:
                    matched = (cur is 0x0C89 or 0x0C8B && next == 0x0CBE) || (cur == 0x0C92 && next == 0x0CCC);
                    break;
                case LipiScript.Malayalam:
                    matched = (cur is 0x0D07 or 0x0D09 && next == 0x0D57) || (cur == 0x0D0E && next == 0x0D46) ||
                        (cur == 0x0D12 && next is 0x0D3E or 0x0D57);
                    break;
            }

            buffer.NextGlyph();
            if (matched)
            {
                buffer.OutputGlyph(0x25CC);
                buffer.Prev().UnicodeProps &= ~UProps.Continuation;
                buffer.NextGlyph();
            }
        }

        buffer.Sync();
    }

    private static (CatPattern, int)[] BuildGrammar()
    {
        CatPattern c = CatPattern.Cat(CatC, CatRa);
        CatPattern n = CatPattern.Seq(
            CatPattern.Seq(CatPattern.Cat(CatZwnj).Opt(), CatPattern.Cat(CatRS)).Opt(),
            CatPattern.Seq(CatPattern.Cat(CatN), CatPattern.Cat(CatN).Opt()).Opt());
        CatPattern z = CatPattern.Cat(CatZwj, CatZwnj);
        CatPattern reph = CatPattern.Alt(CatPattern.Seq(CatPattern.Cat(CatRa), CatPattern.Cat(CatH)), CatPattern.Cat(CatRepha));
        CatPattern sm = CatPattern.Cat(CatSM, CatSMPst);
        CatPattern cn = CatPattern.Seq(c, CatPattern.Cat(CatZwj).Opt(), n.Opt());
        CatPattern symbol = CatPattern.Seq(CatPattern.Cat(CatSymbol), CatPattern.Cat(CatN).Opt());
        CatPattern matraGroup = CatPattern.Seq(
            z.Star(),
            CatPattern.Alt(CatPattern.Cat(CatM), CatPattern.Seq(sm.Opt(), CatPattern.Cat(CatMPst))),
            CatPattern.Cat(CatN).Opt(),
            CatPattern.Cat(CatH).Opt());
        CatPattern syllableTail = CatPattern.Seq(
            CatPattern.Seq(z.Opt(), sm, sm.Opt(), CatPattern.Cat(CatZwnj).Opt()).Opt(),
            CatPattern.Cat(CatA).Star());
        CatPattern halantGroup = CatPattern.Seq(
            z.Opt(),
            CatPattern.Cat(CatH),
            CatPattern.Seq(CatPattern.Cat(CatZwj), CatPattern.Cat(CatN).Opt()).Opt());
        CatPattern finalHalantGroup = CatPattern.Alt(halantGroup, CatPattern.Seq(CatPattern.Cat(CatH), CatPattern.Cat(CatZwnj)));
        CatPattern medialGroup = CatPattern.Cat(CatCM).Opt();
        CatPattern halantOrMatraGroup = CatPattern.Alt(finalHalantGroup, matraGroup.Star());
        CatPattern complexSyllableTail = CatPattern.Seq(
            CatPattern.Seq(halantGroup, cn).Star(),
            medialGroup,
            halantOrMatraGroup,
            syllableTail);

        CatPattern consonantSyllable = CatPattern.Seq(CatPattern.Cat(CatRepha, CatCS).Opt(), cn, complexSyllableTail);
        CatPattern vowelSyllable = CatPattern.Seq(
            reph.Opt(),
            CatPattern.Cat(CatV),
            n.Opt(),
            CatPattern.Alt(CatPattern.Cat(CatZwj), complexSyllableTail));
        CatPattern standaloneCluster = CatPattern.Seq(
            CatPattern.Alt(
                CatPattern.Seq(CatPattern.Cat(CatRepha, CatCS).Opt(), CatPattern.Cat(CatPlaceholder)),
                CatPattern.Seq(reph.Opt(), CatPattern.Cat(CatDottedCircle))),
            n.Opt(),
            complexSyllableTail);
        CatPattern symbolCluster = CatPattern.Seq(symbol, syllableTail);
        CatPattern brokenCluster = CatPattern.Seq(reph.Opt(), n.Opt(), complexSyllableTail);

        return new (CatPattern, int)[]
        {
            (consonantSyllable, ConsonantSyllable),
            (vowelSyllable, VowelSyllable),
            (standaloneCluster, StandaloneCluster),
            (symbolCluster, 3),
            (CatPattern.Cat(CatSMPst), 5),
            (brokenCluster, BrokenCluster),
            (CatPattern.Any(), 5),
        };
    }

    // find_syllables_indic.
    private static bool SetupSyllables(ShapeBuffer buffer)
    {
        List<int> cats = new List<int>(buffer.Len);
        for (int i = 0; i < buffer.Len; i++)
        {
            cats.Add(buffer.Info[i].ShaperCategory);
        }

        int serial = 1;
        int p = 0;
        while (p < cats.Count)
        {
            int bestLen = 0;
            int bestType = 5;
            for (int k = 0; k < Machines.Length; k++)
            {
                int len = Machines[k].LongestMatch(cats, p);
                if (len > bestLen)
                {
                    bestLen = len;
                    bestType = Grammar[k].SyllableType;
                }
            }

            if (bestLen == 0)
            {
                bestLen = 1;
                bestType = 5;
            }

            if (bestType == BrokenCluster)
            {
                buffer.ScratchFlags |= ShapeBuffer.ScratchHasBrokenSyllable;
            }

            for (int i = p; i < p + bestLen; i++)
            {
                buffer.Info[i].Syllable = (serial << 4) | bestType;
            }

            serial++;
            if (serial == 16)
            {
                serial = 1;
            }

            p += bestLen;
        }

        return false;
    }

    private static int NextSyllable(ShapeBuffer buffer, int start)
    {
        int count = start + Math.Min(buffer.Len - start, MaxSyllableLength);
        int syllable = buffer.Info[start].Syllable;
        while (++start < count && syllable == buffer.Info[start].Syllable)
        {
        }

        return start;
    }

    private static bool IsOneOf(in GlyphInfo info, int flags)
    {
        // If it ligated, all bets are off.
        if ((info.GlyphProps & GlyphProps.Ligated) != 0)
        {
            return false;
        }

        return info.ShaperCategory < 32 && ((1 << info.ShaperCategory) & flags) != 0;
    }

    private static int Flag(int c) => 1 << c;

    private static readonly int ConsonantFlags =
        Flag(CatC) | Flag(CatCS) | Flag(CatRa) | Flag(CatCM) | Flag(CatV) | Flag(CatPlaceholder) | Flag(CatDottedCircle);

    private static readonly int JoinerFlags = Flag(CatZwj) | Flag(CatZwnj);

    private static bool IsConsonant(in GlyphInfo info) => IsOneOf(info, ConsonantFlags);

    private static bool IsJoiner(in GlyphInfo info) => IsOneOf(info, JoinerFlags);

    private static bool IsHalant(in GlyphInfo info) => IsOneOf(info, Flag(CatH));

    private static bool FlagHas(int value, int flags) => value < 32 && ((1 << value) & flags) != 0;

    private bool WouldSubstitute(ShapePlan plan, List<LookupMap> lookups, ReadOnlySpan<int> glyphs)
    {
        foreach (LookupMap l in lookups)
        {
            if (OtApplyContext.WouldSubstitute(plan.Gsub, l.Index, glyphs, _zeroContext))
            {
                return true;
            }
        }

        return false;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _consonantPositions =
        new System.Collections.Concurrent.ConcurrentDictionary<int, int>();

    // The position depends only on the glyph (the virama is fixed per plan): cached.
    private int ConsonantPositionFromFace(ShapePlan plan, int consonant, int virama) =>
        _consonantPositions.GetOrAdd(consonant, g => ComputeConsonantPosition(plan, g, virama));

    private int ComputeConsonantPosition(ShapePlan plan, int consonant, int virama)
    {
        ReadOnlySpan<int> glyphs = stackalloc int[] { virama, consonant, virama };
        if (WouldSubstitute(plan, _blwf, glyphs[..2]) || WouldSubstitute(plan, _blwf, glyphs[1..]) ||
            WouldSubstitute(plan, _vatu, glyphs[..2]) || WouldSubstitute(plan, _vatu, glyphs[1..]))
        {
            return PosBelowC;
        }

        if (WouldSubstitute(plan, _pstf, glyphs[..2]) || WouldSubstitute(plan, _pstf, glyphs[1..]))
        {
            return PosPostC;
        }

        if (WouldSubstitute(plan, _pref, glyphs[..2]) || WouldSubstitute(plan, _pref, glyphs[1..]))
        {
            return PosPostC;
        }

        return PosBaseC;
    }

    private bool InitialReordering(ShapePlan plan, ShapeBuffer buffer)
    {
        // update_consonant_positions_indic.
        if (_viramaGlyph != 0)
        {
            for (int i = 0; i < buffer.Len; i++)
            {
                if (buffer.Info[i].ShaperPosition == PosBaseC)
                {
                    buffer.Info[i].ShaperPosition = ConsonantPositionFromFace(plan, buffer.Info[i].Codepoint, _viramaGlyph);
                }
            }
        }

        bool ret = InsertDottedCircles(plan, buffer);
        for (int start = 0, end = buffer.Len > 0 ? NextSyllable(buffer, 0) : 0; start < buffer.Len; start = end, end = start < buffer.Len ? NextSyllable(buffer, start) : start)
        {
            int type = buffer.Info[start].Syllable & 0x0F;
            if (type is ConsonantSyllable or VowelSyllable or BrokenCluster or StandaloneCluster)
            {
                InitialReorderingConsonantSyllable(plan, buffer, start, end);
            }
        }

        return ret;
    }

    // hb_syllabic_insert_dotted_circles.
    private static bool InsertDottedCircles(ShapePlan plan, ShapeBuffer buffer)
    {
        if ((buffer.ScratchFlags & ShapeBuffer.ScratchHasBrokenSyllable) == 0)
        {
            return false;
        }

        int dottedCircleGlyph = plan.NominalGlyph(0x25CC);
        if (dottedCircleGlyph == 0)
        {
            return false;
        }

        GlyphInfo dottedCircle = new GlyphInfo
        {
            Codepoint = dottedCircleGlyph,
            ShaperCategory = CatDottedCircle,
            ShaperPosition = PosEnd,
        };

        buffer.ClearOutput();
        buffer.Idx = 0;
        int lastSyllable = 0;
        while (buffer.Idx < buffer.Len)
        {
            int syllable = buffer.Cur().Syllable;
            if (lastSyllable != syllable && (syllable & 0x0F) == BrokenCluster)
            {
                lastSyllable = syllable;
                GlyphInfo g = dottedCircle;
                g.Cluster = buffer.Cur().Cluster;
                g.Mask = buffer.Cur().Mask;
                g.Syllable = buffer.Cur().Syllable;

                // Insert dottedcircle after possible Repha.
                while (buffer.Idx < buffer.Len && lastSyllable == buffer.Cur().Syllable && buffer.Cur().ShaperCategory == CatRepha)
                {
                    buffer.NextGlyph();
                }

                buffer.OutputInfo(g);
            }
            else
            {
                buffer.NextGlyph();
            }
        }

        buffer.Sync();
        return true;
    }

    private void InitialReorderingConsonantSyllable(ShapePlan plan, ShapeBuffer buffer, int start, int end)
    {
        GlyphInfo[] info = buffer.Info;

        // For compatibility with legacy usage in Kannada, Ra+h+ZWJ must behave like Ra+ZWJ+h...
        if (_script == LipiScript.Kannada && start + 3 <= end &&
            IsOneOf(info[start], Flag(CatRa)) && IsOneOf(info[start + 1], Flag(CatH)) && IsOneOf(info[start + 2], Flag(CatZwj)))
        {
            buffer.MergeClusters(start + 1, start + 3);
            (info[start + 1], info[start + 2]) = (info[start + 2], info[start + 1]);
        }

        // 1. Find base consonant.
        int baseIdx = end;
        bool hasReph = false;
        {
            int limit = start;
            if (_maskArray[Rphf] != 0 && start + 3 <= end &&
                ((_config.RephMode == RephMode.Implicit && !IsJoiner(info[start + 2])) ||
                 (_config.RephMode == RephMode.Explicit && info[start + 2].ShaperCategory == CatZwj)))
            {
                // See if it matches the 'rphf' feature.
                ReadOnlySpan<int> glyphs = stackalloc int[]
                {
                    info[start].Codepoint,
                    info[start + 1].Codepoint,
                    _config.RephMode == RephMode.Explicit ? info[start + 2].Codepoint : 0,
                };
                if (WouldSubstitute(plan, _rphf, glyphs[..2]) ||
                    (_config.RephMode == RephMode.Explicit && WouldSubstitute(plan, _rphf, glyphs)))
                {
                    limit += 2;
                    while (limit < end && IsJoiner(info[limit]))
                    {
                        limit++;
                    }

                    baseIdx = start;
                    hasReph = true;
                }
            }
            else if (_config.RephMode == RephMode.LogRepha && info[start].ShaperCategory == CatRepha)
            {
                limit += 1;
                while (limit < end && IsJoiner(info[limit]))
                {
                    limit++;
                }

                baseIdx = start;
                hasReph = true;
            }

            {
                // -> starting from the end of the syllable, move backwards
                int i = end;
                bool seenBelow = false;
                do
                {
                    i--;

                    // -> until a consonant is found
                    if (IsConsonant(info[i]))
                    {
                        // -> that does not have a below-base or post-base form
                        if (info[i].ShaperPosition != PosBelowC &&
                            (info[i].ShaperPosition != PosPostC || seenBelow))
                        {
                            baseIdx = i;
                            break;
                        }

                        if (info[i].ShaperPosition == PosBelowC)
                        {
                            seenBelow = true;
                        }

                        // -> or arrive at the first consonant. The consonant stopped at will be the base.
                        baseIdx = i;
                    }
                    else if (start < i && info[i].ShaperCategory == CatZwj && info[i - 1].ShaperCategory == CatH)
                    {
                        // A ZWJ after a Halant stops the base search, and requests an explicit half form.
                        break;
                    }
                }
                while (i > limit);
            }

            // Have no other consonant, so Reph is not formed and Ra becomes base.
            if (hasReph && baseIdx == start && limit - baseIdx <= 2)
            {
                hasReph = false;
            }
        }

        // Reorder characters.
        for (int i = start; i < baseIdx; i++)
        {
            info[i].ShaperPosition = Math.Min(PosPreC, info[i].ShaperPosition);
        }

        if (baseIdx < end)
        {
            info[baseIdx].ShaperPosition = PosBaseC;
        }

        // Handle beginning Ra.
        if (hasReph)
        {
            info[start].ShaperPosition = PosRaToBecomeReph;
        }

        // For old-style Indic script tags, move the first post-base Halant after last consonant.
        if (IsOldSpec)
        {
            bool disallowDoubleHalants = _script == LipiScript.Kannada;
            for (int i = baseIdx + 1; i < end; i++)
            {
                if (info[i].ShaperCategory == CatH)
                {
                    int j;
                    for (j = end - 1; j > i; j--)
                    {
                        if (IsConsonant(info[j]) || (disallowDoubleHalants && info[j].ShaperCategory == CatH))
                        {
                            break;
                        }
                    }

                    if (info[j].ShaperCategory != CatH && j > i)
                    {
                        // Move Halant to after last consonant.
                        GlyphInfo t = info[i];
                        Array.Copy(info, i + 1, info, i, j - i);
                        info[j] = t;
                    }

                    break;
                }
            }
        }

        // Attach misc marks to previous char to move with them.
        {
            int lastPos = PosStart;
            for (int i = start; i < end; i++)
            {
                if (FlagHas(info[i].ShaperCategory, JoinerFlags | Flag(CatN) | Flag(CatRS) | Flag(CatCM) | Flag(CatH)))
                {
                    info[i].ShaperPosition = lastPos;
                    if (info[i].ShaperCategory == CatH && info[i].ShaperPosition == PosPreM)
                    {
                        // Uniscribe doesn't move the Halant with Left Matra.
                        for (int j = i; j > start; j--)
                        {
                            if (info[j - 1].ShaperPosition != PosPreM)
                            {
                                info[i].ShaperPosition = info[j - 1].ShaperPosition;
                                break;
                            }
                        }
                    }
                }
                else if (info[i].ShaperPosition != PosSmvd)
                {
                    if (info[i].ShaperCategory == CatMPst && i > start && info[i - 1].ShaperCategory == CatSM)
                    {
                        info[i - 1].ShaperPosition = info[i].ShaperPosition;
                    }

                    lastPos = info[i].ShaperPosition;
                }
            }
        }

        // For post-base consonants let them own anything before them since the last consonant or matra.
        {
            int last = baseIdx;
            for (int i = baseIdx + 1; i < end; i++)
            {
                if (IsConsonant(info[i]))
                {
                    for (int j = last + 1; j < i; j++)
                    {
                        if (info[j].ShaperPosition < PosSmvd)
                        {
                            info[j].ShaperPosition = info[i].ShaperPosition;
                        }
                    }

                    last = i;
                }
                else if (FlagHas(info[i].ShaperCategory, Flag(CatM) | Flag(CatMPst)))
                {
                    last = i;
                }
            }
        }

        {
            // Use syllable() for sort accounting temporarily.
            int syllable = info[start].Syllable;
            for (int i = start; i < end; i++)
            {
                info[i].Syllable = i - start;
            }

            // Sit tight, rock 'n roll!
            buffer.StableSort(start, end, (a, b) => a.ShaperPosition - b.ShaperPosition);

            // Find base again; also flip left-matra sequence.
            int firstLeftMatra = end;
            int lastLeftMatra = end;
            baseIdx = end;
            for (int i = start; i < end; i++)
            {
                if (info[i].ShaperPosition == PosBaseC)
                {
                    baseIdx = i;
                    break;
                }
                else if (info[i].ShaperPosition == PosPreM)
                {
                    if (firstLeftMatra == end)
                    {
                        firstLeftMatra = i;
                    }

                    lastLeftMatra = i;
                }
            }

            if (firstLeftMatra < lastLeftMatra)
            {
                // No need to merge clusters, handled later.
                buffer.ReverseRange(firstLeftMatra, lastLeftMatra + 1);

                // Reverse back nuktas, etc.
                int i = firstLeftMatra;
                for (int j = i; j <= lastLeftMatra; j++)
                {
                    if (FlagHas(info[j].ShaperCategory, Flag(CatM) | Flag(CatMPst)))
                    {
                        buffer.ReverseRange(i, j + 1);
                        i = j + 1;
                    }
                }
            }

            if (IsOldSpec || end - start > 127)
            {
                buffer.MergeClusters(baseIdx, end);
            }
            else
            {
                // Note!  syllable() is a one-byte field.
                for (int i = baseIdx; i < end; i++)
                {
                    if (info[i].Syllable != 255)
                    {
                        int min = i;
                        int max = i;
                        int j = start + info[i].Syllable;
                        while (j != i)
                        {
                            min = Math.Min(min, j);
                            max = Math.Max(max, j);
                            int next = start + info[j].Syllable;
                            info[j].Syllable = 255; // So we don't process j later again.
                            j = next;
                        }

                        buffer.MergeClusters(Math.Max(baseIdx, min), max + 1);
                    }
                }
            }

            // Put syllable back in.
            for (int i = start; i < end; i++)
            {
                info[i].Syllable = syllable;
            }
        }

        // Setup masks now.
        {
            // Reph.
            for (int i = start; i < end && info[i].ShaperPosition == PosRaToBecomeReph; i++)
            {
                info[i].Mask |= _maskArray[Rphf];
            }

            // Pre-base.
            uint mask = _maskArray[Half];
            if (!IsOldSpec && _config.BlwfMode == BlwfMode.PreAndPost)
            {
                mask |= _maskArray[Blwf];
            }

            for (int i = start; i < baseIdx; i++)
            {
                info[i].Mask |= mask;
            }

            // Post-base.
            mask = _maskArray[Blwf] | _maskArray[Abvf] | _maskArray[Pstf];
            for (int i = baseIdx + 1; i < end; i++)
            {
                info[i].Mask |= mask;
            }
        }

        if (IsOldSpec && _script == LipiScript.Devanagari)
        {
            // Old-spec eye-lash Ra needs special handling.
            for (int i = start; i + 1 < baseIdx; i++)
            {
                if (info[i].ShaperCategory == CatRa && info[i + 1].ShaperCategory == CatH &&
                    (i + 2 == baseIdx || info[i + 2].ShaperCategory != CatZwj))
                {
                    info[i].Mask |= _maskArray[Blwf];
                    info[i + 1].Mask |= _maskArray[Blwf];
                }
            }
        }

        const int prefLen = 2;
        Span<int> prefGlyphs = stackalloc int[prefLen];
        if (_maskArray[Pref] != 0 && baseIdx + prefLen < end)
        {
            // Find a Halant,Ra sequence and mark it for pre-base-reordering processing.
            for (int i = baseIdx + 1; i + prefLen - 1 < end; i++)
            {
                prefGlyphs[0] = info[i].Codepoint;
                prefGlyphs[1] = info[i + 1].Codepoint;
                if (WouldSubstitute(plan, _pref, prefGlyphs))
                {
                    for (int j = 0; j < prefLen; j++)
                    {
                        info[i++].Mask |= _maskArray[Pref];
                    }

                    break;
                }
            }
        }

        // Apply ZWJ/ZWNJ effects.
        for (int i = start + 1; i < end; i++)
        {
            if (IsJoiner(info[i]))
            {
                bool nonJoiner = info[i].ShaperCategory == CatZwnj;
                int j = i;
                do
                {
                    j--;

                    // A ZWNJ disables HALF.
                    if (nonJoiner)
                    {
                        info[j].Mask &= ~_maskArray[Half];
                    }
                }
                while (j > start && !IsConsonant(info[j]));
            }
        }
    }

    private bool FinalReordering(ShapeBuffer buffer)
    {
        if (buffer.Len == 0)
        {
            return false;
        }

        for (int start = 0, end = NextSyllable(buffer, 0); start < buffer.Len; start = end, end = start < buffer.Len ? NextSyllable(buffer, start) : start)
        {
            FinalReorderingSyllable(buffer, start, end);
        }

        return false;
    }

    private void FinalReorderingSyllable(ShapeBuffer buffer, int start, int end)
    {
        GlyphInfo[] info = buffer.Info;

        // Recover a lost halant category on a ligated-and-multiplied virama glyph.
        if (_viramaGlyph != 0)
        {
            for (int i = start; i < end; i++)
            {
                if (info[i].Codepoint == _viramaGlyph &&
                    (info[i].GlyphProps & GlyphProps.Ligated) != 0 &&
                    (info[i].GlyphProps & GlyphProps.Multiplied) != 0)
                {
                    // This will make sure that this glyph passes is_halant() test.
                    info[i].ShaperCategory = CatH;
                    info[i].GlyphProps &= ~(GlyphProps.Ligated | GlyphProps.Multiplied);
                }
            }
        }

        bool tryPref = _maskArray[Pref] != 0;

        // Find base again.
        int baseIdx;
        for (baseIdx = start; baseIdx < end; baseIdx++)
        {
            if (info[baseIdx].ShaperPosition >= PosBaseC)
            {
                if (tryPref && baseIdx + 1 < end)
                {
                    for (int i = baseIdx + 1; i < end; i++)
                    {
                        if ((info[i].Mask & _maskArray[Pref]) != 0)
                        {
                            if (!((info[i].GlyphProps & GlyphProps.Substituted) != 0 && LigatedAndDidntMultiply(info[i])))
                            {
                                // Ok, this was a 'pref' candidate but didn't form any. Base is around here...
                                baseIdx = i;
                                while (baseIdx < end && IsHalant(info[baseIdx]))
                                {
                                    baseIdx++;
                                }

                                if (baseIdx < end)
                                {
                                    info[baseIdx].ShaperPosition = PosBaseC;
                                }

                                tryPref = false;
                            }

                            break;
                        }
                    }

                    if (baseIdx == end)
                    {
                        break;
                    }
                }

                // For Malayalam, skip over unformed below- (but NOT post-) forms.
                if (_script == LipiScript.Malayalam)
                {
                    for (int i = baseIdx + 1; i < end; i++)
                    {
                        while (i < end && IsJoiner(info[i]))
                        {
                            i++;
                        }

                        if (i == end || !IsHalant(info[i]))
                        {
                            break;
                        }

                        i++; // Skip halant.
                        while (i < end && IsJoiner(info[i]))
                        {
                            i++;
                        }

                        if (i < end && IsConsonant(info[i]) && info[i].ShaperPosition == PosBelowC)
                        {
                            baseIdx = i;
                            info[baseIdx].ShaperPosition = PosBaseC;
                        }
                    }
                }

                if (start < baseIdx && info[baseIdx].ShaperPosition > PosBaseC)
                {
                    baseIdx--;
                }

                break;
            }
        }

        if (baseIdx == end && start < baseIdx && IsOneOf(info[baseIdx - 1], Flag(CatZwj)))
        {
            baseIdx--;
        }

        if (baseIdx < end)
        {
            while (start < baseIdx && IsOneOf(info[baseIdx], Flag(CatN) | Flag(CatH)))
            {
                baseIdx--;
            }
        }

        // o Reorder matras.
        if (start + 1 < end && start < baseIdx)
        {
            // If we lost track of base, alas, position before last thingy.
            int newPos = baseIdx == end ? baseIdx - 2 : baseIdx - 1;

            // Malayalam / Tamil do not have "half" forms or explicit virama forms.
            if (_script != LipiScript.Malayalam && _script != LipiScript.Tamil)
            {
                while (true)
                {
                    while (newPos > start && !IsOneOf(info[newPos], Flag(CatM) | Flag(CatMPst) | Flag(CatH)))
                    {
                        newPos--;
                    }

                    // If we found no Halant we are done.
                    // Otherwise only proceed if the Halant does not belong to the Matra itself!
                    if (IsHalant(info[newPos]) && info[newPos].ShaperPosition != PosPreM)
                    {
                        // -> If ZWJ follows this halant, matra is NOT repositioned after this halant.
                        if (newPos + 1 < end && info[newPos + 1].ShaperCategory == CatZwj && newPos > start)
                        {
                            // Keep searching.
                            newPos--;
                            continue;
                        }
                    }
                    else
                    {
                        newPos = start; // No move.
                    }

                    break;
                }
            }

            if (start < newPos && info[newPos].ShaperPosition != PosPreM)
            {
                // Now go see if there's actually any matras...
                for (int i = newPos; i > start; i--)
                {
                    if (info[i - 1].ShaperPosition == PosPreM)
                    {
                        int oldPos = i - 1;
                        if (oldPos < baseIdx && baseIdx <= newPos)
                        {
                            // Shouldn't actually happen.
                            baseIdx--;
                        }

                        GlyphInfo tmp = info[oldPos];
                        Array.Copy(info, oldPos + 1, info, oldPos, newPos - oldPos);
                        info[newPos] = tmp;

                        // Note: this merge_clusters() is intentionally *after* the reordering.
                        buffer.MergeClusters(newPos, Math.Min(end, baseIdx + 1));
                        newPos--;
                    }
                }
            }
            else
            {
                for (int i = start; i < baseIdx; i++)
                {
                    if (info[i].ShaperPosition == PosPreM)
                    {
                        buffer.MergeClusters(i, Math.Min(end, baseIdx + 1));
                        break;
                    }
                }
            }
        }

        // o Reorder reph.
        if (start + 1 < end &&
            info[start].ShaperPosition == PosRaToBecomeReph &&
            ((info[start].ShaperCategory == CatRepha) ^ LigatedAndDidntMultiply(info[start])))
        {
            int newRephPos = FindRephPosition(info, start, end, baseIdx);

            // Move.
            buffer.MergeClusters(start, newRephPos + 1);
            GlyphInfo reph = info[start];
            Array.Copy(info, start + 1, info, start, newRephPos - start);
            info[newRephPos] = reph;

            if (start < baseIdx && baseIdx <= newRephPos)
            {
                baseIdx--;
            }
        }

        // o Reorder pre-base-reordering consonants.
        if (tryPref && baseIdx + 1 < end)
        {
            for (int i = baseIdx + 1; i < end; i++)
            {
                if ((info[i].Mask & _maskArray[Pref]) != 0)
                {
                    // Reorder pref only if it ligated.
                    if (LigatedAndDidntMultiply(info[i]))
                    {
                        int newPos = baseIdx;

                        // Malayalam / Tamil do not have "half" forms or explicit virama forms.
                        if (_script != LipiScript.Malayalam && _script != LipiScript.Tamil)
                        {
                            while (newPos > start && !IsOneOf(info[newPos - 1], Flag(CatM) | Flag(CatMPst) | Flag(CatH)))
                            {
                                newPos--;
                            }
                        }

                        if (newPos > start && IsHalant(info[newPos - 1]))
                        {
                            // -> If ZWJ or ZWNJ follow this halant, position is moved after it.
                            if (newPos < end && IsJoiner(info[newPos]))
                            {
                                newPos++;
                            }
                        }

                        int oldPos = i;
                        buffer.MergeClusters(newPos, oldPos + 1);
                        GlyphInfo tmp = info[oldPos];
                        Array.Copy(info, newPos, info, newPos + 1, oldPos - newPos);
                        info[newPos] = tmp;
                        if (newPos <= baseIdx && baseIdx < oldPos)
                        {
                            baseIdx++;
                        }
                    }

                    break;
                }
            }
        }

        // Apply 'init' to the Left Matra if it's a word start.
        if (info[start].ShaperPosition == PosPreM)
        {
            int gc = start > 0 ? info[start - 1].UnicodeProps & UProps.GenCatMask : -1;
            if (start == 0 || !(gc >= UnicodeData.Format && gc <= UnicodeData.NonSpacingMark))
            {
                info[start].Mask |= _maskArray[Init];
            }
        }
    }

    private int FindRephPosition(GlyphInfo[] info, int start, int end, int baseIdx)
    {
        int newRephPos;
        RephPosition rephPos = _config.RephPos;

        // 1. If reph should be positioned after post-base consonant forms, proceed to step 5.
        if (rephPos != RephPosition.AfterPost)
        {
            // 2. Target position is after the first explicit halant glyph between the first
            //    post-reph consonant and last main consonant.
            newRephPos = start + 1;
            while (newRephPos < baseIdx && !IsHalant(info[newRephPos]))
            {
                newRephPos++;
            }

            if (newRephPos < baseIdx && IsHalant(info[newRephPos]))
            {
                // ->If ZWJ or ZWNJ are following this halant, position is moved after it.
                if (newRephPos + 1 < baseIdx && IsJoiner(info[newRephPos + 1]))
                {
                    newRephPos++;
                }

                return newRephPos;
            }

            // 3. If reph should be repositioned after the main consonant.
            if (rephPos == RephPosition.AfterMain)
            {
                newRephPos = baseIdx;
                while (newRephPos + 1 < end && info[newRephPos + 1].ShaperPosition <= PosAfterMain)
                {
                    newRephPos++;
                }

                if (newRephPos < end)
                {
                    return newRephPos;
                }
            }

            // 4. If reph should be positioned before post-base consonant.
            if (rephPos == RephPosition.AfterSub)
            {
                newRephPos = baseIdx;
                while (newRephPos + 1 < end &&
                       !FlagHas(info[newRephPos + 1].ShaperPosition, Flag(PosPostC) | Flag(PosAfterPost) | Flag(PosSmvd)))
                {
                    newRephPos++;
                }

                if (newRephPos < end)
                {
                    return newRephPos;
                }
            }
        }

        // 5. Move reph to a position immediately before the first post-base matra.
        newRephPos = start + 1;
        while (newRephPos < baseIdx && !IsHalant(info[newRephPos]))
        {
            newRephPos++;
        }

        if (newRephPos < baseIdx && IsHalant(info[newRephPos]))
        {
            if (newRephPos + 1 < baseIdx && IsJoiner(info[newRephPos + 1]))
            {
                newRephPos++;
            }

            return newRephPos;
        }

        // 6. Otherwise, reorder reph to the end of the syllable.
        newRephPos = end - 1;
        while (newRephPos > start && info[newRephPos].ShaperPosition == PosSmvd)
        {
            newRephPos--;
        }

        // If the Reph is to be ending up after a Matra,Halant sequence, position it before that Halant.
        if (IsHalant(info[newRephPos]))
        {
            for (int i = baseIdx + 1; i < newRephPos; i++)
            {
                if (FlagHas(info[i].ShaperCategory, Flag(CatM) | Flag(CatMPst)))
                {
                    newRephPos--;
                }
            }
        }

        return newRephPos;
    }

    private static bool LigatedAndDidntMultiply(in GlyphInfo info) =>
        (info.GlyphProps & GlyphProps.Ligated) != 0 && (info.GlyphProps & GlyphProps.Multiplied) == 0;

    private static Config ConfigFor(LipiScript script) => script switch
    {
        LipiScript.Devanagari => new Config(true, 0x094D, RephPosition.BeforePost, RephMode.Implicit, BlwfMode.PreAndPost),
        LipiScript.Bengali => new Config(true, 0x09CD, RephPosition.AfterSub, RephMode.Implicit, BlwfMode.PreAndPost),
        LipiScript.Gurmukhi => new Config(true, 0x0A4D, RephPosition.BeforeSub, RephMode.Implicit, BlwfMode.PreAndPost),
        LipiScript.Gujarati => new Config(true, 0x0ACD, RephPosition.BeforePost, RephMode.Implicit, BlwfMode.PreAndPost),
        LipiScript.Odia => new Config(true, 0x0B4D, RephPosition.AfterMain, RephMode.Implicit, BlwfMode.PreAndPost),
        LipiScript.Tamil => new Config(true, 0x0BCD, RephPosition.AfterPost, RephMode.Implicit, BlwfMode.PreAndPost),
        LipiScript.Telugu => new Config(true, 0x0C4D, RephPosition.AfterPost, RephMode.Explicit, BlwfMode.PostOnly),
        LipiScript.Kannada => new Config(true, 0x0CCD, RephPosition.AfterPost, RephMode.Implicit, BlwfMode.PostOnly),
        LipiScript.Malayalam => new Config(true, 0x0D4D, RephPosition.AfterMain, RephMode.LogRepha, BlwfMode.PreAndPost),
        _ => new Config(false, 0, RephPosition.BeforePost, RephMode.Implicit, BlwfMode.PreAndPost),
    };

    private enum RephPosition
    {
        AfterMain = PosAfterMain,
        BeforeSub = PosBeforeSub,
        AfterSub = PosAfterSub,
        BeforePost = PosBeforePost,
        AfterPost = PosAfterPost,
    }

    private enum RephMode
    {
        Implicit,
        Explicit,
        LogRepha,
    }

    private enum BlwfMode
    {
        PreAndPost,
        PostOnly,
    }

    private sealed record Config(bool HasOldSpec, int Virama, RephPosition RephPos, RephMode RephMode, BlwfMode BlwfMode);
}
