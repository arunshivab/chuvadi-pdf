// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Port of HarfBuzz's normalizer (hb-ot-shape-normalize.cc): decompose,
// reorder marks by modified combining class, recompose, choosing the forms
// the font has glyphs for; see THIRD-PARTY-NOTICES.md.

using Chuvadi.Pdf.Text.Shaping.Unicode;

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>Normalization modes (hb_ot_shape_normalization_mode_t).</summary>
internal enum NormalizationMode
{
    None,
    Decomposed,
    ComposedDiacritics,
    ComposedDiacriticsNoShortCircuit,
}

/// <summary>Text normalization against the font's cmap.</summary>
internal static class Normalizer
{
    private const int MaxCombiningMarks = 32;

    internal static void Normalize(ShapePlan plan, ShapeBuffer buffer)
    {
        if (buffer.Len == 0)
        {
            return;
        }

        NormalizationMode mode = plan.NormalizationMode;
        bool alwaysShortCircuit = mode == NormalizationMode.None;
        bool mightShortCircuit = alwaysShortCircuit ||
            (mode != NormalizationMode.Decomposed && mode != NormalizationMode.ComposedDiacriticsNoShortCircuit);

        // First round, decompose.
        bool allSimple = true;
        buffer.ClearOutput();
        int count = buffer.Len;
        buffer.Idx = 0;
        do
        {
            int end;
            for (end = buffer.Idx + 1; end < count; end++)
            {
                if (IsUnicodeMark(buffer.Info[end]))
                {
                    break;
                }
            }

            if (end < count)
            {
                end--; // Leave one base for the marks to cluster with.
            }

            // From idx to end are simple clusters.
            if (mightShortCircuit)
            {
                int done = 0;
                while (buffer.Idx + done < end)
                {
                    int g = plan.NominalGlyph(buffer.Info[buffer.Idx + done].Codepoint);
                    if (g == 0)
                    {
                        break;
                    }

                    buffer.Info[buffer.Idx + done].NormalizerGlyph = g;
                    done++;
                }

                buffer.NextGlyphs(done);
            }

            while (buffer.Idx < end)
            {
                DecomposeCurrentCharacter(plan, buffer, mightShortCircuit);
            }

            if (buffer.Idx == count)
            {
                break;
            }

            allSimple = false;

            // Find all the marks now.
            for (end = buffer.Idx + 1; end < count; end++)
            {
                if (!IsUnicodeMark(buffer.Info[end]))
                {
                    break;
                }
            }

            // idx to end is one non-simple cluster.
            DecomposeMultiCharCluster(plan, buffer, end, alwaysShortCircuit);
        }
        while (buffer.Idx < count);
        buffer.Sync();

        // Second round, reorder (in place).
        if (!allSimple)
        {
            count = buffer.Len;
            for (int i = 0; i < count; i++)
            {
                if (Cc(buffer.Info[i]) == 0)
                {
                    continue;
                }

                int end;
                for (end = i + 1; end < count; end++)
                {
                    if (Cc(buffer.Info[end]) == 0)
                    {
                        break;
                    }
                }

                // We are going to do a O(n^2).  Only do this if the sequence is short.
                if (end - i > MaxCombiningMarks)
                {
                    i = end;
                    continue;
                }

                buffer.Sort(i, end, (a, b) => Cc(a).CompareTo(Cc(b)));
                i = end;
            }
        }

        if ((buffer.ScratchFlags & ShapeBuffer.ScratchHasCgj) != 0)
        {
            // For all CGJ, check if it prevented any reordering at all.
            // If it did NOT, then make it skippable.
            GlyphInfo[] info = buffer.Info;
            for (int i = 1; i + 1 < buffer.Len; i++)
            {
                if (info[i].Codepoint == 0x034F && (Cc(info[i + 1]) == 0 || Cc(info[i - 1]) <= Cc(info[i + 1])))
                {
                    info[i].UnicodeProps &= ~UProps.Hidden;
                }
            }
        }

        // Third round, recompose.
        if (!allSimple &&
            (mode == NormalizationMode.ComposedDiacritics || mode == NormalizationMode.ComposedDiacriticsNoShortCircuit))
        {
            buffer.ClearOutput();
            count = buffer.Len;
            int starter = 0;
            buffer.NextGlyph();
            while (buffer.Idx < count)
            {
                ref GlyphInfo cur = ref buffer.Cur();
                if (IsUnicodeMark(cur) &&
                    (starter == buffer.OutLen - 1 || Cc(buffer.Prev()) < Cc(cur)) &&
                    plan.Compose(buffer.OutInfo[starter].Codepoint, cur.Codepoint, out int composed))
                {
                    int glyph = plan.NominalGlyph(composed);
                    if (glyph != 0)
                    {
                        // Composes.
                        buffer.NextGlyph();
                        buffer.MergeOutClusters(starter, buffer.OutLen);
                        buffer.OutLen--;
                        ref GlyphInfo s = ref buffer.OutInfo[starter];
                        s.Codepoint = composed;
                        s.NormalizerGlyph = glyph;
                        ShapePlan.SetUnicodeProps(ref s, buffer);
                        continue;
                    }
                }

                // Blocked, or doesn't compose.
                buffer.NextGlyph();
                if (Cc(buffer.Prev()) == 0)
                {
                    starter = buffer.OutLen - 1;
                }
            }

            buffer.Sync();
        }
    }

    internal static bool IsUnicodeMark(in GlyphInfo info) =>
        UnicodeData.IsMarkCategory(info.UnicodeProps & UProps.GenCatMask);

