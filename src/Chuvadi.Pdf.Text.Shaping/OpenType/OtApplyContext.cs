// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Port of HarfBuzz's lookup application (hb-ot-layout-gsubgpos.hh, the
// OT/Layout/GSUB and OT/Layout/GPOS subtables, hb-ot-layout.cc
// apply_forward/apply_backward); see THIRD-PARTY-NOTICES.md.

using System;
using Chuvadi.Pdf.Text.Shaping.Unicode;

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>How a skipping iterator matches a glyph against a data value.</summary>
internal enum MatchKind
{
    /// <summary>No match function: anything that is not skipped matches.</summary>
    None,

    /// <summary>The value is a glyph id.</summary>
    Glyph,

    /// <summary>The value is a class in a ClassDef.</summary>
    Class,

    /// <summary>The value is a Coverage offset from the subtable start.</summary>
    Coverage,
}

/// <summary>
/// HarfBuzz's skipping iterator: walks forward over the input buffer or
/// backward over the output buffer, skipping glyphs the lookup ignores.
/// </summary>
internal sealed class SkippingIterator
{
    internal const int Match = 0;
    internal const int NotMatch = 1;
    internal const int Skip = 2;

    internal const int SkipNo = 0;
    internal const int SkipYes = 1;
    internal const int SkipMaybe = 2;

    private readonly OtApplyContext _c;
    private int _end;
    private uint _mask;
    private bool _ignoreZwnj;
    private bool _ignoreZwj;
    private bool _ignoreHidden;
    private bool _perSyllable;
    private int _syllable;
    private MatchKind _kind;
    private int _matchData;
    private int _glyphData;

    internal SkippingIterator(OtApplyContext c)
    {
        _c = c;
    }

    internal int Idx { get; set; }

    internal int LookupProps { get; set; }

    internal void Init(bool contextMatch)
    {
        _end = _c.Buffer.Len;
        _glyphData = -1;
        _kind = MatchKind.None;
        _matchData = 0;
        LookupProps = _c.LookupProps;
        _ignoreZwnj = _c.TableIndex == 1 || (contextMatch && _c.AutoZwnj);
        _ignoreZwj = contextMatch || _c.AutoZwj;
        _ignoreHidden = _c.TableIndex == 1;
        _mask = contextMatch ? uint.MaxValue : _c.LookupMask;
        _perSyllable = _c.TableIndex == 0 && _c.PerSyllable;
        _syllable = 0;
    }

    internal void SetMatchFunc(MatchKind kind, int data)
    {
        _kind = kind;
        _matchData = data;
    }

    internal void SetGlyphData(int offset) => _glyphData = offset;

    internal void Reset(int start)
    {
        Idx = start;
        _end = _c.Buffer.Len;
        _syllable = _c.Buffer.Cur().Syllable;
    }

    internal void ResetBack(int start)
    {
        Idx = start;
        _syllable = _c.Buffer.Cur().Syllable;
    }

    internal void ResetFast(int start) => Idx = start;

    internal int MaySkip(in GlyphInfo info)
    {
        if (!_c.CheckGlyphProperty(info, LookupProps))
        {
            return SkipYes;
        }

        if (OtApplyContext.IsDefaultIgnorable(info) &&
            (_ignoreZwnj || !OtApplyContext.IsZwnj(info)) &&
            (_ignoreZwj || !OtApplyContext.IsZwj(info)) &&
            (_ignoreHidden || (info.UnicodeProps & UProps.Hidden) == 0))
        {
            return SkipMaybe;
        }

        return SkipNo;
    }

    internal int MatchGlyph(in GlyphInfo info)
    {
        int skip = MaySkip(info);
        if (skip == SkipYes)
        {
            return Skip;
        }

        // may_match: 0 no, 1 yes, 2 maybe.
        int match;
        if ((info.Mask & _mask) == 0 || (_perSyllable && _syllable != 0 && _syllable != info.Syllable))
        {
            match = 0;
        }
        else if (_kind != MatchKind.None)
        {
            match = _c.MatchValue(_kind, _matchData, info.Codepoint, CurrentGlyphData()) ? 1 : 0;
        }
        else
        {
            match = 2;
        }

        if (match == 1 || (match == 2 && skip == SkipNo))
        {
            return Match;
        }

        return skip == SkipNo ? NotMatch : Skip;
    }

