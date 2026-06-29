// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Indic reordering
//
// Applies Unicode Indic syllable reordering to a logical-order glyph buffer.
// The reordering model:
//
//   1. Segment the buffer into syllables at consonant/independent-vowel
//      boundaries (taking virama+consonant clusters as a unit).
//   2. Within each syllable, for every vowel-dependent slot:
//        • Left position:         move the slot before the base consonant.
//        • LeftAndRight position: replace the slot with [leftGid, base, rightGid]
//                                 using the LiPi font decomposition map.
//        • TopAndLeft / TopAndLeftAndRight: treated as LeftAndRight.
//        • Right / Top / Bottom / TopAndRight / TopAndBottom: no reorder.
//   3. Return the reordered buffer ready for GsubEngine.Apply().
//
// Codepoints are retained in each slot's Cluster field so upstream callers can
// map shaped glyphs back to source positions.

using System.Collections.Generic;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Text.Shaping.OpenType;

namespace Chuvadi.Pdf.Text.Shaping.Indic;

/// <summary>
/// Performs Unicode Indic syllable reordering on a logical-order glyph buffer
/// and synthesises Left_And_Right vowel decompositions using LiPi font glyph ids.
/// </summary>
internal static class IndicReorderer
{
    /// <summary>
    /// Reorders a logical-order glyph buffer for an Indic script.
    /// </summary>
    /// <param name="logical">
    /// Buffer in logical (Unicode) order, one slot per code point, with
    /// <see cref="GlyphSlot.GlyphId"/> set to the cmap glyph id and
    /// <see cref="GlyphSlot.Cluster"/> to the source code point.
    /// </param>
    /// <param name="script">The script being shaped.</param>
    /// <param name="loader">Font loader used to look up advance widths.</param>
    /// <returns>A new buffer in visual (reordered) order.</returns>
    internal static GlyphBuffer Reorder(GlyphBuffer logical, LipiScript script, TrueTypeLoader loader)
    {
        IReadOnlyList<VowelDecomposition> decomps = IndicData.GetDecompositions(script);
        List<GlyphSlot> output = new List<GlyphSlot>(logical.Count + 4);

        // Collect the codepoint→glyph mapping from clusters (set in BuildBuffer)
        // Each GlyphSlot.Cluster holds the source codepoint (set by TextShaper.BuildBuffer).
        int count = logical.Count;

        int i = 0;
        while (i < count)
        {
            // Identify one syllable starting at i.
            int syllableEnd = FindSyllableEnd(logical, i, count);
            ReorderSyllable(logical, i, syllableEnd, decomps, output, loader);
            i = syllableEnd;
        }

        return new GlyphBuffer(output);
    }

    // Returns the exclusive end index of the syllable starting at `start`.
    // A syllable ends just before the next base (consonant or independent vowel)
    // that is NOT part of a virama cluster.
    private static int FindSyllableEnd(GlyphBuffer buf, int start, int count)
    {
        int i = start + 1;
        while (i < count)
        {
            int cp = buf[i].Cluster;
            IndicSyllabicCategory cat = IndicData.GetSyllabicCategory(cp);

            // An independent vowel always starts a new syllable
            if (cat == IndicSyllabicCategory.VowelIndependent)
            {
                return i;
            }

            // A consonant starts a new syllable UNLESS the previous slot was a virama
            // (meaning this consonant is a conjunct/cluster continuation)
            if (cat == IndicSyllabicCategory.Consonant
                || cat == IndicSyllabicCategory.ConsonantDead
                || cat == IndicSyllabicCategory.ConsonantPlaceholder)
            {
                int prev = i - 1;
                int prevCp = buf[prev].Cluster;
                IndicSyllabicCategory prevCat = IndicData.GetSyllabicCategory(prevCp);
                if (prevCat != IndicSyllabicCategory.Virama)
                {
                    return i;
                }
            }

            i++;
        }

        return count;
    }

