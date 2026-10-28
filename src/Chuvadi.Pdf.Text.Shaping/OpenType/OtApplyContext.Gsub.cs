// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Port of HarfBuzz's GSUB subtable apply functions (OT/Layout/GSUB/*.hh);
// see THIRD-PARTY-NOTICES.md.

using System;

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>GSUB subtables.</summary>
internal sealed partial class OtApplyContext
{
    private const int MaxMapValue = 255;

    private bool ApplyGsubSubtable(int type, int st)
    {
        return type switch
        {
            1 => ApplySingleSubst(st),
            2 => ApplyMultipleSubst(st),
            3 => ApplyAlternateSubst(st),
            4 => ApplyLigatureSubst(st),
            5 => ApplyContext(st),
            6 => ApplyChainContext(st),
            8 => ApplyReverseChainSingleSubst(st),
            _ => false,
        };
    }

    private bool ApplySingleSubst(int st)
    {
        byte[] d = Data;
        int format = OtReader.U16(d, st);
        int glyph = Buffer.Cur().Codepoint;
        int index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), glyph);
        if (index == OtCommon.NotCovered)
        {
            return false;
        }

        if (format == 1)
        {
            ReplaceGlyph((glyph + OtReader.U16(d, st + 4)) & 0xFFFF);
            return true;
        }

        if (format == 2)
        {
            if (index >= OtReader.U16(d, st + 4))
            {
                return false;
            }

            ReplaceGlyph(OtReader.U16(d, st + 6 + (index * 2)));
            return true;
        }

        return false;
    }

    private bool ApplyMultipleSubst(int st)
    {
        byte[] d = Data;
        if (OtReader.U16(d, st) != 1)
        {
            return false;
        }

        int index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), Buffer.Cur().Codepoint);
        if (index == OtCommon.NotCovered)
        {
            return false;
        }

        // An absent sequence reads as an empty one (deletes the glyph), like HarfBuzz's Null.
        int sequence = OffsetAt(st, st + 6, index, OtReader.U16(d, st + 4));
        int count = sequence < 0 ? 0 : OtReader.U16(d, sequence);

        if (count == 1)
        {
            ReplaceGlyph(OtReader.U16(d, sequence + 2));
            return true;
        }

        if (count == 0)
        {
            Buffer.DeleteGlyph();
            return true;
        }

        int klass = (Buffer.Cur().GlyphProps & GlyphProps.Ligature) != 0 ? GlyphProps.BaseGlyph : 0;
        int ligId = GetLigId(Buffer.Cur());
        for (int i = 0; i < count; i++)
        {
            // If attached to a ligature, don't disturb that.
            if (ligId == 0)
            {
                SetLigPropsForMark(ref Buffer.Cur(), 0, i);
            }

            OutputGlyphForComponent(OtReader.U16(d, sequence + 2 + (i * 2)), klass);
        }

        Buffer.SkipGlyph();
        return true;
    }

    private bool ApplyAlternateSubst(int st)
    {
        byte[] d = Data;
        if (OtReader.U16(d, st) != 1)
        {
            return false;
        }

        int index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), Buffer.Cur().Codepoint);
        if (index == OtCommon.NotCovered)
        {
            return false;
        }

        int set = OffsetAt(st, st + 6, index, OtReader.U16(d, st + 4));
        int count = set < 0 ? 0 : OtReader.U16(d, set);
        if (count == 0)
        {
            return false;
        }

        uint glyphMask = Buffer.Cur().Mask;
        int shift = System.Numerics.BitOperations.TrailingZeroCount(LookupMask);
        int altIndex = (int)((LookupMask & glyphMask) >> shift);

        // If alt_index is MAX_VALUE, randomize feature if it is the rand feature.
        if (altIndex == MaxMapValue && RandomFeature)
        {
            RandomState = unchecked((uint)RandomState * 48271u) % 2147483647u;
            altIndex = (int)(RandomState % (uint)count) + 1;
        }

        if (altIndex > count || altIndex == 0)
        {
            return false;
        }

        ReplaceGlyph(OtReader.U16(d, set + 2 + ((altIndex - 1) * 2)));
        return true;
    }

    private bool ApplyLigatureSubst(int st)
    {
        byte[] d = Data;
        if (OtReader.U16(d, st) != 1)
        {
            return false;
        }

        int index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), Buffer.Cur().Codepoint);
        if (index == OtCommon.NotCovered)
        {
            return false;
        }

        int set = OffsetAt(st, st + 6, index, OtReader.U16(d, st + 4));
        if (set < 0)
        {
            return false;
        }

        int ligCount = OtReader.U16(d, set);
        for (int i = 0; i < ligCount; i++)
        {
            int lig = set + OtReader.U16(d, set + 2 + (i * 2));
            if (ApplyLigature(lig))
            {
                return true;
            }
        }

        return false;
    }

    private bool ApplyLigature(int lig)
    {
        byte[] d = Data;
        int ligGlyph = OtReader.U16(d, lig);
        int count = OtReader.U16(d, lig + 2);
        if (count == 0)
        {
            return false;
        }

        if (count == 1)
        {
            ReplaceGlyph(ligGlyph);
            return true;
        }

        if (count > MaxContextLength)
        {
            return false;
        }

        if (!MatchInput(count, lig + 4, MatchKind.Glyph, 0, out int matchEnd, out int totalComponentCount))
        {
            return false;
        }

        LigateInput(count, matchEnd, ligGlyph, totalComponentCount);
        return true;
    }

    private bool ApplyReverseChainSingleSubst(int st)
    {
        byte[] d = Data;
        if (OtReader.U16(d, st) != 1)
        {
            return false;
        }

        int index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), Buffer.Cur().Codepoint);
        if (index == OtCommon.NotCovered)
        {
            return false;
        }

        // No chaining to this type.
        if (NestingLevelLeft != MaxNestingLevel)
        {
            return false;
        }

        int backtrackCount = OtReader.U16(d, st + 4);
        int backtrack = st + 6;
        int lookaheadCountOffset = backtrack + (backtrackCount * 2);
        int lookaheadCount = OtReader.U16(d, lookaheadCountOffset);
        int lookahead = lookaheadCountOffset + 2;
        int substituteCountOffset = lookahead + (lookaheadCount * 2);
        int substituteCount = OtReader.U16(d, substituteCountOffset);
        if (index >= substituteCount)
        {
            return false;
        }

        if (MatchBacktrack(backtrackCount, backtrack, MatchKind.Coverage, st, out _) &&
            MatchLookahead(lookaheadCount, lookahead, MatchKind.Coverage, st, Buffer.Idx + 1, out _))
        {
            ReplaceGlyphInplace(OtReader.U16(d, substituteCountOffset + 2 + (index * 2)));

            // The main loop decreases idx; we don't.
            return true;
        }

        return false;
    }

    // ── would_apply (zero-context feature probing) ─────────────────────────

    /// <summary>
    /// Returns whether a GSUB lookup would substitute the given glyph sequence
    /// (HarfBuzz's hb_ot_layout_lookup_would_substitute).
    /// </summary>
    internal static bool WouldSubstitute(OtLayoutTable gsub, int lookupIndex, ReadOnlySpan<int> glyphs, bool zeroContext)
    {
        OtLookup? lookup = gsub.GetLookup(lookupIndex);
        if (lookup is null || glyphs.Length == 0 || !lookup.Digest.MayHave(glyphs[0]))
        {
            return false;
        }

        foreach (int st in lookup.Subtables)
        {
            if (WouldApplySubtable(gsub.Data, lookup.LookupType, st, glyphs, zeroContext))
            {
                return true;
            }
        }

        return false;
    }

    private static bool WouldApplySubtable(byte[] d, int type, int st, ReadOnlySpan<int> glyphs, bool zeroContext)
    {
        int format = OtReader.U16(d, st);
        switch (type)
        {
            case 1:
            case 2:
            case 3:
            case 8:
                return glyphs.Length == 1 && OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), glyphs[0]) != OtCommon.NotCovered;

            case 4:
                {
                    int index = OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), glyphs[0]);
                    if (index == OtCommon.NotCovered || index >= OtReader.U16(d, st + 4))
                    {
                        return false;
                    }

                    int set = st + OtReader.U16(d, st + 6 + (index * 2));
                    int ligCount = OtReader.U16(d, set);
                    for (int i = 0; i < ligCount; i++)
                    {
                        int lig = set + OtReader.U16(d, set + 2 + (i * 2));
                        int count = OtReader.U16(d, lig + 2);
                        if (count != glyphs.Length)
                        {
                            continue;
                        }

                        bool all = true;
                        for (int k = 1; k < count; k++)
                        {
                            if (glyphs[k] != OtReader.U16(d, lig + 4 + ((k - 1) * 2)))
                            {
                                all = false;
                                break;
                            }
                        }

                        if (all)
                        {
                            return true;
                        }
                    }

                    return false;
                }

            case 5:
                return WouldApplyContext(d, st, format, glyphs);

            case 6:
                return WouldApplyChainContext(d, st, format, glyphs, zeroContext);

            default:
                return false;
        }
    }

    private static bool WouldMatchInput(byte[] d, int count, int inputOffset, MatchKind kind, int data, ReadOnlySpan<int> glyphs)
    {
        if (count != glyphs.Length)
        {
            return false;
        }

        for (int i = 1; i < count; i++)
        {
            int value = OtReader.U16(d, inputOffset + ((i - 1) * 2));
            bool ok = kind switch
            {
                MatchKind.Glyph => glyphs[i] == value,
                MatchKind.Class => OtCommon.ClassOf(d, data, glyphs[i]) == value,
                MatchKind.Coverage => OtCommon.Coverage(d, data + value, glyphs[i]) != OtCommon.NotCovered,
                _ => true,
            };
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static bool WouldApplyContext(byte[] d, int st, int format, ReadOnlySpan<int> glyphs)
    {
        int ruleSet;
        MatchKind kind;
        int data;
        switch (format)
        {
            case 1:
                ruleSet = RuleSetAt(d, st, st + 6, OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), glyphs[0]), OtReader.U16(d, st + 4));
                kind = MatchKind.Glyph;
                data = 0;
                break;
            case 2:
                data = OtReader.U16(d, st + 4) == 0 ? 0 : st + OtReader.U16(d, st + 4);
                ruleSet = RuleSetAt(d, st, st + 8, OtCommon.ClassOf(d, data, glyphs[0]), OtReader.U16(d, st + 6));
                kind = MatchKind.Class;
                break;
            case 3:
                {
                    int glyphCount = OtReader.U16(d, st + 2);
                    return WouldMatchInput(d, glyphCount, st + 8, MatchKind.Coverage, st, glyphs);
                }

            default:
                return false;
        }

        if (ruleSet < 0)
        {
            return false;
        }

        int ruleCount = OtReader.U16(d, ruleSet);
        for (int r = 0; r < ruleCount; r++)
        {
            int rule = ruleSet + OtReader.U16(d, ruleSet + 2 + (r * 2));
            if (WouldMatchInput(d, OtReader.U16(d, rule), rule + 4, kind, data, glyphs))
            {
                return true;
            }
        }

        return false;
    }

    private static bool WouldApplyChainContext(byte[] d, int st, int format, ReadOnlySpan<int> glyphs, bool zeroContext)
    {
        int ruleSet;
        MatchKind kind;
        int data;
        switch (format)
        {
            case 1:
                ruleSet = RuleSetAt(d, st, st + 6, OtCommon.Coverage(d, st + OtReader.U16(d, st + 2), glyphs[0]), OtReader.U16(d, st + 4));
                kind = MatchKind.Glyph;
                data = 0;
                break;
            case 2:
                {
                    int input = OtReader.U16(d, st + 6);
                    data = input == 0 ? 0 : st + input;
                    ruleSet = RuleSetAt(d, st, st + 12, OtCommon.ClassOf(d, data, glyphs[0]), OtReader.U16(d, st + 10));
                    kind = MatchKind.Class;
                    break;
                }

            case 3:
                {
                    int backtrackCount = OtReader.U16(d, st + 2);
                    int inputCountOffset = st + 4 + (backtrackCount * 2);
                    int inputCount = OtReader.U16(d, inputCountOffset);
                    int lookaheadCount = OtReader.U16(d, inputCountOffset + 2 + (inputCount * 2));
                    return (!zeroContext || (backtrackCount == 0 && lookaheadCount == 0)) &&
                        WouldMatchInput(d, inputCount, inputCountOffset + 4, MatchKind.Coverage, st, glyphs);
                }

            default:
                return false;
        }

        if (ruleSet < 0)
        {
            return false;
        }

        int ruleCount = OtReader.U16(d, ruleSet);
        for (int r = 0; r < ruleCount; r++)
        {
            int rule = ruleSet + OtReader.U16(d, ruleSet + 2 + (r * 2));
            int backtrackCount = OtReader.U16(d, rule);
            int inputCountOffset = rule + 2 + (backtrackCount * 2);
            int inputCount = OtReader.U16(d, inputCountOffset);
            int lookaheadCount = OtReader.U16(d, inputCountOffset + 2 + (Math.Max(inputCount - 1, 0) * 2));
            if ((!zeroContext || (backtrackCount == 0 && lookaheadCount == 0)) &&
                WouldMatchInput(d, inputCount, inputCountOffset + 2, kind, data, glyphs))
            {
                return true;
            }
        }

        return false;
    }

    private static int RuleSetAt(byte[] d, int baseOffset, int arrayOffset, int index, int count)
    {
        if (index < 0 || index >= count)
        {
            return -1;
        }

        int o = OtReader.U16(d, arrayOffset + (index * 2));
        return o == 0 ? -1 : baseOffset + o;
    }
}