    internal bool Next(out int unsafeTo)
    {
        GlyphInfo[] info = _c.Buffer.Info;
        int stop = _end - 1;
        while (Idx < stop)
        {
            Idx++;
            switch (MatchGlyph(info[Idx]))
            {
                case Match:
                    AdvanceGlyphData();
                    unsafeTo = 0;
                    return true;
                case NotMatch:
                    unsafeTo = Idx + 1;
                    return false;
                default:
                    continue;
            }
        }

        unsafeTo = _end;
        return false;
    }

    internal bool Prev(out int unsafeFrom)
    {
        GlyphInfo[] outInfo = _c.Buffer.OutInfo;
        while (Idx > 0)
        {
            Idx--;
            switch (MatchGlyph(outInfo[Idx]))
            {
                case Match:
                    AdvanceGlyphData();
                    unsafeFrom = 0;
                    return true;
                case NotMatch:
                    unsafeFrom = Math.Max(1, Idx) - 1;
                    return false;
                default:
                    continue;
            }
        }

        unsafeFrom = 0;
        return false;
    }

    private int CurrentGlyphData() => _glyphData < 0 ? 0 : OtReader.U16(_c.Data, _glyphData);

    private void AdvanceGlyphData()
    {
        if (_glyphData >= 0)
        {
            _glyphData += 2;
        }
    }
}

/// <summary>
/// Applies GSUB or GPOS lookups to a <see cref="ShapeBuffer"/> (HarfBuzz's
/// hb_ot_apply_context_t together with the subtable apply functions).
/// </summary>
internal sealed partial class OtApplyContext
{
    private const int MaxNestingLevel = 64;
    private const int MaxContextLength = 64;

    private readonly GdefTable _gdef;
    private readonly int[][] _matchPositionPool = new int[MaxNestingLevel + 2][];
    private int _poolDepth;

    internal OtApplyContext(int tableIndex, OtLayoutTable table, GdefTable gdef, ShapeBuffer buffer)
    {
        TableIndex = tableIndex;
        Table = table;
        _gdef = gdef;
        Buffer = buffer;
        HasGlyphClasses = gdef.HasGlyphClasses;
        MatchPositions = RentMatchPositions();
        IterInput = new SkippingIterator(this);
        IterContext = new SkippingIterator(this);
        InitIters();
    }

    internal int TableIndex { get; }

    internal OtLayoutTable Table { get; }

    internal byte[] Data => Table.Data;

    internal ShapeBuffer Buffer { get; }

    internal bool HasGlyphClasses { get; }

    internal uint LookupMask { get; private set; } = 1;

    internal int LookupProps { get; private set; }

    internal bool AutoZwnj { get; private set; } = true;

    internal bool AutoZwj { get; private set; } = true;

    internal bool PerSyllable { get; private set; }

    internal bool RandomFeature { get; private set; }

    internal uint RandomState { get; set; } = 1;

    internal SkippingIterator IterInput { get; }

    internal SkippingIterator IterContext { get; }

    private int[] MatchPositions { get; set; }

    private int NestingLevelLeft { get; set; } = MaxNestingLevel;

    private int LastBase { get; set; } = -1;

    private int LastBaseUntil { get; set; }

    private bool IsGsub => TableIndex == 0;

    internal static bool IsDefaultIgnorable(in GlyphInfo info) =>
        (info.UnicodeProps & UProps.Ignorable) != 0 && (info.GlyphProps & GlyphProps.Substituted) == 0;

    internal static bool IsDefaultIgnorableValue(GlyphInfo info) => IsDefaultIgnorable(info);

    internal static bool IsZwnj(in GlyphInfo info) =>
        (info.UnicodeProps & UProps.GenCatMask) == UnicodeData.Format && (info.UnicodeProps & UProps.CfZwnj) != 0;

    internal static bool IsZwj(in GlyphInfo info) =>
        (info.UnicodeProps & UProps.GenCatMask) == UnicodeData.Format && (info.UnicodeProps & UProps.CfZwj) != 0;

    internal static int GetLigId(in GlyphInfo info) => info.LigProps >> 5;

    internal static bool LigatedInternal(in GlyphInfo info) => (info.LigProps & 0x10) != 0;

