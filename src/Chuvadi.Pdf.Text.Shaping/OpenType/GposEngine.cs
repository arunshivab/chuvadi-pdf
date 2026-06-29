// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>
/// Applies enabled GPOS lookups to a <see cref="GlyphBuffer"/> after GSUB
/// substitution is complete. Implements lookup types 1 (single pos), 2 (pair
/// pos formats 1 and 2), 4 (mark-to-base), 6 (mark-to-mark), and 9 (extension).
/// </summary>
internal static class GposEngine
{
    /// <summary>
    /// Applies all GPOS lookups whose feature tags are enabled by
    /// <paramref name="features"/> to <paramref name="buffer"/>.
    /// </summary>
    internal static void Apply(
        byte[] sfnt,
        GlyphBuffer buffer,
        ShapingFeatures features,
        string scriptTag,
        string langTag)
    {
        int gpos = OtReader.FindTable(sfnt, "GPOS");
        if (gpos < 0)
        {
            return;
        }

        Dictionary<string, List<int>> featureMap =
            OtReader.ReadFeatureMap(sfnt, gpos, scriptTag, langTag);

        int lookupListOff = gpos + OtReader.U16(sfnt, gpos + 8);
        int lookupCount = OtReader.U16(sfnt, lookupListOff);

        bool[] apply = new bool[lookupCount];
        foreach (System.Collections.Generic.KeyValuePair<string, List<int>> kv in featureMap)
        {
            if (!features.IsEnabled(kv.Key))
            {
                continue;
            }

            foreach (int li in kv.Value)
            {
                if (li < lookupCount)
                {
                    apply[li] = true;
                }
            }
        }

        for (int li = 0; li < lookupCount; li++)
        {
            if (!apply[li])
            {
                continue;
            }

            int lOff = lookupListOff + OtReader.U16(sfnt, lookupListOff + 2 + li * 2);
            ApplyLookup(sfnt, buffer, lOff);
        }
    }

    private static void ApplyLookup(byte[] sfnt, GlyphBuffer buf, int lOff)
    {
        int ltype = OtReader.U16(sfnt, lOff);
        int subCount = OtReader.U16(sfnt, lOff + 4);

        for (int i = 0; i < buf.Count; i++)
        {
            if (buf[i].Deleted)
            {
                continue;
            }

            for (int si = 0; si < subCount; si++)
            {
                int subOff = lOff + OtReader.U16(sfnt, lOff + 6 + si * 2);
                int realType = ltype;
                if (ltype == 9)
                {
                    realType = OtReader.U16(sfnt, subOff + 2);
                    subOff = subOff + OtReader.U32(sfnt, subOff + 4);
                }

                bool applied = realType switch
                {
                    1 => ApplyType1(sfnt, buf, i, subOff),
                    2 => ApplyType2(sfnt, buf, i, subOff),
                    4 => ApplyType4(sfnt, buf, i, subOff),
                    6 => ApplyType6(sfnt, buf, i, subOff),
                    _ => false,
                };

                if (applied)
                {
                    break;
                }
            }
        }
    }

    // Type 1 — Single adjustment
    private static bool ApplyType1(byte[] sfnt, GlyphBuffer buf, int i, int subOff)
    {
        int fmt = OtReader.U16(sfnt, subOff);
        int covOff = subOff + OtReader.U16(sfnt, subOff + 2);
        CoverageTable cov = new CoverageTable(sfnt, covOff);
        int covIdx = cov.IndexOf(buf[i].GlyphId);
        if (covIdx < 0)
        {
            return false;
        }

        int valueFormat = OtReader.U16(sfnt, subOff + 4);
        int vrOff;
        if (fmt == 1)
        {
            vrOff = subOff + 6;
        }
        else
        {
            // Format 2: one ValueRecord per covered glyph
            vrOff = subOff + 8 + covIdx * ValueRecordSize(valueFormat);
        }

        ApplyValueRecord(buf[i], sfnt, vrOff, valueFormat);
        return true;
    }

