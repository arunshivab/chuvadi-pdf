// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Port of HarfBuzz's GPOS subtable apply functions and attachment
// propagation (OT/Layout/GPOS/*.hh); see THIRD-PARTY-NOTICES.md. Positions
// are in font units; device tables and variations do not apply (no ppem).

using System;
using System.Numerics;

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>GPOS subtables.</summary>
internal sealed partial class OtApplyContext
{
    internal const int AttachTypeMark = 1;
    internal const int AttachTypeCursive = 2;

    /// <summary>Gets or sets whether any glyph was attached (mark or cursive).</summary>
    internal bool HasAttachment { get; set; }

    /// <summary>Accumulates attachment offsets after GPOS (position_finish_offsets).</summary>
    internal static void PropagateAttachmentOffsets(ShapeBuffer buffer)
    {
        GlyphPos[] pos = buffer.Pos;
        int len = buffer.Len;
        for (int i = 0; i < len; i++)
        {
            if (pos[i].AttachChain != 0)
            {
                Propagate(pos, len, i, MaxNestingLevel);
            }
        }
    }

    private static void Propagate(GlyphPos[] pos, int len, int i, int nestingLevel)
    {
        int chain = pos[i].AttachChain;
        int type = pos[i].AttachType;
        pos[i].AttachChain = 0;
        int j = i + chain;
        if (j < 0 || j >= len || nestingLevel == 0)
        {
            return;
        }

        if (pos[j].AttachChain != 0)
        {
            Propagate(pos, len, j, nestingLevel - 1);
        }

        if ((type & AttachTypeCursive) != 0)
        {
            pos[i].YOffset = Sat((long)pos[i].YOffset + pos[j].YOffset);
            return;
        }

        pos[i].XOffset = Sat((long)pos[i].XOffset + pos[j].XOffset);
        if (j < i)
        {
            for (int k = j; k < i; k++)
            {
                pos[i].XOffset = Sat((long)pos[i].XOffset - pos[k].XAdvance);
                pos[i].YOffset = Sat((long)pos[i].YOffset - pos[k].YAdvance);
            }
        }
        else
        {
            for (int k = i; k < j; k++)
            {
                pos[i].XOffset = Sat((long)pos[i].XOffset + pos[k].XAdvance);
                pos[i].YOffset = Sat((long)pos[i].YOffset + pos[k].YAdvance);
            }
        }
    }

    private static int Sat(long v) => (int)Math.Clamp(v, int.MinValue, int.MaxValue);

    private bool ApplyGposSubtable(int type, int st)
    {
        return type switch
        {
            1 => ApplySinglePos(st),
            2 => ApplyPairPos(st),
            3 => ApplyCursivePos(st),
            4 => ApplyMarkBasePos(st),
            5 => ApplyMarkLigPos(st),
            6 => ApplyMarkMarkPos(st),
            7 => ApplyContext(st),
            8 => ApplyChainContext(st),
            _ => false,
        };
    }

    private void ApplyValue(int valueFormat, int values, ref GlyphPos pos)
    {
        byte[] d = Data;
        int v = values;
        if ((valueFormat & 0x01) != 0)
        {
            pos.XOffset = Sat((long)pos.XOffset + OtReader.I16(d, v));
            v += 2;
        }

        if ((valueFormat & 0x02) != 0)
        {
            pos.YOffset = Sat((long)pos.YOffset + OtReader.I16(d, v));
            v += 2;
        }

        if ((valueFormat & 0x04) != 0)
        {
            pos.XAdvance = Sat((long)pos.XAdvance + OtReader.I16(d, v));
        }
    }