    internal static int GetLigComp(in GlyphInfo info) => LigatedInternal(info) ? 0 : info.LigProps & 0x0F;

    internal static int GetLigNumComps(in GlyphInfo info) =>
        (info.GlyphProps & GlyphProps.Ligature) != 0 && LigatedInternal(info) ? info.LigProps & 0x0F : 1;

    internal static int GetLigNumCompsInLigation(in GlyphInfo info) =>
        (info.GlyphProps & GlyphProps.Multiplied) != 0 && GetLigComp(info) != 0 ? 0 : GetLigNumComps(info);

    internal static void SetLigPropsForLigature(ref GlyphInfo info, int ligId, int numComps) =>
        info.LigProps = (ligId << 5) | 0x10 | (numComps & 0x0F);

    internal static void SetLigPropsForMark(ref GlyphInfo info, int ligId, int comp) =>
        info.LigProps = (ligId << 5) | (comp & 0x0F);

    internal void SetLookup(uint mask, bool autoZwj, bool autoZwnj, bool random, bool perSyllable)
    {
        LookupMask = mask;
        LastBase = -1;
        LastBaseUntil = 0;
        AutoZwj = autoZwj;
        AutoZwnj = autoZwnj;
        RandomFeature = random;
        PerSyllable = perSyllable;
    }

    internal void SetLookupProps(int props)
    {
        LookupProps = _gdef.SanitizeLookupProps(props);
        InitIters();
    }

    internal bool CheckGlyphProperty(in GlyphInfo info, int matchProps)
    {
        int glyphProps = info.GlyphProps;
        if ((glyphProps & matchProps & LookupFlag.IgnoreFlags) != 0)
        {
            return false;
        }

        if ((glyphProps & GlyphProps.Mark) != 0)
        {
            if ((matchProps & LookupFlag.UseMarkFilteringSet) != 0)
            {
                return _gdef.MarkSetCovers(matchProps >>> 16, info.Codepoint);
            }

            if ((matchProps & LookupFlag.MarkAttachmentType) != 0)
            {
                return (matchProps & LookupFlag.MarkAttachmentType) == (glyphProps & LookupFlag.MarkAttachmentType);
            }
        }

        return true;
    }

    internal bool MatchValue(MatchKind kind, int data, int glyph, int value) => kind switch
    {
        MatchKind.Glyph => glyph == value,
        MatchKind.Class => OtCommon.ClassOf(Data, data, glyph) == value,
        MatchKind.Coverage => OtCommon.Coverage(Data, data + value, glyph) != OtCommon.NotCovered,
        _ => true,
    };

    /// <summary>Applies one lookup to the whole buffer (HarfBuzz's apply_string).</summary>
    internal bool ApplyString(OtLookup lookup)
    {
        if (Buffer.Len == 0 || LookupMask == 0 || !Buffer.Successful)
        {
            return false;
        }

        SetLookupProps(lookup.Props);
        bool ret;
        if (!(IsGsub && lookup.LookupType == 8))
        {
            if (IsGsub)
            {
                Buffer.ClearOutput();
            }

            Buffer.Idx = 0;
            ret = ApplyForward(lookup);
            if (IsGsub)
            {
                Buffer.Sync();
            }
        }
        else
        {
            Buffer.Idx = Buffer.Len - 1;
            ret = ApplyBackward(lookup);
        }

        return ret;
    }

    private bool ApplyForward(OtLookup lookup)
    {
        bool ret = false;
        while (Buffer.Idx < Buffer.Len && Buffer.Successful)
        {
            GlyphInfo[] info = Buffer.Info;
            int j = Buffer.Idx;
            while (j < Buffer.Len &&
                   !(lookup.Digest.MayHave(info[j].Codepoint) &&
                     (info[j].Mask & LookupMask) != 0 &&
                     CheckGlyphProperty(info[j], LookupProps)))
            {
                j++;
            }

            if (j > Buffer.Idx)
            {
                Buffer.NextGlyphs(j - Buffer.Idx);
            }

            if (Buffer.Idx >= Buffer.Len)
            {
                break;
            }

            if (ApplySubtables(lookup))
            {
                ret = true;
            }
            else
            {
                Buffer.NextGlyph();
            }
        }

        return ret;
    }