    // Type 2 — Pair adjustment (kern)
    private static bool ApplyType2(byte[] sfnt, GlyphBuffer buf, int i, int subOff)
    {
        int fmt = OtReader.U16(sfnt, subOff);
        int rawCovOff = OtReader.U16(sfnt, subOff + 2);

        // PairPos Format 2: CoverageOffset=0 is a degenerate but valid encoding where the
        // coverage pointer self-references the subtable header. The class arrays themselves
        // determine which glyphs are adjusted, so we skip the coverage gate in that case.
        if (rawCovOff != 0 || fmt == 1)
        {
            int covOff = subOff + rawCovOff;
            CoverageTable cov = new CoverageTable(sfnt, covOff);
            if (cov.IndexOf(buf[i].GlyphId) < 0)
            {
                return false;
            }
        }

        int next = NextActive(buf, i);
        if (next < 0)
        {
            return false;
        }

        int vf1 = OtReader.U16(sfnt, subOff + 4);
        int vf2 = OtReader.U16(sfnt, subOff + 6);
        int vr1Sz = ValueRecordSize(vf1);
        int vr2Sz = ValueRecordSize(vf2);

        if (fmt == 1)
        {
            return ApplyType2Fmt1(sfnt, buf, i, next, subOff, vf1, vf2, vr1Sz, vr2Sz);
        }

        if (fmt == 2)
        {
            return ApplyType2Fmt2(sfnt, buf, i, next, subOff, vf1, vf2, vr1Sz, vr2Sz);
        }

        return false;
    }

    private static bool ApplyType2Fmt1(
        byte[] sfnt, GlyphBuffer buf, int i, int j,
        int subOff, int vf1, int vf2, int vr1Sz, int vr2Sz)
    {
        int pairSetCount = OtReader.U16(sfnt, subOff + 8);
        int covOff = subOff + OtReader.U16(sfnt, subOff + 2);
        int covIdx = new CoverageTable(sfnt, covOff).IndexOf(buf[i].GlyphId);
        if (covIdx >= pairSetCount)
        {
            return false;
        }

        int psOff = subOff + OtReader.U16(sfnt, subOff + 10 + covIdx * 2);
        int pairCount = OtReader.U16(sfnt, psOff);
        int g2 = buf[j].GlyphId;

        // Binary search PairValueRecords sorted by SecondGlyph
        int recordSz = 2 + vr1Sz + vr2Sz;
        int lo = 0; int hi = pairCount - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int recOff = psOff + 2 + mid * recordSz;
            int sg = OtReader.U16(sfnt, recOff);
            if (sg == g2)
            {
                ApplyValueRecord(buf[i], sfnt, recOff + 2, vf1);
                ApplyValueRecord(buf[j], sfnt, recOff + 2 + vr1Sz, vf2);
                return true;
            }

            if (sg < g2) { lo = mid + 1; }
            else { hi = mid - 1; }
        }