    internal static int Cc(in GlyphInfo info) => IsUnicodeMark(info) ? info.UnicodeProps >> 8 : 0;

    private static void OutputChar(ShapePlan plan, ShapeBuffer buffer, int unichar, int glyph)
    {
        buffer.Cur().NormalizerGlyph = glyph;
        buffer.OutputGlyph(unichar);
        ShapePlan.SetUnicodeProps(ref buffer.Prev(), buffer);
        _ = plan;
    }

    private static void NextChar(ShapeBuffer buffer, int glyph)
    {
        buffer.Cur().NormalizerGlyph = glyph;
        buffer.NextGlyph();
    }

    private static int Decompose(ShapePlan plan, ShapeBuffer buffer, bool shortest, int ab)
    {
        if (!plan.Decompose(ab, out int a, out int b))
        {
            return 0;
        }

        int bGlyph = 0;
        if (b != 0)
        {
            bGlyph = plan.NominalGlyph(b);
            if (bGlyph == 0)
            {
                return 0;
            }
        }

        int aGlyph = plan.NominalGlyph(a);
        bool hasA = aGlyph != 0;
        if (shortest && hasA)
        {
            // Output a and b.
            OutputChar(plan, buffer, a, aGlyph);
            if (b != 0)
            {
                OutputChar(plan, buffer, b, bGlyph);
                return 2;
            }

            return 1;
        }

        int ret = Decompose(plan, buffer, shortest, a);
        if (ret != 0)
        {
            if (b != 0)
            {
                OutputChar(plan, buffer, b, bGlyph);
                return ret + 1;
            }

            return ret;
        }

        if (hasA)
        {
            OutputChar(plan, buffer, a, aGlyph);
            if (b != 0)
            {
                OutputChar(plan, buffer, b, bGlyph);
                return 2;
            }

            return 1;
        }

        return 0;
    }

    private static void DecomposeCurrentCharacter(ShapePlan plan, ShapeBuffer buffer, bool shortest)
    {
        int u = buffer.Cur().Codepoint;
        int glyph = plan.NominalGlyph(u);
        if (shortest && glyph != 0)
        {
            NextChar(buffer, glyph);
            return;
        }

        if (Decompose(plan, buffer, shortest, u) != 0)
        {
            buffer.SkipGlyph();
            return;
        }

        if (!shortest && glyph != 0)
        {
            NextChar(buffer, glyph);
            return;
        }

        if ((buffer.Cur().UnicodeProps & UProps.GenCatMask) == UnicodeData.SpaceSeparator)
        {
            int spaceType = UnicodeData.SpaceFallbackType(u);
            int spaceGlyph = plan.NominalGlyph(0x0020);
            if (spaceType != UnicodeData.NotSpace && spaceGlyph != 0)
            {
                ref GlyphInfo cur = ref buffer.Cur();
                cur.UnicodeProps = (spaceType << 8) | (cur.UnicodeProps & 0xFF);
                NextChar(buffer, spaceGlyph);
                buffer.ScratchFlags |= ShapeBuffer.ScratchHasSpaceFallback;
                return;
            }
        }

        if (u == 0x2011)
        {
            // U+2011 is the only sensible character that is a no-break version of another character
            // and not a space.  The space ones are handled already.  Handle this lone one.
            int other = plan.NominalGlyph(0x2010);
            if (other != 0)
            {
                NextChar(buffer, other);
                return;
            }
        }

        NextChar(buffer, glyph);
    }

    private static void DecomposeMultiCharCluster(ShapePlan plan, ShapeBuffer buffer, int end, bool shortCircuit)
    {
        for (int i = buffer.Idx; i < end; i++)
        {
            if (UnicodeData.IsVariationSelector(buffer.Info[i].Codepoint))
            {
                HandleVariationSelectorCluster(plan, buffer, end);
                return;
            }
        }

        while (buffer.Idx < end)
        {
            DecomposeCurrentCharacter(plan, buffer, shortCircuit);
        }
    }

    // Variation sequences (cmap format 14) are not mapped: the selector is passed
    // through for GSUB, as HarfBuzz does when the font has no variation glyph.
    private static void HandleVariationSelectorCluster(ShapePlan plan, ShapeBuffer buffer, int end)
    {
        while (buffer.Idx < end - 1)
        {
            if (UnicodeData.IsVariationSelector(buffer.Cur(1).Codepoint))
            {
                SetGlyph(plan, ref buffer.Cur());
                buffer.NextGlyph();
                buffer.ScratchFlags |= ShapeBuffer.ScratchHasVariationSelectorFallback;
                ref GlyphInfo vs = ref buffer.Cur();
                vs.UnicodeProps = UnicodeData.Format | (vs.UnicodeProps & (0xFF & ~UProps.GenCatMask)) | UProps.CfVs;
                SetGlyph(plan, ref buffer.Cur());
                buffer.NextGlyph();

                // Skip any further variation selectors.
                while (buffer.Idx < end && UnicodeData.IsVariationSelector(buffer.Cur().Codepoint))
                {
                    SetGlyph(plan, ref buffer.Cur());
                    buffer.NextGlyph();
                }
            }
            else
            {
                SetGlyph(plan, ref buffer.Cur());
                buffer.NextGlyph();
            }
        }

        if (buffer.Idx < end)
        {
            SetGlyph(plan, ref buffer.Cur());
            buffer.NextGlyph();
        }
    }

    private static void SetGlyph(ShapePlan plan, ref GlyphInfo info) => info.NormalizerGlyph = plan.NominalGlyph(info.Codepoint);
}