    private bool ApplyBackward(OtLookup lookup)
    {
        bool ret = false;
        do
        {
            ref GlyphInfo cur = ref Buffer.Cur();
            if (lookup.Digest.MayHave(cur.Codepoint) &&
                (cur.Mask & LookupMask) != 0 &&
                CheckGlyphProperty(cur, LookupProps))
            {
                ret |= ApplySubtables(lookup);
            }

            Buffer.Idx--;
        }
        while (Buffer.Idx >= 0);
        return ret;
    }

    private bool ApplySubtables(OtLookup lookup)
    {
        foreach (int st in lookup.Subtables)
        {
            bool applied = IsGsub ? ApplyGsubSubtable(lookup.LookupType, st) : ApplyGposSubtable(lookup.LookupType, st);
            if (applied)
            {
                return true;
            }
        }

        return false;
    }

    private bool Recurse(int lookupIndex)
    {
        Buffer.MaxOps--;
        if (NestingLevelLeft == 0 || !Buffer.Successful)
        {
            return false;
        }

        OtLookup? lookup = Table.GetLookup(lookupIndex);
        if (lookup is null)
        {
            return false;
        }

        NestingLevelLeft--;
        int savedProps = LookupProps;
        SetLookupProps(lookup.Props);
        int[] savedPositions = MatchPositions;
        MatchPositions = RentMatchPositions();

        bool ret = ApplySubtables(lookup);

        _poolDepth--;
        MatchPositions = savedPositions;
        SetLookupProps(savedProps);
        NestingLevelLeft++;
        return ret;
    }

    private int[] RentMatchPositions()
    {
        int[] a = _matchPositionPool[_poolDepth] ??= new int[MaxContextLength + 8];
        _poolDepth++;
        return a;
    }

    private void InitIters()
    {
        IterInput.Init(false);
        IterContext.Init(true);
    }

    // ── Glyph class bookkeeping (HarfBuzz _set_glyph_class) ────────────────

    private void SetGlyphClass(int glyph, int classGuess = 0, bool ligature = false, bool component = false)
    {
        Buffer.Digest.Add(glyph);
        ref GlyphInfo cur = ref Buffer.Cur();
        int props = cur.GlyphProps | GlyphProps.Substituted;
        if (ligature)
        {
            props |= GlyphProps.Ligated;
            props &= ~GlyphProps.Multiplied;
        }

        if (component)
        {
            props |= GlyphProps.Multiplied;
        }

        if (HasGlyphClasses)
        {
            cur.GlyphProps = (props & GlyphProps.Preserve) | _gdef.GetGlyphProps(glyph);
        }
        else if (classGuess != 0)
        {
            cur.GlyphProps = (props & GlyphProps.Preserve) | classGuess;
        }
        else
        {
            cur.GlyphProps = props;
        }
    }

    private void ReplaceGlyph(int glyph)
    {
        SetGlyphClass(glyph);
        Buffer.ReplaceGlyph(glyph);
    }

    private void ReplaceGlyphInplace(int glyph)
    {
        SetGlyphClass(glyph);
        Buffer.Cur().Codepoint = glyph;
    }

    private void ReplaceGlyphWithLigature(int glyph, int classGuess)
    {
        SetGlyphClass(glyph, classGuess, ligature: true);
        Buffer.ReplaceGlyph(glyph);
    }

    private void OutputGlyphForComponent(int glyph, int classGuess)
    {
        SetGlyphClass(glyph, classGuess, component: true);
        Buffer.OutputGlyph(glyph);
    }

    private int AllocateLigId()
    {
        int ligId = Buffer.NextSerial() & 0x07;
        while (ligId == 0)
        {
            ligId = Buffer.NextSerial() & 0x07;
        }

        return ligId;
    }

    // ── Matching (match_input, ligate_input, backtrack, lookahead) ──────────

