// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Port of HarfBuzz's feature map (hb-ot-map.cc, hb-ot-map.hh): features are
// collected in stages, given mask bits, and resolved to sorted lookup lists
// per stage with pause callbacks between stages; see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>Feature flags (HarfBuzz's hb_ot_map_feature_flags_t).</summary>
[Flags]
internal enum FeatureFlags
{
    None = 0,
    Global = 0x0001,
    HasFallback = 0x0002,
    ManualZwnj = 0x0004,
    ManualZwj = 0x0008,
    ManualJoiners = ManualZwnj | ManualZwj,
    GlobalManualJoiners = Global | ManualJoiners,
    GlobalHasFallback = Global | HasFallback,
    GlobalSearch = 0x0010,
    Randomize = 0x0020,
    PerSyllable = 0x0040,
}

/// <summary>A callback run between GSUB stages; returns whether the glyphs changed.</summary>
internal delegate bool PauseFunc(ShapePlan plan, ShapeBuffer buffer);

/// <summary>A lookup to apply, with its feature mask and matching options.</summary>
internal sealed class LookupMap
{
    internal int Index { get; set; }

    internal uint Mask { get; set; }

    internal bool AutoZwnj { get; set; }

    internal bool AutoZwj { get; set; }

    internal bool RandomFeature { get; set; }

    internal bool PerSyllable { get; set; }
}

/// <summary>A compiled feature: its mask bits and stages.</summary>
internal sealed class FeatureMap
{
    internal string Tag { get; set; } = string.Empty;

    internal int[] Index { get; } = new int[2];

    internal int[] Stage { get; } = new int[2];

    internal int Shift { get; set; }

    internal uint Mask { get; set; }

    internal uint OneMask { get; set; }

    internal bool AutoZwnj { get; set; }

    internal bool AutoZwj { get; set; }

    internal bool RandomFeature { get; set; }

    internal bool PerSyllable { get; set; }
}

/// <summary>The end of a stage's lookups and the pause to run after it.</summary>
internal sealed class StageMap
{
    internal int LastLookup { get; set; }

    internal PauseFunc? Pause { get; set; }
}

/// <summary>The compiled feature map of a shape plan.</summary>
internal sealed class OtMap
{
    internal const int GlobalBitShift = 31;
    internal const uint GlobalBitMask = 1u << GlobalBitShift;

    internal uint GlobalMask { get; set; }

    internal string?[] ChosenScript { get; } = new string?[2];

    internal List<FeatureMap> Features { get; } = new List<FeatureMap>();

    internal List<LookupMap>[] Lookups { get; } = { new List<LookupMap>(), new List<LookupMap>() };

    internal List<StageMap>[] Stages { get; } = { new List<StageMap>(), new List<StageMap>() };

    internal FeatureMap? Find(string tag)
    {
        foreach (FeatureMap f in Features)
        {
            if (string.Equals(f.Tag, tag, StringComparison.Ordinal))
            {
                return f;
            }
        }

        return null;
    }

    internal uint GetMask(string tag, out int shift)
    {
        FeatureMap? f = Find(tag);
        shift = f?.Shift ?? 0;
        return f?.Mask ?? 0;
    }

    internal uint GetOneMask(string tag) => Find(tag)?.OneMask ?? 0;

    internal int GetFeatureStage(int tableIndex, string tag) => Find(tag)?.Stage[tableIndex] ?? int.MaxValue;

    internal List<LookupMap> GetStageLookups(int tableIndex, int stage)
    {
        List<LookupMap> result = new List<LookupMap>();
        List<StageMap> stages = Stages[tableIndex];
        if (stage > stages.Count)
        {
            return result;
        }

        int start = stage > 0 ? stages[stage - 1].LastLookup : 0;
        int end = stage < stages.Count ? stages[stage].LastLookup : Lookups[tableIndex].Count;
        for (int i = start; i < end; i++)
        {
            result.Add(Lookups[tableIndex][i]);
        }

        return result;
    }