    private bool ApplySinglePos(int st)
    {
        byte[] d = Data;
        int format = OtReader.U16(d, st);
        int index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), Buffer.Cur().Codepoint);
        if (index == OtCommon.NotCovered)
        {
            return false;
        }

        int valueFormat = OtReader.U16(d, st + 4);
        if (format == 1)
        {
            ApplyValue(valueFormat, st + 6, ref Buffer.Pos[Buffer.Idx]);
        }
        else if (format == 2)
        {
            if (index >= OtReader.U16(d, st + 6))
            {
                return false;
            }

            ApplyValue(valueFormat, st + 8 + (index * BitOperations.PopCount((uint)valueFormat) * 2), ref Buffer.Pos[Buffer.Idx]);
        }
        else
        {
            return false;
        }

        Buffer.Idx++;
        return true;
    }

    private bool ApplyPairPos(int st)
    {
        byte[] d = Data;
        int format = OtReader.U16(d, st);
        int index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), Buffer.Cur().Codepoint);
        if (index == OtCommon.NotCovered)
        {
            return false;
        }

        SkippingIterator skippy = IterInput;
        skippy.ResetFast(Buffer.Idx);
        if (!skippy.Next(out _))
        {
            return false;
        }

        int valueFormat1 = OtReader.U16(d, st + 4);
        int valueFormat2 = OtReader.U16(d, st + 6);
        int len1 = BitOperations.PopCount((uint)valueFormat1);
        int len2 = BitOperations.PopCount((uint)valueFormat2);
        int second = skippy.Idx;

        if (format == 1)
        {
            int pairSet = OffsetAt(st, st + 10, index, OtReader.U16(d, st + 8));
            if (pairSet < 0)
            {
                return false;
            }

            int count = OtReader.U16(d, pairSet);
            int recordSize = 2 + ((len1 + len2) * 2);
            int glyph = Buffer.Info[second].Codepoint;
            int lo = 0;
            int hi = count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int rec = pairSet + 2 + (mid * recordSize);
                int g = OtReader.U16(d, rec);
                if (g < glyph)
                {
                    lo = mid + 1;
                }
                else if (g > glyph)
                {
                    hi = mid - 1;
                }
                else
                {
                    if (len1 != 0)
                    {
                        ApplyValue(valueFormat1, rec + 2, ref Buffer.Pos[Buffer.Idx]);
                    }

                    if (len2 != 0)
                    {
                        ApplyValue(valueFormat2, rec + 2 + (len1 * 2), ref Buffer.Pos[second]);
                        second++;
                    }

                    Buffer.Idx = second;
                    return true;
                }
            }

            return false;
        }

        if (format == 2)
        {
            int classDef1 = OffsetOrZero(st, st + 8);
            int classDef2 = OffsetOrZero(st, st + 10);
            int class1Count = OtReader.U16(d, st + 12);
            int class2Count = OtReader.U16(d, st + 14);
            int klass1 = OtCommon.ClassOf(d, classDef1, Buffer.Cur().Codepoint);
            int klass2 = OtCommon.ClassOf(d, classDef2, Buffer.Info[second].Codepoint);
            if (klass1 >= class1Count || klass2 >= class2Count)
            {
                return false;
            }

            int record = st + 16 + ((((klass1 * class2Count) + klass2) * (len1 + len2)) * 2);
            if (len1 != 0)
            {
                ApplyValue(valueFormat1, record, ref Buffer.Pos[Buffer.Idx]);
            }

            if (len2 != 0)
            {
                ApplyValue(valueFormat2, record + (len1 * 2), ref Buffer.Pos[second]);
                second++;
            }

            Buffer.Idx = second;
            return true;
        }

        return false;
    }

    private void GetAnchor(int anchor, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (anchor <= 0)
        {
            return;
        }

        int format = OtReader.U16(Data, anchor);
        if (format is 1 or 2 or 3)
        {
            x = OtReader.I16(Data, anchor + 2);
            y = OtReader.I16(Data, anchor + 4);
        }
    }

    private bool ApplyCursivePos(int st)
    {
        byte[] d = Data;
        if (OtReader.U16(d, st) != 1)
        {
            return false;
        }

        int coverage = st + OtReader.U16(d, st + 2);
        int count = OtReader.U16(d, st + 4);
        int thisEntry = EntryExit(st, count, OtCommon.Coverage(d, coverage, Buffer.Cur().Codepoint), 0);
        if (thisEntry < 0)
        {
            return false;
        }

        SkippingIterator skippy = IterInput;
        skippy.ResetFast(Buffer.Idx);
        if (!skippy.Prev(out _))
        {
            return false;
        }

        int prevExit = EntryExit(st, count, OtCommon.Coverage(d, coverage, Buffer.Info[skippy.Idx].Codepoint), 2);
        if (prevExit < 0)
        {
            return false;
        }

        int i = skippy.Idx;
        int j = Buffer.Idx;
        GetAnchor(prevExit, out int exitX, out int exitY);
        GetAnchor(thisEntry, out int entryX, out int entryY);
        GlyphPos[] pos = Buffer.Pos;

        // Main-direction adjustment (left to right).
        pos[i].XAdvance = Sat((long)exitX + pos[i].XOffset);
        int dd = Sat((long)entryX + pos[j].XOffset);
        pos[j].XAdvance = Sat((long)pos[j].XAdvance - dd);
        pos[j].XOffset = Sat((long)pos[j].XOffset - dd);

        // Cross-direction adjustment.
        int child = i;
        int parent = j;
        int yOffset = entryY - exitY;
        if ((LookupProps & LookupFlag.RightToLeft) == 0)
        {
            child = j;
            parent = i;
            yOffset = -yOffset;
        }

        ReverseCursiveMinorOffset(pos, Buffer.Len, child, parent, 0);
        pos[child].AttachChain = parent - child;
        pos[child].AttachType = AttachTypeCursive;
        HasAttachment = true;
        pos[child].YOffset = yOffset;

        // If parent was attached to child, separate them.
        if (pos[parent].AttachChain == -pos[child].AttachChain)
        {
            pos[parent].AttachChain = 0;
            pos[parent].YOffset = 0;
        }

        Buffer.Idx++;
        return true;
    }

    // Returns the entry (field 0) or exit (field 2) anchor offset of a coverage index, or -1.
    private int EntryExit(int st, int count, int index, int field)
    {
        if (index < 0 || index >= count)
        {
            return -1;
        }

        int o = OtReader.U16(Data, st + 6 + (index * 4) + field);
        return o == 0 ? -1 : st + o;
    }

    private static void ReverseCursiveMinorOffset(GlyphPos[] pos, int len, int i, int newParent, int nestingLevel)
    {
        if (nestingLevel > MaxNestingLevel)
        {
            return;
        }

        int chain = pos[i].AttachChain;
        int type = pos[i].AttachType;
        if (chain == 0 || (type & AttachTypeCursive) == 0)
        {
            return;
        }

        pos[i].AttachChain = 0;
        int j = i + chain;
        if (j < 0 || j >= len || j == newParent)
        {
            return;
        }

        ReverseCursiveMinorOffset(pos, len, j, newParent, nestingLevel + 1);
        pos[j].YOffset = Sat(-(long)pos[i].YOffset);
        pos[j].AttachChain = -chain;
        pos[j].AttachType = type;
    }

    private static bool AcceptBase(ShapeBuffer buffer, int idx)
    {
        GlyphInfo[] info = buffer.Info;
        return (info[idx].GlyphProps & GlyphProps.Multiplied) == 0 ||
            GetLigComp(info[idx]) == 0 ||
            idx == 0 ||
            (info[idx - 1].GlyphProps & GlyphProps.Mark) != 0 ||
            (info[idx - 1].GlyphProps & GlyphProps.Multiplied) == 0 ||
            GetLigId(info[idx]) != GetLigId(info[idx - 1]) ||
            GetLigComp(info[idx]) != GetLigComp(info[idx - 1]) + 1;
    }

    private static bool AcceptLigature(ShapeBuffer buffer, int idx) =>
        (buffer.Info[idx].GlyphProps & GlyphProps.Multiplied) == 0 || GetLigComp(buffer.Info[idx]) == 0;

    // Finds the base for MarkBase/MarkLig with HarfBuzz's last_base cache.
    private int FindBase(int baseCoverage, bool ligature)
    {
        SkippingIterator skippy = IterInput;
        skippy.LookupProps = LookupFlag.IgnoreMarks;
        if (LastBaseUntil > Buffer.Idx)
        {
            LastBaseUntil = 0;
            LastBase = -1;
        }

        for (int j = Buffer.Idx; j > LastBaseUntil; j--)
        {
            int match = skippy.MatchGlyph(Buffer.Info[j - 1]);
            if (match == SkippingIterator.Match)
            {
                bool accept = ligature ? AcceptLigature(Buffer, j - 1) : AcceptBase(Buffer, j - 1);
                if (!accept && OtCommon.Coverage(Data, baseCoverage, Buffer.Info[j - 1].Codepoint) == OtCommon.NotCovered)
                {
                    match = SkippingIterator.Skip;
                }
            }

            if (match == SkippingIterator.Match)
            {
                LastBase = j - 1;
                break;
            }
        }

        LastBaseUntil = Buffer.Idx;
        return LastBase;
    }

    private bool ApplyMarkBasePos(int st)
    {
        byte[] d = Data;
        if (OtReader.U16(d, st) != 1)
        {
            return false;
        }

        int markIndex = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), Buffer.Cur().Codepoint);
        if (markIndex == OtCommon.NotCovered)
        {
            return false;
        }

        int baseCoverage = st + OtReader.U16(d, st + 4);
        int idx = FindBase(baseCoverage, ligature: false);
        if (idx < 0)
        {
            return false;
        }

        int baseIndex = OtCommon.Coverage(d, baseCoverage, Buffer.Info[idx].Codepoint);
        if (baseIndex == OtCommon.NotCovered)
        {
            return false;
        }

        int classCount = OtReader.U16(d, st + 6);
        int markArray = st + OtReader.U16(d, st + 8);
        int baseArray = st + OtReader.U16(d, st + 10);
        return ApplyMarkArray(markArray, markIndex, baseIndex, baseArray, classCount, idx);
    }

    private bool ApplyMarkLigPos(int st)
    {
        byte[] d = Data;
        if (OtReader.U16(d, st) != 1)
        {
            return false;
        }

        int markIndex = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), Buffer.Cur().Codepoint);
        if (markIndex == OtCommon.NotCovered)
        {
            return false;
        }

        int ligatureCoverage = st + OtReader.U16(d, st + 4);
        int idx = FindBase(ligatureCoverage, ligature: true);
        if (idx < 0)
        {
            return false;
        }

        int ligIndex = OtCommon.Coverage(d, ligatureCoverage, Buffer.Info[idx].Codepoint);
        if (ligIndex == OtCommon.NotCovered)
        {
            return false;
        }

        int classCount = OtReader.U16(d, st + 6);
        int markArray = st + OtReader.U16(d, st + 8);
        int ligatureArray = st + OtReader.U16(d, st + 10);
        int ligAttach = OffsetAt(ligatureArray, ligatureArray + 2, ligIndex, OtReader.U16(d, ligatureArray));
        int compCount = ligAttach < 0 ? 0 : OtReader.U16(d, ligAttach);
        if (compCount == 0)
        {
            return false;
        }

        int ligId = GetLigId(Buffer.Info[idx]);
        int markId = GetLigId(Buffer.Cur());
        int markComp = GetLigComp(Buffer.Cur());
        int compIndex = ligId != 0 && ligId == markId && markComp > 0
            ? Math.Min(compCount, markComp) - 1
            : compCount - 1;

        return ApplyMarkArray(markArray, markIndex, compIndex, ligAttach, classCount, idx);
    }

    private bool ApplyMarkMarkPos(int st)
    {
        byte[] d = Data;
        if (OtReader.U16(d, st) != 1)
        {
            return false;
        }

        int mark1Index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), Buffer.Cur().Codepoint);
        if (mark1Index == OtCommon.NotCovered)
        {
            return false;
        }

        // Search backwards for a suitable mark glyph until a non-mark glyph.
        SkippingIterator skippy = IterInput;
        skippy.ResetFast(Buffer.Idx);
        skippy.LookupProps = LookupProps & ~LookupFlag.IgnoreFlags;
        if (!skippy.Prev(out _))
        {
            return false;
        }

        int j = skippy.Idx;
        if ((Buffer.Info[j].GlyphProps & GlyphProps.Mark) == 0)
        {
            return false;
        }

        int id1 = GetLigId(Buffer.Cur());
        int id2 = GetLigId(Buffer.Info[j]);
        int comp1 = GetLigComp(Buffer.Cur());
        int comp2 = GetLigComp(Buffer.Info[j]);
        bool good;
        if (id1 == id2)
        {
            good = id1 == 0 || comp1 == comp2;
        }
        else
        {
            good = (id1 > 0 && comp1 == 0) || (id2 > 0 && comp2 == 0);
        }

        if (!good)
        {
            return false;
        }

        int mark2Index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 4), Buffer.Info[j].Codepoint);
        if (mark2Index == OtCommon.NotCovered)
        {
            return false;
        }

        int classCount = OtReader.U16(d, st + 6);
        int mark1Array = st + OtReader.U16(d, st + 8);
        int mark2Array = st + OtReader.U16(d, st + 10);
        return ApplyMarkArray(mark1Array, mark1Index, mark2Index, mark2Array, classCount, j);
    }

    // MarkArray::apply + apply_mark_attachment. anchors is an AnchorMatrix (rows, then offsets).
    private bool ApplyMarkArray(int markArray, int markIndex, int glyphIndex, int anchors, int classCount, int glyphPos)
    {
        byte[] d = Data;
        int markClass = 0;
        int markAnchor = 0;
        if (markIndex < OtReader.U16(d, markArray))
        {
            int rec = markArray + 2 + (markIndex * 4);
            markClass = OtReader.U16(d, rec);
            int o = OtReader.U16(d, rec + 2);
            markAnchor = o == 0 ? 0 : markArray + o;
        }

        int rows = OtReader.U16(d, anchors);
        if (glyphIndex >= rows || markClass >= classCount)
        {
            return false;
        }

        int offset = OtReader.U16(d, anchors + 2 + (((glyphIndex * classCount) + markClass) * 2));
        if (offset == 0)
        {
            return false;
        }

        GetAnchor(markAnchor, out int markX, out int markY);
        GetAnchor(anchors + offset, out int baseX, out int baseY);

        GlyphPos[] pos = Buffer.Pos;
        int baseOffset = pos[glyphPos].YOffset;
        int gp = glyphPos;
        while ((pos[gp].AttachType & AttachTypeCursive) != 0)
        {
            int chain = pos[gp].AttachChain;
            if (chain == 0)
            {
                break;
            }

            int parent = gp + chain;
            if (parent < 0 || parent >= Buffer.Len)
            {
                break;
            }

            gp = parent;
            baseOffset = Sat((long)baseOffset + pos[gp].YOffset);
        }

        ref GlyphPos mark = ref pos[Buffer.Idx];
        mark.AttachChain = glyphPos - Buffer.Idx;
        mark.AttachType = AttachTypeMark;
        mark.XOffset = baseX - markX;
        mark.YOffset = Sat((long)(baseY - markY) + baseOffset);
        HasAttachment = true;
        Buffer.Idx++;
        return true;
    }
}