    private bool MatchInput(int count, int inputOffset, MatchKind kind, int matchData, out int endPosition, out int totalComponentCount)
    {
        ShapeBuffer buffer = Buffer;
        totalComponentCount = 0;
        if (count == 1)
        {
            endPosition = buffer.Idx + 1;
            MatchPositions[0] = buffer.Idx;
            totalComponentCount = GetLigNumComps(buffer.Cur());
            return true;
        }

        if (count > MaxContextLength)
        {
            endPosition = 0;
            return false;
        }

        SkippingIterator skippy = IterInput;
        skippy.Reset(buffer.Idx);
        skippy.SetMatchFunc(kind, matchData);
        skippy.SetGlyphData(inputOffset);

        int firstLigId = GetLigId(buffer.Cur());
        int firstLigComp = GetLigComp(buffer.Cur());
        int ligbase = 0; // 0 not checked, 1 may not skip, 2 may skip
        int total = 0;

        for (int i = 1; i < count; i++)
        {
            if (!skippy.Next(out int unsafeTo))
            {
                endPosition = unsafeTo;
                return false;
            }

            MatchPositions[i] = skippy.Idx;
            ref GlyphInfo m = ref buffer.Info[skippy.Idx];
            int thisLigId = GetLigId(m);
            int thisLigComp = GetLigComp(m);

            if (firstLigId != 0 && firstLigComp != 0)
            {
                if (firstLigId != thisLigId || firstLigComp != thisLigComp)
                {
                    if (ligbase == 0)
                    {
                        bool found = false;
                        GlyphInfo[] o = buffer.OutInfo;
                        int j = buffer.OutLen;
                        while (j > 0 && GetLigId(o[j - 1]) == firstLigId)
                        {
                            if (GetLigComp(o[j - 1]) == 0)
                            {
                                j--;
                                found = true;
                                break;
                            }

                            j--;
                        }

                        ligbase = found && skippy.MaySkip(o[j]) == SkippingIterator.SkipYes ? 2 : 1;
                    }

                    if (ligbase == 1)
                    {
                        endPosition = 0;
                        return false;
                    }
                }
            }
            else if (thisLigId != 0 && thisLigComp != 0 && thisLigId != firstLigId)
            {
                endPosition = 0;
                return false;
            }

            total += GetLigNumCompsInLigation(m);
        }

        endPosition = skippy.Idx + 1;
        totalComponentCount = total + GetLigNumComps(buffer.Cur());
        MatchPositions[0] = buffer.Idx;
        return true;
    }

    private void LigateInput(int count, int matchEnd, int ligGlyph, int totalComponentCount)
    {
        ShapeBuffer buffer = Buffer;
        buffer.MergeClusters(buffer.Idx, matchEnd);

        bool isBaseLigature = (buffer.Info[MatchPositions[0]].GlyphProps & GlyphProps.BaseGlyph) != 0;
        bool isMarkLigature = (buffer.Info[MatchPositions[0]].GlyphProps & GlyphProps.Mark) != 0;
        for (int i = 1; i < count; i++)
        {
            if ((buffer.Info[MatchPositions[i]].GlyphProps & GlyphProps.Mark) == 0)
            {
                isBaseLigature = false;
                isMarkLigature = false;
                break;
            }
        }

        bool isLigature = !isBaseLigature && !isMarkLigature;
        int klass = isLigature ? GlyphProps.Ligature : 0;
        int ligId = isLigature ? AllocateLigId() : 0;
        int lastLigId = GetLigId(buffer.Cur());
        int lastNumComponents = GetLigNumComps(buffer.Cur());
        int componentsSoFar = lastNumComponents;

        if (isLigature)
        {
            ref GlyphInfo cur = ref buffer.Cur();
            SetLigPropsForLigature(ref cur, ligId, totalComponentCount);
            if ((cur.UnicodeProps & UProps.GenCatMask) == UnicodeData.NonSpacingMark)
            {
                cur.UnicodeProps = UnicodeData.OtherLetter | (cur.UnicodeProps & (0xFF & ~UProps.GenCatMask));
            }
        }

        ReplaceGlyphWithLigature(ligGlyph, klass);

        for (int i = 1; i < count; i++)
        {
            while (buffer.Idx < MatchPositions[i] && buffer.Successful)
            {
                if (isLigature)
                {
                    ref GlyphInfo cur = ref buffer.Cur();
                    int thisComp = GetLigComp(cur);
                    if (thisComp == 0)
                    {
                        thisComp = lastNumComponents;
                    }

                    int newLigComp = componentsSoFar - lastNumComponents + Math.Min(thisComp, lastNumComponents);
                    SetLigPropsForMark(ref cur, ligId, newLigComp);
                }

                buffer.NextGlyph();
            }

            lastLigId = GetLigId(buffer.Cur());
            lastNumComponents = GetLigNumCompsInLigation(buffer.Cur());
            componentsSoFar += lastNumComponents;

            // Skip the base glyph.
            buffer.Idx++;
        }

        if (!isMarkLigature && lastLigId != 0)
        {
            // Re-adjust components for any marks following.
            for (int i = buffer.Idx; i < buffer.Len; i++)
            {
                ref GlyphInfo g = ref buffer.Info[i];
                if (lastLigId != GetLigId(g))
                {
                    break;
                }

                int thisComp = GetLigComp(g);
                if (thisComp == 0)
                {
                    break;
                }

                int newLigComp = componentsSoFar - lastNumComponents + Math.Min(thisComp, lastNumComponents);
                SetLigPropsForMark(ref g, ligId, newLigComp);
            }
        }
    }