    /// <summary>Applies the GSUB (0) or GPOS (1) lookups stage by stage (hb_ot_map_t::apply).</summary>
    internal void Apply(int tableIndex, ShapePlan plan, OtApplyContext c)
    {
        int i = 0;
        List<LookupMap> lookups = Lookups[tableIndex];
        c.Buffer.UpdateDigest();
        foreach (StageMap stage in Stages[tableIndex])
        {
            for (; i < stage.LastLookup; i++)
            {
                LookupMap l = lookups[i];
                OtLookup? lookup = c.Table.GetLookup(l.Index);

                // Only try applying the lookup if there is any overlap.
                if (lookup is null || !lookup.Digest.MayIntersect(c.Buffer.Digest))
                {
                    continue;
                }

                c.SetLookup(l.Mask, l.AutoZwj, l.AutoZwnj, l.RandomFeature, l.PerSyllable);
                c.ApplyString(lookup);
            }

            if (stage.Pause is not null)
            {
                stage.Pause(plan, c.Buffer);
                c.Buffer.UpdateDigest();
            }
        }
    }
}

/// <summary>Collects features and pauses, then compiles an <see cref="OtMap"/> (hb_ot_map_builder_t).</summary>
internal sealed class OtMapBuilder
{
    private const int MaxBits = 8;

    private readonly OtLayoutTable[] _tables;
    private readonly int[] _scriptIndex = new int[2];
    private readonly int[] _languageIndex = new int[2];
    private readonly string?[] _chosenScript = new string?[2];
    private readonly int[] _currentStage = new int[2];
    private readonly List<FeatureInfo> _features = new List<FeatureInfo>();
    private readonly List<(int Index, PauseFunc? Pause)>[] _stages =
    {
        new List<(int, PauseFunc?)>(),
        new List<(int, PauseFunc?)>(),
    };

    internal OtMapBuilder(OtLayoutTable gsub, OtLayoutTable gpos, IReadOnlyList<string> scriptTags)
    {
        _tables = new[] { gsub, gpos };
        for (int t = 0; t < 2; t++)
        {
            _scriptIndex[t] = SelectScript(_tables[t], scriptTags, out _chosenScript[t]);
            int lang = _tables[t].FindLanguage(_scriptIndex[t], "dflt");
            _languageIndex[t] = lang;
        }
    }

    internal string? ChosenGsubScript => _chosenScript[0];

    internal void AddFeature(string tag, FeatureFlags flags = FeatureFlags.None, int value = 1)
    {
        _features.Add(new FeatureInfo
        {
            Tag = tag,
            Seq = _features.Count + 1,
            MaxValue = value,
            Flags = flags,
            DefaultValue = (flags & FeatureFlags.Global) != 0 ? value : 0,
            Stage0 = _currentStage[0],
            Stage1 = _currentStage[1],
        });
    }

    internal void EnableFeature(string tag, FeatureFlags flags = FeatureFlags.None, int value = 1) =>
        AddFeature(tag, FeatureFlags.Global | flags, value);

    internal void DisableFeature(string tag) => AddFeature(tag, FeatureFlags.Global, 0);

    internal void AddGsubPause(PauseFunc? pause) => AddPause(0, pause);

    internal void AddGposPause(PauseFunc? pause) => AddPause(1, pause);