    // Reorders one syllable [start, end) and appends its slots to output.
    private static void ReorderSyllable(
        GlyphBuffer buf,
        int start,
        int end,
        IReadOnlyList<VowelDecomposition> decomps,
        List<GlyphSlot> output,
        TrueTypeLoader loader)
    {
        // Collect slots in logical order, noting the base consonant index
        // within the working list and which vowel slots need reordering.
        List<GlyphSlot> slots = new List<GlyphSlot>(end - start);
        for (int i = start; i < end; i++)
        {
            slots.Add(buf[i]);
        }

        // Find the base consonant: first consonant (or consonant-like) slot.
        int baseIdx = -1;
        for (int i = 0; i < slots.Count; i++)
        {
            IndicSyllabicCategory cat = IndicData.GetSyllabicCategory(slots[i].Cluster);
            if (cat == IndicSyllabicCategory.Consonant
                || cat == IndicSyllabicCategory.ConsonantDead
                || cat == IndicSyllabicCategory.ConsonantPlaceholder
                || cat == IndicSyllabicCategory.VowelIndependent
                || cat == IndicSyllabicCategory.ModifyingLetter)
            {
                baseIdx = i;
                break;
            }
        }

        if (baseIdx < 0)
        {
            // No base — emit as-is (numbers, stray marks, etc.)
            output.AddRange(slots);
            return;
        }

        // Build the reordered syllable.
        // Strategy: emit [pre-base vowels] [base + cluster] [post-base vowels/marks]
        // with Left_And_Right decomposed into [leftGid] [base+cluster] [rightGid].

        // Collect pre-base (Left/LeftAndRight) vowel indices and their decompositions
        List<(int SlotIdx, VowelDecomposition? Decomp)> preBase =
            new List<(int, VowelDecomposition?)>();
        List<(int SlotIdx, VowelDecomposition? Decomp)> postBase =
            new List<(int, VowelDecomposition?)>();

        for (int i = 0; i < slots.Count; i++)
        {
            if (i == baseIdx)
            {
                continue;
            }

            int cp = slots[i].Cluster;
            IndicSyllabicCategory isc = IndicData.GetSyllabicCategory(cp);

            // Only dependent vowels and virama are considered for reordering;
            // marks (Bindu, Visarga, Nukta) follow the base unchanged.
            if (isc != IndicSyllabicCategory.VowelDependent
                && isc != IndicSyllabicCategory.Virama)
            {
                if (i < baseIdx)
                {
                    preBase.Add((i, null));
                }
                else
                {
                    postBase.Add((i, null));
                }

                continue;
            }

            IndicPositionalCategory ipc = IndicData.GetPositionalCategory(cp);
            bool isLeft = ipc == IndicPositionalCategory.Left;
            bool isLeftAndRight = ipc == IndicPositionalCategory.LeftAndRight
                               || ipc == IndicPositionalCategory.TopAndLeft
                               || ipc == IndicPositionalCategory.TopAndLeftAndRight;

            if (isLeft || isLeftAndRight)
            {
                VowelDecomposition? decomp = null;
                if (isLeftAndRight)
                {
                    decomp = FindDecomposition(decomps, cp);
                }

                preBase.Add((i, decomp));
            }
            else
            {
                postBase.Add((i, null));
            }
        }

        // Emit: pre-base left parts (Left_And_Right only emits left here)
        foreach ((int idx, VowelDecomposition? decomp) in preBase)
        {
            if (decomp.HasValue)
            {
                // Left part of L_And_R: emit with left glyph id, original cluster
                GlyphSlot leftSlot = new GlyphSlot(
                    decomp.Value.LeftGlyphId,
                    slots[idx].Cluster,
                    EstimateAdvance(decomp.Value.LeftGlyphId, loader));
                output.Add(leftSlot);
            }
            else
            {
                // Plain Left: move the whole slot pre-base
                output.Add(slots[idx]);
            }
        }

        // Emit: base consonant and any virama+cluster consonants after it
        // (these were left in logical position, just skipped in pre/post lists)
        output.Add(slots[baseIdx]);
        for (int i = baseIdx + 1; i < slots.Count; i++)
        {
            IndicSyllabicCategory cat = IndicData.GetSyllabicCategory(slots[i].Cluster);
            bool isViramaOrCluster = cat == IndicSyllabicCategory.Virama
                || cat == IndicSyllabicCategory.Consonant
                || cat == IndicSyllabicCategory.ConsonantDead;
            bool alreadyHandled = IsInList(preBase, i) || IsInList(postBase, i);
            if (isViramaOrCluster && !alreadyHandled)
            {
                output.Add(slots[i]);
            }
        }

        // Emit: post-base marks and vowels
        foreach ((int idx, VowelDecomposition? decomp) in postBase)
        {
            output.Add(slots[idx]);
        }

        // Emit: right parts of Left_And_Right (after all post-base)
        foreach ((int idx, VowelDecomposition? decomp) in preBase)
        {
            if (decomp.HasValue)
            {
                GlyphSlot rightSlot = new GlyphSlot(
                    decomp.Value.RightGlyphId,
                    slots[idx].Cluster,
                    EstimateAdvance(decomp.Value.RightGlyphId, loader));
                output.Add(rightSlot);
            }
        }
    }

    private static VowelDecomposition? FindDecomposition(
        IReadOnlyList<VowelDecomposition> decomps, int codepoint)
    {
        foreach (VowelDecomposition d in decomps)
        {
            if (d.ComposedCodepoint == codepoint)
            {
                return d;
            }
        }

        return null;
    }

    private static bool IsInList(List<(int SlotIdx, VowelDecomposition? Decomp)> list, int idx)
    {
        foreach ((int si, _) in list)
        {
            if (si == idx)
            {
                return true;
            }
        }

        return false;
    }

    private static int EstimateAdvance(int glyphId, TrueTypeLoader loader)
    {
        if (glyphId <= 0)
        {
            return 0;
        }

        double scale = 1000.0 / loader.UnitsPerEm;
        return (int)System.Math.Round(loader.GetGlyphMetrics(glyphId).AdvanceWidth * scale);
    }
}