    private bool MatchBacktrack(int count, int dataOffset, MatchKind kind, int matchData, out int matchStart)
    {
        if (count == 0)
        {
            matchStart = Buffer.BacktrackLen;
            return true;
        }

        SkippingIterator skippy = IterContext;
        skippy.ResetBack(Buffer.BacktrackLen);
        skippy.SetMatchFunc(kind, matchData);
        skippy.SetGlyphData(dataOffset);
        for (int i = 0; i < count; i++)
        {
            if (!skippy.Prev(out int unsafeFrom))
            {
                matchStart = unsafeFrom;
                return false;
            }
        }

        matchStart = skippy.Idx;
        return true;
    }

    private bool MatchLookahead(int count, int dataOffset, MatchKind kind, int matchData, int startIndex, out int endIndex)
    {
        if (count == 0)
        {
            endIndex = startIndex;
            return true;
        }

        SkippingIterator skippy = IterContext;
        skippy.Reset(startIndex - 1);
        skippy.SetMatchFunc(kind, matchData);
        skippy.SetGlyphData(dataOffset);
        for (int i = 0; i < count; i++)
        {
            if (!skippy.Next(out int unsafeTo))
            {
                endIndex = unsafeTo;
                return false;
            }
        }

        endIndex = skippy.Idx + 1;
        return true;
    }

    // apply_lookup: runs the nested lookup records over the matched positions.
    private void ApplyLookup(int count, int lookupCount, int lookupRecords, int matchEnd)
    {
        ShapeBuffer buffer = Buffer;
        byte[] d = Data;
        int end;
        {
            int bl = buffer.BacktrackLen;
            end = bl + matchEnd - buffer.Idx;
            int delta = bl - buffer.Idx;
            for (int j = 0; j < count; j++)
            {
                MatchPositions[j] += delta;
            }
        }

        for (int i = 0; i < lookupCount && buffer.Successful; i++)
        {
            int idx = OtReader.U16(d, lookupRecords + (i * 4));
            int lookupIndex = OtReader.U16(d, lookupRecords + (i * 4) + 2);
            if (idx >= count)
            {
                continue;
            }

            int origLen = buffer.BacktrackLen + buffer.LookaheadLen;
            if (MatchPositions[idx] >= origLen)
            {
                continue;
            }

            buffer.MoveTo(MatchPositions[idx]);
            if (buffer.MaxOps <= 0)
            {
                break;
            }

            if (!Recurse(lookupIndex))
            {
                continue;
            }

            int newLen = buffer.BacktrackLen + buffer.LookaheadLen;
            int delta = newLen - origLen;
            if (delta == 0)
            {
                continue;
            }

            end += delta;
            if (end < MatchPositions[idx])
            {
                delta += MatchPositions[idx] - end;
                end = MatchPositions[idx];
            }

            int next = idx + 1;
            if (delta > 0)
            {
                if (delta + count > MaxContextLength)
                {
                    break;
                }
            }
            else
            {
                delta = Math.Max(delta, next - count);
                next -= delta;
            }

            // Shift!
            Array.Copy(MatchPositions, next, MatchPositions, next + delta, count - next);
            next += delta;
            count += delta;

            // Fill in new entries.
            for (int j = idx + 1; j < next; j++)
            {
                MatchPositions[j] = MatchPositions[j - 1] + 1;
            }

            // And fixup the rest.
            for (; next < count; next++)
            {
                MatchPositions[next] += delta;
            }
        }

        buffer.MoveTo(end);
    }

