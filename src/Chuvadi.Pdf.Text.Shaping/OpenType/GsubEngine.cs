// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>
/// Applies enabled GSUB lookups to a <see cref="GlyphBuffer"/>.
/// Implements lookup types 1 (single), 3 (alternate, first alt), 4 (ligature),
/// 5 (context formats 1–2), and 6 (chaining context formats 1–3).
/// Type 7 (extension) is unwrapped and dispatched to the real type.
/// </summary>
internal static class GsubEngine
{
    /// <summary>
    /// Applies all GSUB lookups whose feature tags are enabled by
    /// <paramref name="features"/> to <paramref name="buffer"/>.
    /// </summary>
    internal static void Apply(
        byte[] sfnt,
        GlyphBuffer buffer,
        ShapingFeatures features,
        string scriptTag,
        string langTag)
    {
        int gsub = OtReader.FindTable(sfnt, "GSUB");
        if (gsub < 0)
        {
            return;
        }

        Dictionary<string, List<int>> featureMap =
            OtReader.ReadFeatureMap(sfnt, gsub, scriptTag, langTag);

        int lookupListOff = gsub + OtReader.U16(sfnt, gsub + 8);
        int lookupCount = OtReader.U16(sfnt, lookupListOff);

        // Build an ordered set of lookup indices to apply (preserving table order)
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
            ApplyLookup(sfnt, gsub, buffer, lOff, apply);
        }
    }

    private static void ApplyLookup(byte[] sfnt, int gsub, GlyphBuffer buf, int lOff, bool[] apply)
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
                if (ltype == 7)
                {
                    // Extension: fmt(2) extType(2) extOff(4)
                    realType = OtReader.U16(sfnt, subOff + 2);
                    subOff = subOff + OtReader.U32(sfnt, subOff + 4);
                }

                bool applied = realType switch
                {
                    1 => ApplyType1(sfnt, buf, i, subOff),
                    3 => ApplyType3(sfnt, buf, i, subOff),
                    4 => ApplyType4(sfnt, buf, i, subOff),
                    5 => ApplyType5(sfnt, gsub, buf, i, subOff, apply),
                    6 => ApplyType6(sfnt, gsub, buf, i, subOff, apply),
                    _ => false,
                };

                if (applied)
                {
                    break;  // move to next slot; each slot matches at most one subtable
                }
            }
        }
    }

    // Type 1 — Single substitution
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

        if (fmt == 1)
        {
            int delta = OtReader.I16(sfnt, subOff + 4);
            buf[i].GlyphId = (buf[i].GlyphId + delta) & 0xFFFF;
            return true;
        }

        if (fmt == 2)
        {
            int count = OtReader.U16(sfnt, subOff + 4);
            if (covIdx < count)
            {
                buf[i].GlyphId = OtReader.U16(sfnt, subOff + 6 + covIdx * 2);
                return true;
            }
        }

        return false;
    }

    // Type 3 — Alternate substitution (always take first alternate)
    private static bool ApplyType3(byte[] sfnt, GlyphBuffer buf, int i, int subOff)
    {
        int covOff = subOff + OtReader.U16(sfnt, subOff + 2);
        CoverageTable cov = new CoverageTable(sfnt, covOff);
        int covIdx = cov.IndexOf(buf[i].GlyphId);
        if (covIdx < 0)
        {
            return false;
        }

        int altSetCount = OtReader.U16(sfnt, subOff + 4);
        if (covIdx >= altSetCount)
        {
            return false;
        }

        int altSetOff = subOff + OtReader.U16(sfnt, subOff + 6 + covIdx * 2);
        int altCount = OtReader.U16(sfnt, altSetOff);
        if (altCount == 0)
        {
            return false;
        }

        // Take the first alternate (index 0)
        buf[i].GlyphId = OtReader.U16(sfnt, altSetOff + 2);
        return true;
    }

    // Type 4 — Ligature substitution
    private static bool ApplyType4(byte[] sfnt, GlyphBuffer buf, int i, int subOff)
    {
        int covOff = subOff + OtReader.U16(sfnt, subOff + 2);
        CoverageTable cov = new CoverageTable(sfnt, covOff);
        int covIdx = cov.IndexOf(buf[i].GlyphId);
        if (covIdx < 0)
        {
            return false;
        }

        int ligSetCount = OtReader.U16(sfnt, subOff + 4);
        if (covIdx >= ligSetCount)
        {
            return false;
        }

        int ligSetOff = subOff + OtReader.U16(sfnt, subOff + 6 + covIdx * 2);
        int ligCount = OtReader.U16(sfnt, ligSetOff);

        for (int li = 0; li < ligCount; li++)
        {
            int ligOff = ligSetOff + OtReader.U16(sfnt, ligSetOff + 2 + li * 2);
            int ligGlyph = OtReader.U16(sfnt, ligOff);
            int compCount = OtReader.U16(sfnt, ligOff + 2); // includes first glyph
            int need = compCount - 1;

            // Collect the next `need` non-deleted slots
            List<int> nextSlots = new List<int>(need);
            for (int j = i + 1; j < buf.Count && nextSlots.Count < need; j++)
            {
                if (!buf[j].Deleted)
                {
                    nextSlots.Add(j);
                }
            }

            if (nextSlots.Count < need)
            {
                continue;
            }

            // Verify components match
            bool match = true;
            for (int k = 0; k < need; k++)
            {
                int expected = OtReader.U16(sfnt, ligOff + 4 + k * 2);
                if (buf[nextSlots[k]].GlyphId != expected)
                {
                    match = false;
                    break;
                }
            }

            if (!match)
            {
                continue;
            }

            // Substitute: write ligature into last component slot, delete earlier ones
            int lastSlot = nextSlots[need - 1];
            buf[lastSlot].GlyphId = ligGlyph;
            // Delete slots from i up to (not including) lastSlot
            buf.Substitute(lastSlot, i, ligGlyph);
            // Update cluster of surviving slot to the first input cluster
            buf[lastSlot].Cluster = buf[i].Cluster;
            return true;
        }

        return false;
    }

    // Type 5 — Context substitution (formats 1 and 2)
    private static bool ApplyType5(byte[] sfnt, int gsub, GlyphBuffer buf, int i, int subOff, bool[] apply)
    {
        int fmt = OtReader.U16(sfnt, subOff);

        if (fmt == 1)
        {
            return ApplyType5Fmt1(sfnt, gsub, buf, i, subOff, apply);
        }

        if (fmt == 2)
        {
            return ApplyType5Fmt2(sfnt, gsub, buf, i, subOff, apply);
        }

        return false;
    }

    private static bool ApplyType5Fmt1(byte[] sfnt, int gsub, GlyphBuffer buf, int i, int subOff, bool[] apply)
    {
        int covOff = subOff + OtReader.U16(sfnt, subOff + 2);
        CoverageTable cov = new CoverageTable(sfnt, covOff);
        if (cov.IndexOf(buf[i].GlyphId) < 0)
        {
            return false;
        }

        int ruleSetCount = OtReader.U16(sfnt, subOff + 4);
        int covIdx = cov.IndexOf(buf[i].GlyphId);
        if (covIdx >= ruleSetCount)
        {
            return false;
        }

        int ruleSetOff = subOff + OtReader.U16(sfnt, subOff + 6 + covIdx * 2);
        int ruleCount = OtReader.U16(sfnt, ruleSetOff);
        for (int ri = 0; ri < ruleCount; ri++)
        {
            int rOff = ruleSetOff + OtReader.U16(sfnt, ruleSetOff + 2 + ri * 2);
            if (ApplyContextRule(sfnt, gsub, buf, i, rOff, apply))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ApplyType5Fmt2(byte[] sfnt, int gsub, GlyphBuffer buf, int i, int subOff, bool[] apply)
    {
        int covOff = subOff + OtReader.U16(sfnt, subOff + 2);
        CoverageTable cov = new CoverageTable(sfnt, covOff);
        if (cov.IndexOf(buf[i].GlyphId) < 0)
        {
            return false;
        }

        int cdOff = subOff + OtReader.U16(sfnt, subOff + 4);
        ClassDefTable cd = new ClassDefTable(sfnt, cdOff);
        int cls = cd.ClassOf(buf[i].GlyphId);

        int classSetCount = OtReader.U16(sfnt, subOff + 6);
        if (cls >= classSetCount)
        {
            return false;
        }

        int classSetPtr = OtReader.U16(sfnt, subOff + 8 + cls * 2);
        if (classSetPtr == 0)
        {
            return false;
        }

        int classSetOff = subOff + classSetPtr;
        int ruleCount = OtReader.U16(sfnt, classSetOff);
        for (int ri = 0; ri < ruleCount; ri++)
        {
            int rOff = classSetOff + OtReader.U16(sfnt, classSetOff + 2 + ri * 2);
            if (ApplyContextRule(sfnt, gsub, buf, i, rOff, apply))
            {
                return true;
            }
        }

        return false;
    }

    // Applies a ContextSubstRule (glyphCount, substCount, glyphIds[], substRecords[])
    // seqCount-1 glyphs after i must match, then apply nested lookups.
    private static bool ApplyContextRule(
        byte[] sfnt, int gsub, GlyphBuffer buf, int i, int rOff, bool[] apply)
    {
        int glyphCount = OtReader.U16(sfnt, rOff);
        int substCount = OtReader.U16(sfnt, rOff + 2);
        int need = glyphCount - 1;

        List<int> nextSlots = CollectNext(buf, i, need);
        if (nextSlots.Count < need)
        {
            return false;
        }

        for (int k = 0; k < need; k++)
        {
            int expected = OtReader.U16(sfnt, rOff + 4 + k * 2);
            if (buf[nextSlots[k]].GlyphId != expected)
            {
                return false;
            }
        }

        // Build slot index array for nested lookup application
        List<int> slots = new List<int>(glyphCount) { i };
        slots.AddRange(nextSlots);

        for (int si = 0; si < substCount; si++)
        {
            int seqIdx = OtReader.U16(sfnt, rOff + 4 + need * 2 + si * 4);
            int lookupIdx = OtReader.U16(sfnt, rOff + 4 + need * 2 + si * 4 + 2);
            if (seqIdx < slots.Count && lookupIdx < apply.Length)
            {
                int lOff = GetLookupOffset(sfnt, gsub, lookupIdx);
                ApplyLookupAtSlot(sfnt, buf, slots[seqIdx], lOff);
            }
        }

        return true;
    }

    // Type 6 — Chaining context substitution (formats 1, 2, 3)
    private static bool ApplyType6(byte[] sfnt, int gsub, GlyphBuffer buf, int i, int subOff, bool[] apply)
    {
        int fmt = OtReader.U16(sfnt, subOff);
        return fmt switch
        {
            1 => ApplyType6Fmt1(sfnt, gsub, buf, i, subOff, apply),
            2 => ApplyType6Fmt2(sfnt, gsub, buf, i, subOff, apply),
            3 => ApplyType6Fmt3(sfnt, gsub, buf, i, subOff, apply),
            _ => false,
        };
    }

    private static bool ApplyType6Fmt1(byte[] sfnt, int gsub, GlyphBuffer buf, int i, int subOff, bool[] apply)
    {
        int covOff = subOff + OtReader.U16(sfnt, subOff + 2);
        CoverageTable cov = new CoverageTable(sfnt, covOff);
        int covIdx = cov.IndexOf(buf[i].GlyphId);
        if (covIdx < 0)
        {
            return false;
        }

        int ruleSetCount = OtReader.U16(sfnt, subOff + 4);
        if (covIdx >= ruleSetCount)
        {
            return false;
        }

        int ruleSetPtr = OtReader.U16(sfnt, subOff + 6 + covIdx * 2);
        if (ruleSetPtr == 0)
        {
            return false;
        }

        int ruleSetOff = subOff + ruleSetPtr;
        int ruleCount = OtReader.U16(sfnt, ruleSetOff);
        for (int ri = 0; ri < ruleCount; ri++)
        {
            int rOff = ruleSetOff + OtReader.U16(sfnt, ruleSetOff + 2 + ri * 2);
            if (ApplyChainRule(sfnt, gsub, buf, i, rOff, apply))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ApplyType6Fmt2(byte[] sfnt, int gsub, GlyphBuffer buf, int i, int subOff, bool[] apply)
    {
        int covOff = subOff + OtReader.U16(sfnt, subOff + 2);
        CoverageTable cov = new CoverageTable(sfnt, covOff);
        if (cov.IndexOf(buf[i].GlyphId) < 0)
        {
            return false;
        }

        int backCdOff = subOff + OtReader.U16(sfnt, subOff + 4);
        int inpCdOff = subOff + OtReader.U16(sfnt, subOff + 6);
        int lookCdOff = subOff + OtReader.U16(sfnt, subOff + 8);
        ClassDefTable inpCd = new ClassDefTable(sfnt, inpCdOff);
        ClassDefTable backCd = new ClassDefTable(sfnt, backCdOff);
        ClassDefTable lookCd = new ClassDefTable(sfnt, lookCdOff);

        int cls = inpCd.ClassOf(buf[i].GlyphId);
        int chainSetCount = OtReader.U16(sfnt, subOff + 10);
        if (cls >= chainSetCount)
        {
            return false;
        }

        int chainSetPtr = OtReader.U16(sfnt, subOff + 12 + cls * 2);
        if (chainSetPtr == 0)
        {
            return false;
        }

        int chainSetOff = subOff + chainSetPtr;
        int ruleCount = OtReader.U16(sfnt, chainSetOff);
        for (int ri = 0; ri < ruleCount; ri++)
        {
            int rOff = chainSetOff + OtReader.U16(sfnt, chainSetOff + 2 + ri * 2);
            if (ApplyChainRule(sfnt, gsub, buf, i, rOff, apply))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ApplyType6Fmt3(byte[] sfnt, int gsub, GlyphBuffer buf, int i, int subOff, bool[] apply)
    {
        int off = subOff + 2;

        // BacktrackCoverage[]
        int backCount = OtReader.U16(sfnt, off); off += 2;
        for (int k = 0; k < backCount; k++)
        {
            int covOff = subOff + OtReader.U16(sfnt, off); off += 2;
            CoverageTable bCov = new CoverageTable(sfnt, covOff);
            // Backtrack goes backwards from i
            int slot = PrevActive(buf, i, backCount - k);
            if (slot < 0 || bCov.IndexOf(buf[slot].GlyphId) < 0)
            {
                return false;
            }
        }

        // InputCoverage[]
        int inpCount = OtReader.U16(sfnt, off); off += 2;
        List<int> inpSlots = new List<int>(inpCount) { i };
        for (int k = 1; k < inpCount; k++)
        {
            int covOff2 = subOff + OtReader.U16(sfnt, off);
            CoverageTable iCov = new CoverageTable(sfnt, covOff2);
            int next = NextActive(buf, inpSlots[inpSlots.Count - 1]);
            if (next < 0 || iCov.IndexOf(buf[next].GlyphId) < 0)
            {
                return false;
            }

            inpSlots.Add(next);
            off += 2;
        }

        off += 2;  // advance past first InputCoverage already consumed above start

        // LookaheadCoverage[]
        int lookCount = OtReader.U16(sfnt, off); off += 2;
        int afterLast = inpSlots[inpSlots.Count - 1];
        for (int k = 0; k < lookCount; k++)
        {
            int covOff = subOff + OtReader.U16(sfnt, off); off += 2;
            CoverageTable lCov = new CoverageTable(sfnt, covOff);
            int next = NextActive(buf, afterLast);
            if (next < 0 || lCov.IndexOf(buf[next].GlyphId) < 0)
            {
                return false;
            }

            afterLast = next;
        }

        // SubstLookupRecord[]
        int substCount = OtReader.U16(sfnt, off); off += 2;
        for (int si = 0; si < substCount; si++)
        {
            int seqIdx = OtReader.U16(sfnt, off); off += 2;
            int lookupIdx = OtReader.U16(sfnt, off); off += 2;
            if (seqIdx < inpSlots.Count && lookupIdx < apply.Length)
            {
                int lOff = GetLookupOffset(sfnt, gsub, lookupIdx);
                ApplyLookupAtSlot(sfnt, buf, inpSlots[seqIdx], lOff);
            }
        }

        return true;
    }

    // ChainContextSubstRule: backtrackCount(2) backtrack[](2) inputCount(2) input[](2)
    //   lookaheadCount(2) lookahead[](2) substCount(2) substRecord[](4)
    private static bool ApplyChainRule(byte[] sfnt, int gsub, GlyphBuffer buf, int i, int rOff, bool[] apply)
    {
        int off = rOff;
        int backCount = OtReader.U16(sfnt, off); off += 2;
        for (int k = 0; k < backCount; k++)
        {
            int expected = OtReader.U16(sfnt, off); off += 2;
            int slot = PrevActive(buf, i, k + 1);
            if (slot < 0 || buf[slot].GlyphId != expected)
            {
                return false;
            }
        }

        int inpCount = OtReader.U16(sfnt, off); off += 2;
        off += 2;  // first input glyph is current slot (already matched by coverage)
        List<int> inpSlots = new List<int>(inpCount) { i };
        for (int k = 1; k < inpCount; k++)
        {
            int expected = OtReader.U16(sfnt, off); off += 2;
            int next = NextActive(buf, inpSlots[inpSlots.Count - 1]);
            if (next < 0 || buf[next].GlyphId != expected)
            {
                return false;
            }

            inpSlots.Add(next);
        }

        int lookCount = OtReader.U16(sfnt, off); off += 2;
        int afterLast = inpSlots[inpSlots.Count - 1];
        for (int k = 0; k < lookCount; k++)
        {
            int expected = OtReader.U16(sfnt, off); off += 2;
            int next = NextActive(buf, afterLast);
            if (next < 0 || buf[next].GlyphId != expected)
            {
                return false;
            }

            afterLast = next;
        }

        int substCount = OtReader.U16(sfnt, off); off += 2;
        for (int si = 0; si < substCount; si++)
        {
            int seqIdx = OtReader.U16(sfnt, off); off += 2;
            int lookupIdx = OtReader.U16(sfnt, off); off += 2;
            if (seqIdx < inpSlots.Count && lookupIdx < apply.Length)
            {
                int lOff = GetLookupOffset(sfnt, gsub, lookupIdx);
                ApplyLookupAtSlot(sfnt, buf, inpSlots[seqIdx], lOff);
            }
        }

        return true;
    }

    private static void ApplyLookupAtSlot(byte[] sfnt, GlyphBuffer buf, int slot, int lOff)
    {
        int ltype = OtReader.U16(sfnt, lOff);
        int subCount = OtReader.U16(sfnt, lOff + 4);
        for (int si = 0; si < subCount; si++)
        {
            int subOff = lOff + OtReader.U16(sfnt, lOff + 6 + si * 2);
            int realType = ltype;
            if (ltype == 7)
            {
                realType = OtReader.U16(sfnt, subOff + 2);
                subOff = subOff + OtReader.U32(sfnt, subOff + 4);
            }

            bool applied = realType switch
            {
                1 => ApplyType1(sfnt, buf, slot, subOff),
                3 => ApplyType3(sfnt, buf, slot, subOff),
                4 => ApplyType4(sfnt, buf, slot, subOff),
                _ => false,
            };

            if (applied)
            {
                break;
            }
        }
    }

    private static int GetLookupOffset(byte[] sfnt, int gsub, int li)
    {
        int llo = gsub + OtReader.U16(sfnt, gsub + 8);
        return llo + OtReader.U16(sfnt, llo + 2 + li * 2);
    }

    private static List<int> CollectNext(GlyphBuffer buf, int from, int count)
    {
        List<int> result = new List<int>(count);
        for (int j = from + 1; j < buf.Count && result.Count < count; j++)
        {
            if (!buf[j].Deleted)
            {
                result.Add(j);
            }
        }

        return result;
    }

    private static int NextActive(GlyphBuffer buf, int from)
    {
        for (int j = from + 1; j < buf.Count; j++)
        {
            if (!buf[j].Deleted)
            {
                return j;
            }
        }

        return -1;
    }

    private static int PrevActive(GlyphBuffer buf, int from, int steps)
    {
        int count = 0;
        for (int j = from - 1; j >= 0; j--)
        {
            if (!buf[j].Deleted)
            {
                count++;
                if (count == steps)
                {
                    return j;
                }
            }
        }

        return -1;
    }
}