    internal OtMap Compile()
    {
        OtMap m = new OtMap { GlobalMask = OtMap.GlobalBitMask };
        int[] requiredFeatureIndex = new int[2];
        int[] requiredFeatureStage = new int[2];
        string?[] requiredFeatureTag = new string?[2];
        Dictionary<string, int>[] featureIndices = new Dictionary<string, int>[2];
        for (int t = 0; t < 2; t++)
        {
            m.ChosenScript[t] = _chosenScript[t];
            int langSys = _tables[t].LangSysOffset(_scriptIndex[t], _languageIndex[t]);
            requiredFeatureIndex[t] = _tables[t].RequiredFeature(langSys);
            requiredFeatureTag[t] = requiredFeatureIndex[t] >= 0 ? _tables[t].FeatureTag(requiredFeatureIndex[t]) : null;
            featureIndices[t] = _tables[t].FeatureMap(langSys);
        }

        // Sort features and merge duplicates.
        List<FeatureInfo> f = new List<FeatureInfo>(_features);
        f.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.Tag, b.Tag);
            return c != 0 ? c : a.Seq.CompareTo(b.Seq);
        });
        List<FeatureInfo> merged = new List<FeatureInfo>();
        foreach (FeatureInfo fi in f)
        {
            if (merged.Count == 0 || !string.Equals(merged[^1].Tag, fi.Tag, StringComparison.Ordinal))
            {
                merged.Add(fi.Clone());
                continue;
            }

            FeatureInfo j = merged[^1];
            if ((fi.Flags & FeatureFlags.Global) != 0)
            {
                j.Flags |= FeatureFlags.Global;
                j.MaxValue = fi.MaxValue;
                j.DefaultValue = fi.DefaultValue;
            }
            else
            {
                j.Flags &= ~FeatureFlags.Global;
                j.MaxValue = Math.Max(j.MaxValue, fi.MaxValue);
            }

            j.Flags |= fi.Flags & FeatureFlags.HasFallback;
            j.Stage0 = Math.Min(j.Stage0, fi.Stage0);
            j.Stage1 = Math.Min(j.Stage1, fi.Stage1);
        }

        // Allocate bits now.
        int nextBit = 3 + 1;
        foreach (FeatureInfo info in merged)
        {
            int bitsNeeded = (info.Flags & FeatureFlags.Global) != 0 && info.MaxValue == 1
                ? 0
                : Math.Min(MaxBits, BitStorage(info.MaxValue));
            if (info.MaxValue == 0 || nextBit + bitsNeeded >= OtMap.GlobalBitShift)
            {
                continue;
            }

            bool found = false;
            int[] featureIndex = new int[2];
            int[] stage = { info.Stage0, info.Stage1 };
            for (int t = 0; t < 2; t++)
            {
                if (string.Equals(requiredFeatureTag[t], info.Tag, StringComparison.Ordinal))
                {
                    requiredFeatureStage[t] = stage[t];
                }

                if (featureIndices[t].TryGetValue(info.Tag, out int index))
                {
                    featureIndex[t] = index;
                    found = true;
                }
                else
                {
                    featureIndex[t] = -1;
                }
            }

            if (!found && (info.Flags & FeatureFlags.GlobalSearch) != 0)
            {
                for (int t = 0; t < 2; t++)
                {
                    featureIndex[t] = _tables[t].FindFeatureAnywhere(info.Tag);
                    found |= featureIndex[t] >= 0;
                }
            }

            if (!found && (info.Flags & FeatureFlags.HasFallback) == 0)
            {
                continue;
            }

            FeatureMap map = new FeatureMap
            {
                Tag = info.Tag,
                AutoZwnj = (info.Flags & FeatureFlags.ManualZwnj) == 0,
                AutoZwj = (info.Flags & FeatureFlags.ManualZwj) == 0,
                RandomFeature = (info.Flags & FeatureFlags.Randomize) != 0,
                PerSyllable = (info.Flags & FeatureFlags.PerSyllable) != 0,
            };
            map.Index[0] = featureIndex[0];
            map.Index[1] = featureIndex[1];
            map.Stage[0] = info.Stage0;
            map.Stage[1] = info.Stage1;
            if ((info.Flags & FeatureFlags.Global) != 0 && info.MaxValue == 1)
            {
                map.Shift = OtMap.GlobalBitShift;
                map.Mask = OtMap.GlobalBitMask;
            }
            else
            {
                map.Shift = nextBit;
                map.Mask = (uint)((1L << (nextBit + bitsNeeded)) - (1L << nextBit));
                nextBit += bitsNeeded;
                m.GlobalMask |= ((uint)info.DefaultValue << map.Shift) & map.Mask;
            }

            map.OneMask = (1u << map.Shift) & map.Mask;
            m.Features.Add(map);
        }

        AddGsubPause(null);
        AddGposPause(null);

        for (int t = 0; t < 2; t++)
        {
            List<LookupMap> lookups = m.Lookups[t];
            int stageIndex = 0;
            int lastNumLookups = 0;
            for (int stage = 0; stage < _currentStage[t]; stage++)
            {
                if (requiredFeatureIndex[t] >= 0 && requiredFeatureStage[t] == stage)
                {
                    AddLookups(lookups, t, requiredFeatureIndex[t], OtMap.GlobalBitMask, true, true, false, false);
                }

                foreach (FeatureMap feature in m.Features)
                {
                    if (feature.Stage[t] == stage)
                    {
                        AddLookups(lookups, t, feature.Index[t], feature.Mask, feature.AutoZwnj, feature.AutoZwj, feature.RandomFeature, feature.PerSyllable);
                    }
                }

                // Sort lookups and merge duplicates.
                if (lastNumLookups + 1 < lookups.Count)
                {
                    List<LookupMap> range = lookups.GetRange(lastNumLookups, lookups.Count - lastNumLookups);
                    StableSortByIndex(range);
                    lookups.RemoveRange(lastNumLookups, lookups.Count - lastNumLookups);
                    foreach (LookupMap l in range)
                    {
                        if (lookups.Count > lastNumLookups && lookups[^1].Index == l.Index)
                        {
                            LookupMap j = lookups[^1];
                            j.Mask |= l.Mask;
                            j.AutoZwnj &= l.AutoZwnj;
                            j.AutoZwj &= l.AutoZwj;
                        }
                        else
                        {
                            lookups.Add(l);
                        }
                    }
                }

                lastNumLookups = lookups.Count;
                if (stageIndex < _stages[t].Count && _stages[t][stageIndex].Index == stage)
                {
                    m.Stages[t].Add(new StageMap { LastLookup = lastNumLookups, Pause = _stages[t][stageIndex].Pause });
                    stageIndex++;
                }
            }
        }

        return m;
    }

    private static void StableSortByIndex(List<LookupMap> list)
    {
        for (int i = 1; i < list.Count; i++)
        {
            LookupMap t = list[i];
            int j = i - 1;
            while (j >= 0 && list[j].Index > t.Index)
            {
                list[j + 1] = list[j];
                j--;
            }

            list[j + 1] = t;
        }
    }

    private static int BitStorage(int v)
    {
        int n = 0;
        while (v > 0)
        {
            n++;
            v >>= 1;
        }

        return n;
    }

    private static int SelectScript(OtLayoutTable table, IReadOnlyList<string> tags, out string? chosen)
    {
        foreach (string tag in tags)
        {
            int i = table.FindScript(tag);
            if (i >= 0)
            {
                chosen = tag;
                return i;
            }
        }

        foreach (string fallback in new[] { "DFLT", "dflt", "latn" })
        {
            int i = table.FindScript(fallback);
            if (i >= 0)
            {
                chosen = fallback;
                return i;
            }
        }

        chosen = null;
        return -1;
    }

    private void AddPause(int tableIndex, PauseFunc? pause)
    {
        _stages[tableIndex].Add((_currentStage[tableIndex], pause));
        _currentStage[tableIndex]++;
    }

    private void AddLookups(List<LookupMap> lookups, int t, int featureIndex, uint mask, bool autoZwnj, bool autoZwj, bool random, bool perSyllable)
    {
        if (featureIndex < 0)
        {
            return;
        }

        int tableLookupCount = _tables[t].LookupCount;
        foreach (int li in _tables[t].FeatureLookups(featureIndex))
        {
            if (li >= tableLookupCount)
            {
                continue;
            }

            lookups.Add(new LookupMap
            {
                Index = li,
                Mask = mask,
                AutoZwnj = autoZwnj,
                AutoZwj = autoZwj,
                RandomFeature = random,
                PerSyllable = perSyllable,
            });
        }
    }

    private sealed class FeatureInfo
    {
        internal string Tag { get; set; } = string.Empty;

        internal int Seq { get; set; }

        internal int MaxValue { get; set; }

        internal FeatureFlags Flags { get; set; }

        internal int DefaultValue { get; set; }

        internal int Stage0 { get; set; }

        internal int Stage1 { get; set; }

        internal FeatureInfo Clone() => (FeatureInfo)MemberwiseClone();
    }
}