    private bool ContextApplyLookup(int inputCount, int inputOffset, int lookupCount, int lookupRecords, MatchKind kind, int matchData)
    {
        if (inputCount > MaxContextLength)
        {
            return false;
        }

        if (MatchInput(inputCount, inputOffset, kind, matchData, out int matchEnd, out _))
        {
            ApplyLookup(inputCount, lookupCount, lookupRecords, matchEnd);
            return true;
        }

        return false;
    }

    private bool ChainContextApplyLookup(
        int backtrackCount, int backtrackOffset, MatchKind backtrackKind, int backtrackData,
        int inputCount, int inputOffset, MatchKind inputKind, int inputData,
        int lookaheadCount, int lookaheadOffset, MatchKind lookaheadKind, int lookaheadData,
        int lookupCount, int lookupRecords)
    {
        if (inputCount > MaxContextLength)
        {
            return false;
        }

        if (!MatchInput(inputCount, inputOffset, inputKind, inputData, out int matchEnd, out _) ||
            !MatchLookahead(lookaheadCount, lookaheadOffset, lookaheadKind, lookaheadData, matchEnd, out _))
        {
            return false;
        }

        if (!MatchBacktrack(backtrackCount, backtrackOffset, backtrackKind, backtrackData, out _))
        {
            return false;
        }

        ApplyLookup(inputCount, lookupCount, lookupRecords, matchEnd);
        return true;
    }

    // ── Context and chain context (shared by GSUB type 5/6 and GPOS 7/8) ───

    private bool ApplyContext(int st)
    {
        byte[] d = Data;
        int format = OtReader.U16(d, st);
        int cur = Buffer.Cur().Codepoint;
        switch (format)
        {
            case 1:
                {
                    int index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), cur);
                    if (index == OtCommon.NotCovered)
                    {
                        return false;
                    }

                    int ruleSet = OffsetAt(st, st + 6, index, OtReader.U16(d, st + 4));
                    return ruleSet >= 0 && ApplyRuleSet(ruleSet, MatchKind.Glyph, 0);
                }