        return false;
    }

    private static bool ApplyType2Fmt2(
        byte[] sfnt, GlyphBuffer buf, int i, int j,
        int subOff, int vf1, int vf2, int vr1Sz, int vr2Sz)
    {
        int cd1Off = subOff + OtReader.U16(sfnt, subOff + 8);
        int cd2Off = subOff + OtReader.U16(sfnt, subOff + 10);
        ClassDefTable cd1 = new ClassDefTable(sfnt, cd1Off);
        ClassDefTable cd2 = new ClassDefTable(sfnt, cd2Off);

        int cls1 = cd1.ClassOf(buf[i].GlyphId);
        int cls2 = cd2.ClassOf(buf[j].GlyphId);
        int class2Count = OtReader.U16(sfnt, subOff + 14);

        // Class1Record[cls1].Class2Record[cls2].Value1/Value2
        int rowSz = class2Count * (vr1Sz + vr2Sz);
        int cellOff = subOff + 16 + cls1 * rowSz + cls2 * (vr1Sz + vr2Sz);

        // Read both ValueRecord XAdvance fields to determine whether this subtable
        // actually adjusts anything. If both are zero, return false so the next
        // subtable (which may have non-zero kern for this pair) gets a chance to run.
        int xaOff1 = OtReader.Popcount16(vf1 & 0x0003) * 2;
        int xaDelta1 = (vf1 & 0x0004) != 0 ? OtReader.I16(sfnt, cellOff + xaOff1) : 0;
        int xaOff2 = OtReader.Popcount16(vf2 & 0x0003) * 2;
        int xaDelta2 = (vf2 & 0x0004) != 0 ? OtReader.I16(sfnt, cellOff + vr1Sz + xaOff2) : 0;
        bool hasDelta = xaDelta1 != 0 || xaDelta2 != 0;

        if (hasDelta)
        {
            if (vf1 != 0) { ApplyValueRecord(buf[i], sfnt, cellOff, vf1); }
            if (vf2 != 0) { ApplyValueRecord(buf[j], sfnt, cellOff + vr1Sz, vf2); }
        }

        return hasDelta;
    }

    // Type 4 — Mark-to-base attachment
    private static bool ApplyType4(byte[] sfnt, GlyphBuffer buf, int i, int subOff)
    {
        int markCovOff = subOff + OtReader.U16(sfnt, subOff + 2);
        CoverageTable markCov = new CoverageTable(sfnt, markCovOff);
        int markIdx = markCov.IndexOf(buf[i].GlyphId);
        if (markIdx < 0)
        {
            return false;
        }

        // Find preceding base glyph
        int baseSlot = PrevBase(buf, i);
        if (baseSlot < 0)
        {
            return false;
        }

        int baseCovOff = subOff + OtReader.U16(sfnt, subOff + 4);
        CoverageTable baseCov = new CoverageTable(sfnt, baseCovOff);
        int baseIdx = baseCov.IndexOf(buf[baseSlot].GlyphId);
        if (baseIdx < 0)
        {
            return false;
        }

        int markCount = OtReader.U16(sfnt, subOff + 6);
        int baseCount = OtReader.U16(sfnt, subOff + 8);
        if (markIdx >= markCount || baseIdx >= baseCount)
        {
            return false;
        }

        int markArrayOff = subOff + OtReader.U16(sfnt, subOff + 10);
        int baseArrayOff = subOff + OtReader.U16(sfnt, subOff + 12);

        // MarkRecord: markClass(2) markAnchorOffset(2)
        int markRecOff = markArrayOff + 2 + markIdx * 4;
        int markClass = OtReader.U16(sfnt, markRecOff);
        int markAnchorOff = markArrayOff + OtReader.U16(sfnt, markRecOff + 2);

        // BaseRecord: baseAnchorOffsets[markClassCount](2)
        int markClassCount = OtReader.U16(sfnt, markArrayOff);
        int baseRecOff = baseArrayOff + 2 + baseIdx * (markClassCount * 2);
        int baseAnchorPtr = OtReader.U16(sfnt, baseRecOff + markClass * 2);
        if (baseAnchorPtr == 0)
        {
            return false;
        }

        int baseAnchorOff = baseArrayOff + baseAnchorPtr;
        int markX = OtReader.I16(sfnt, markAnchorOff + 2);
        int markY = OtReader.I16(sfnt, markAnchorOff + 4);
        int baseX = OtReader.I16(sfnt, baseAnchorOff + 2);
        int baseY = OtReader.I16(sfnt, baseAnchorOff + 4);

        buf[i].XOffset = baseX - markX;
        buf[i].YOffset = baseY - markY;
        buf[i].XAdvance = 0;  // marks don't advance
        return true;
    }

    // Type 6 — Mark-to-mark attachment
    private static bool ApplyType6(byte[] sfnt, GlyphBuffer buf, int i, int subOff)
    {
        int mark1CovOff = subOff + OtReader.U16(sfnt, subOff + 2);
        CoverageTable mark1Cov = new CoverageTable(sfnt, mark1CovOff);
        int mark1Idx = mark1Cov.IndexOf(buf[i].GlyphId);
        if (mark1Idx < 0)
        {
            return false;
        }

        int mark2Slot = PrevBase(buf, i);
        if (mark2Slot < 0)
        {
            return false;
        }

        int mark2CovOff = subOff + OtReader.U16(sfnt, subOff + 4);
        CoverageTable mark2Cov = new CoverageTable(sfnt, mark2CovOff);
        int mark2Idx = mark2Cov.IndexOf(buf[mark2Slot].GlyphId);
        if (mark2Idx < 0)
        {
            return false;
        }

        int mark1Count = OtReader.U16(sfnt, subOff + 6);
        int mark2Count = OtReader.U16(sfnt, subOff + 8);
        if (mark1Idx >= mark1Count || mark2Idx >= mark2Count)
        {
            return false;
        }

        int mark1ArrayOff = subOff + OtReader.U16(sfnt, subOff + 10);
        int mark2ArrayOff = subOff + OtReader.U16(sfnt, subOff + 12);

        int mark1RecOff = mark1ArrayOff + 2 + mark1Idx * 4;
        int markClass = OtReader.U16(sfnt, mark1RecOff);
        int mark1AnchorOff = mark1ArrayOff + OtReader.U16(sfnt, mark1RecOff + 2);

        int markClassCount = OtReader.U16(sfnt, mark1ArrayOff);
        int mark2RecOff = mark2ArrayOff + 2 + mark2Idx * (markClassCount * 2);
        int mark2AnchorPtr = OtReader.U16(sfnt, mark2RecOff + markClass * 2);
        if (mark2AnchorPtr == 0)
        {
            return false;
        }

        int mark2AnchorOff = mark2ArrayOff + mark2AnchorPtr;
        int m1x = OtReader.I16(sfnt, mark1AnchorOff + 2);
        int m1y = OtReader.I16(sfnt, mark1AnchorOff + 4);
        int m2x = OtReader.I16(sfnt, mark2AnchorOff + 2);
        int m2y = OtReader.I16(sfnt, mark2AnchorOff + 4);

        buf[i].XOffset = m2x - m1x;
        buf[i].YOffset = m2y - m1y;
        buf[i].XAdvance = 0;
        return true;
    }

    // Apply a ValueRecord to a glyph slot.
    // ValueFormat bits: 0x0001=XPlacement 0x0002=YPlacement 0x0004=XAdvance 0x0008=YAdvance
    private static void ApplyValueRecord(GlyphSlot slot, byte[] sfnt, int vrOff, int valueFormat)
    {
        int off = vrOff;
        if ((valueFormat & 0x0001) != 0) { slot.XOffset += OtReader.I16(sfnt, off); off += 2; }
        if ((valueFormat & 0x0002) != 0) { slot.YOffset += OtReader.I16(sfnt, off); off += 2; }
        if ((valueFormat & 0x0004) != 0) { slot.XAdvance += OtReader.I16(sfnt, off); off += 2; }
        if ((valueFormat & 0x0008) != 0) { off += 2; }   // YAdvance: skip (vertical layout unused)
        // Device/VariationIndex fields (bits 4-7) are skipped; we use design-space metrics only
    }

    private static int ValueRecordSize(int valueFormat)
        => OtReader.Popcount16(valueFormat & 0x00FF) * 2;

    private static int NextActive(GlyphBuffer buf, int from)
    {
        for (int j = from + 1; j < buf.Count; j++)
        {
            if (!buf[j].Deleted) { return j; }
        }

        return -1;
    }

    private static int PrevBase(GlyphBuffer buf, int from)
    {
        for (int j = from - 1; j >= 0; j--)
        {
            if (!buf[j].Deleted) { return j; }
        }

        return -1;
    }
}