            case 2:
                {
                    int index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), cur);
                    if (index == OtCommon.NotCovered)
                    {
                        return false;
                    }

                    int classDef = st + OtReader.U16(d, st + 4);
                    int klass = OtCommon.ClassOf(d, classDef, cur);
                    int ruleSet = OffsetAt(st, st + 8, klass, OtReader.U16(d, st + 6));
                    return ruleSet >= 0 && ApplyRuleSet(ruleSet, MatchKind.Class, classDef);
                }

            case 3:
                {
                    int glyphCount = OtReader.U16(d, st + 2);
                    int lookupCount = OtReader.U16(d, st + 4);
                    if (glyphCount == 0)
                    {
                        return false;
                    }

                    if (OtCommon.Coverage(d, st + OtReader.U16(d, st + 6), cur) == OtCommon.NotCovered)
                    {
                        return false;
                    }

                    return ContextApplyLookup(glyphCount, st + 8, lookupCount, st + 6 + (glyphCount * 2), MatchKind.Coverage, st);
                }

            default:
                return false;
        }
    }

    private bool ApplyRuleSet(int ruleSet, MatchKind kind, int matchData)
    {
        byte[] d = Data;
        int ruleCount = OtReader.U16(d, ruleSet);
        for (int r = 0; r < ruleCount; r++)
        {
            int rule = ruleSet + OtReader.U16(d, ruleSet + 2 + (r * 2));
            int inputCount = OtReader.U16(d, rule);
            int lookupCount = OtReader.U16(d, rule + 2);
            int lookupRecords = rule + 4 + (Math.Max(inputCount - 1, 0) * 2);
            if (ContextApplyLookup(inputCount, rule + 4, lookupCount, lookupRecords, kind, matchData))
            {
                return true;
            }
        }

        return false;
    }

    private bool ApplyChainContext(int st)
    {
        byte[] d = Data;
        int format = OtReader.U16(d, st);
        int cur = Buffer.Cur().Codepoint;
        switch (format)
        {
            case 1:
                {
                    int index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), cur);
                    if (index == OtCommon.NotCovered)
                    {
                        return false;
                    }

                    int ruleSet = OffsetAt(st, st + 6, index, OtReader.U16(d, st + 4));
                    return ruleSet >= 0 && ApplyChainRuleSet(ruleSet, MatchKind.Glyph, 0, 0, 0);
                }

            case 2:
                {
                    int index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), cur);
                    if (index == OtCommon.NotCovered)
                    {
                        return false;
                    }

                    int backtrackClassDef = OffsetOrZero(st, st + 4);
                    int inputClassDef = OffsetOrZero(st, st + 6);
                    int lookaheadClassDef = OffsetOrZero(st, st + 8);
                    int klass = OtCommon.ClassOf(d, inputClassDef, cur);
                    int ruleSet = OffsetAt(st, st + 12, klass, OtReader.U16(d, st + 10));
                    return ruleSet >= 0 &&
                        ApplyChainRuleSet(ruleSet, MatchKind.Class, backtrackClassDef, inputClassDef, lookaheadClassDef);
                }

            case 3:
                {
                    int backtrackCount = OtReader.U16(d, st + 2);
                    int backtrack = st + 4;
                    int inputCountOffset = backtrack + (backtrackCount * 2);
                    int inputCount = OtReader.U16(d, inputCountOffset);
                    int input = inputCountOffset + 2;
                    int lookaheadCountOffset = input + (inputCount * 2);
                    int lookaheadCount = OtReader.U16(d, lookaheadCountOffset);
                    int lookahead = lookaheadCountOffset + 2;
                    int lookupCountOffset = lookahead + (lookaheadCount * 2);
                    int lookupCount = OtReader.U16(d, lookupCountOffset);
                    if (inputCount == 0)
                    {
                        return false;
                    }

                    if (OtCommon.Coverage(d, st + OtReader.U16(d, input), cur) == OtCommon.NotCovered)
                    {
                        return false;
                    }

                    return ChainContextApplyLookup(
                        backtrackCount, backtrack, MatchKind.Coverage, st,
                        inputCount, input + 2, MatchKind.Coverage, st,
                        lookaheadCount, lookahead, MatchKind.Coverage, st,
                        lookupCount, lookupCountOffset + 2);
                }

            default:
                return false;
        }
    }

    private bool ApplyChainRuleSet(int ruleSet, MatchKind kind, int backtrackData, int inputData, int lookaheadData)
    {
        byte[] d = Data;
        int ruleCount = OtReader.U16(d, ruleSet);
        for (int r = 0; r < ruleCount; r++)
        {
            int rule = ruleSet + OtReader.U16(d, ruleSet + 2 + (r * 2));
            int backtrackCount = OtReader.U16(d, rule);
            int backtrack = rule + 2;
            int inputCountOffset = backtrack + (backtrackCount * 2);
            int inputCount = OtReader.U16(d, inputCountOffset);
            int input = inputCountOffset + 2;
            int lookaheadCountOffset = input + (Math.Max(inputCount - 1, 0) * 2);
            int lookaheadCount = OtReader.U16(d, lookaheadCountOffset);
            int lookahead = lookaheadCountOffset + 2;
            int lookupCountOffset = lookahead + (lookaheadCount * 2);
            int lookupCount = OtReader.U16(d, lookupCountOffset);
            if (ChainContextApplyLookup(
                backtrackCount, backtrack, kind, backtrackData,
                inputCount, input, kind, inputData,
                lookaheadCount, lookahead, kind, lookaheadData,
                lookupCount, lookupCountOffset + 2))
            {
                return true;
            }
        }

        return false;
    }

    // Returns base + offsets[index] (16-bit offsets array at arrayOffset), or -1 for a null/absent entry.
    private int OffsetAt(int baseOffset, int arrayOffset, int index, int count)
    {
        if (index < 0 || index >= count)
        {
            return -1;
        }

        int o = OtReader.U16(Data, arrayOffset + (index * 2));
        return o == 0 ? -1 : baseOffset + o;
    }

    private int OffsetOrZero(int baseOffset, int at)
    {
        int o = OtReader.U16(Data, at);
        return o == 0 ? 0 : baseOffset + o;
    }
}
