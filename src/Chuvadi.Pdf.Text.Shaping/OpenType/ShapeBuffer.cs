// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Port of HarfBuzz's hb_buffer_t in/out buffer model (hb-buffer.hh,
// hb-buffer.cc); see THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>Per-glyph shaping state (HarfBuzz's hb_glyph_info_t and its variables).</summary>
internal struct GlyphInfo
{
    /// <summary>The code point before glyph mapping, the glyph id after it.</summary>
    internal int Codepoint;

    /// <summary>Feature mask bits.</summary>
    internal uint Mask;

    /// <summary>The cluster value (UTF-16 index of the source text).</summary>
    internal int Cluster;

    /// <summary>Glyph class and substitution flags (<see cref="GlyphProps"/>).</summary>
    internal int GlyphProps;

    /// <summary>Ligature id and component (HarfBuzz lig_props).</summary>
    internal int LigProps;

    /// <summary>Syllable serial and type.</summary>
    internal int Syllable;

    /// <summary>General category, ignorable/hidden/continuation flags and combining class.</summary>
    internal int UnicodeProps;

    /// <summary>The complex shaper's category of the glyph (Indic category).</summary>
    internal int ShaperCategory;

    /// <summary>The complex shaper's auxiliary value (Indic position).</summary>
    internal int ShaperPosition;

    /// <summary>The glyph the normalizer mapped the code point to.</summary>
    internal int NormalizerGlyph;
}

/// <summary>A glyph position in font units (HarfBuzz's hb_glyph_position_t).</summary>
internal struct GlyphPos
{
    internal int XAdvance;
    internal int YAdvance;
    internal int XOffset;
    internal int YOffset;

    /// <summary>Relative index of the glyph this one attaches to (0 = none).</summary>
    internal int AttachChain;

    /// <summary>The attachment type (mark or cursive).</summary>
    internal int AttachType;
}

/// <summary>Glyph property flags (HarfBuzz's hb_ot_layout_glyph_props_flags_t).</summary>
internal static class GlyphProps
{
    internal const int BaseGlyph = 0x02;
    internal const int Ligature = 0x04;
    internal const int Mark = 0x08;
    internal const int Substituted = 0x10;
    internal const int Ligated = 0x20;
    internal const int Multiplied = 0x40;
    internal const int Preserve = Substituted | Ligated | Multiplied;
}

/// <summary>Unicode property bits stored in <see cref="GlyphInfo.UnicodeProps"/>.</summary>
internal static class UProps
{
    internal const int GenCatMask = 0x001F;
    internal const int Ignorable = 0x0020;
    internal const int Hidden = 0x0040;
    internal const int Continuation = 0x0080;
    internal const int CfZwj = 0x0100;
    internal const int CfZwnj = 0x0200;
    internal const int CfVs = 0x0400;
}

/// <summary>
/// The shaping buffer: an input array, an output array that may alias it,
/// and positions. Substitutions read at <see cref="Idx"/> and write at
/// <see cref="OutLen"/>, exactly as HarfBuzz's buffer does.
/// </summary>
internal sealed class ShapeBuffer
{
    internal const int ScratchHasDefaultIgnorables = 0x01;
    internal const int ScratchHasContinuations = 0x02;
    internal const int ScratchHasBrokenSyllable = 0x04;
    internal const int ScratchHasSpaceFallback = 0x08;
    internal const int ScratchHasCgj = 0x10;
    internal const int ScratchHasVariationSelectorFallback = 0x20;

    private GlyphInfo[] _spare = Array.Empty<GlyphInfo>();

    internal ShapeBuffer(int capacity)
    {
        Info = new GlyphInfo[Math.Max(capacity, 8)];
        OutInfo = Info;
        Pos = new GlyphPos[Info.Length];
    }

    internal GlyphInfo[] Info { get; private set; }

    internal GlyphInfo[] OutInfo { get; private set; }

    internal GlyphPos[] Pos { get; private set; }

    internal int Len { get; set; }

    internal int Idx { get; set; }

    internal int OutLen { get; set; }

    internal bool HaveOutput { get; private set; }

    internal int ScratchFlags { get; set; }

    internal LipiScript Script { get; set; }

    /// <summary>Gets or sets the remaining operation budget (HarfBuzz max_ops).</summary>
    internal int MaxOps { get; set; } = 16384;

    /// <summary>Gets whether the operation budget has not run out.</summary>
    internal bool Successful => MaxOps >= 0;

    /// <summary>A digest of the glyphs in the buffer (lets lookups that cannot match be skipped).</summary>
    internal SetDigest Digest;

    internal void UpdateDigest()
    {
        Digest = default;
        for (int i = 0; i < Len; i++)
        {
            Digest.Add(Info[i].Codepoint);
        }
    }

    private int Serial { get; set; }

    internal ref GlyphInfo Cur(int i = 0) => ref Info[Idx + i];

    internal ref GlyphInfo Prev() => ref OutInfo[OutLen > 0 ? OutLen - 1 : 0];

    internal int BacktrackLen => HaveOutput ? OutLen : Idx;

    internal int LookaheadLen => Len - Idx;

    internal int NextSerial()
    {
        Serial = (Serial + 1) & 0xFF;
        if (Serial == 0)
        {
            Serial = 1;
        }

        return Serial;
    }

    internal void Add(int codepoint, int cluster)
    {
        Ensure(Len + 1);
        Info[Len] = new GlyphInfo { Codepoint = codepoint, Cluster = cluster };
        Len++;
    }

    internal void ClearOutput()
    {
        HaveOutput = true;
        Idx = 0;
        OutLen = 0;
        OutInfo = Info;
    }

    internal void ClearPositions()
    {
        HaveOutput = false;
        OutLen = 0;
        OutInfo = Info;
        Array.Clear(Pos, 0, Len);
    }

    internal void Sync()
    {
        NextGlyphs(Len - Idx);
        if (!ReferenceEquals(OutInfo, Info))
        {
            _spare = Info;
            Info = OutInfo;
        }

        Len = OutLen;
        HaveOutput = false;
        OutLen = 0;
        OutInfo = Info;
        Idx = 0;
    }

    internal void Ensure(int size)
    {
        if (size <= Info.Length && size <= OutInfo.Length)
        {
            return;
        }

        int newSize = Math.Max(size, Info.Length * 2);
        bool aliased = ReferenceEquals(OutInfo, Info);
        GlyphInfo[] info = new GlyphInfo[newSize];
        Array.Copy(Info, info, Info.Length);
        Info = info;
        if (aliased)
        {
            OutInfo = Info;
        }
        else if (OutInfo.Length < newSize)
        {
            GlyphInfo[] o = new GlyphInfo[newSize];
            Array.Copy(OutInfo, o, OutInfo.Length);
            OutInfo = o;
        }

        if (Pos.Length < newSize)
        {
            GlyphPos[] p = new GlyphPos[newSize];
            Array.Copy(Pos, p, Pos.Length);
            Pos = p;
        }
    }

    private void MakeRoomFor(int numIn, int numOut)
    {
        Ensure(OutLen + numOut);
        if (ReferenceEquals(OutInfo, Info) && OutLen + numOut > Idx + numIn)
        {
            GlyphInfo[] o = _spare.Length >= Info.Length ? _spare : new GlyphInfo[Info.Length];
            _spare = Array.Empty<GlyphInfo>();
            Array.Copy(Info, o, OutLen);
            OutInfo = o;
        }
    }

    private void ShiftForward(int count)
    {
        MaxOps -= Len - Idx;
        Ensure(Len + count);
        Array.Copy(Info, Idx, Info, Idx + count, Len - Idx);
        if (Idx + count > Len)
        {
            Array.Clear(Info, Len, Idx + count - Len);
        }

        Len += count;
        Idx += count;
    }

    /// <summary>Moves the cursor to output position <paramref name="i"/> (HarfBuzz's move_to).</summary>
    internal void MoveTo(int i)
    {
        if (!HaveOutput)
        {
            Idx = i;
            return;
        }

        if (OutLen < i)
        {
            int count = i - OutLen;
            MaxOps -= count;
            MakeRoomFor(count, count);
            Array.Copy(Info, Idx, OutInfo, OutLen, count);
            Idx += count;
            OutLen += count;
        }
        else if (OutLen > i)
        {
            int count = OutLen - i;
            MaxOps -= count;
            if (Idx < count)
            {
                ShiftForward(count - Idx);
            }

            Idx -= count;
            OutLen -= count;
            Array.Copy(OutInfo, OutLen, Info, Idx, count);
        }
    }

    internal void ReplaceGlyphs(int numIn, int numOut, ReadOnlySpan<int> glyphs)
    {
        MakeRoomFor(numIn, numOut);
        MergeClusters(Idx, Idx + numIn);
        GlyphInfo orig = Idx < Len ? Info[Idx] : Prev();
        for (int i = 0; i < numOut; i++)
        {
            GlyphInfo g = orig;
            g.Codepoint = glyphs[i];
            OutInfo[OutLen + i] = g;
        }

        Idx += numIn;
        OutLen += numOut;
    }

    internal void ReplaceGlyph(int glyph)
    {
        if (ReferenceEquals(OutInfo, Info) && OutLen == Idx)
        {
            // Same effect as ReplaceGlyphs(1, 1) when nothing needs moving.
            MergeClusters(Idx, Idx + 1);
            Info[Idx].Codepoint = glyph;
            Idx++;
            OutLen++;
            return;
        }

        ReplaceGlyphs(1, 1, stackalloc int[] { glyph });
    }

    internal void OutputGlyph(int glyph) => ReplaceGlyphs(0, 1, stackalloc int[] { glyph });

    internal void OutputInfo(GlyphInfo info)
    {
        MakeRoomFor(0, 1);
        OutInfo[OutLen] = info;
        OutLen++;
    }

    internal void CopyGlyph() => OutputInfo(Info[Idx]);

    internal void NextGlyph()
    {
        if (HaveOutput)
        {
            if (!ReferenceEquals(OutInfo, Info) || OutLen != Idx)
            {
                Ensure(OutLen + 1);
                OutInfo[OutLen] = Info[Idx];
            }

            OutLen++;
        }

        Idx++;
    }

    internal void NextGlyphs(int n)
    {
        if (n <= 0)
        {
            return;
        }

        if (HaveOutput)
        {
            if (!ReferenceEquals(OutInfo, Info) || OutLen != Idx)
            {
                Ensure(OutLen + n);
                Array.Copy(Info, Idx, OutInfo, OutLen, n);
            }

            OutLen += n;
        }

        Idx += n;
    }

    internal void SkipGlyph() => Idx++;

    internal void ResetMasks(uint mask)
    {
        for (int j = 0; j < Len; j++)
        {
            Info[j].Mask = mask;
        }
    }

    internal void SetMasks(uint value, uint mask, int clusterStart, int clusterEnd)
    {
        if (mask == 0)
        {
            return;
        }

        uint notMask = ~mask;
        value &= mask;
        for (int i = 0; i < Len; i++)
        {
            if (clusterStart <= Info[i].Cluster && Info[i].Cluster < clusterEnd)
            {
                Info[i].Mask = (Info[i].Mask & notMask) | value;
            }
        }
    }

    internal void MergeClusters(int start, int end)
    {
        if (end - start < 2)
        {
            return;
        }

        MaxOps -= end - start;

        int cluster = Info[start].Cluster;
        for (int i = start + 1; i < end; i++)
        {
            cluster = Math.Min(cluster, Info[i].Cluster);
        }

        // Extend end.
        if (cluster != Info[end - 1].Cluster)
        {
            while (end < Len && Info[end - 1].Cluster == Info[end].Cluster)
            {
                end++;
            }
        }

        // Extend start.
        if (cluster != Info[start].Cluster)
        {
            while (Idx < start && Info[start - 1].Cluster == Info[start].Cluster)
            {
                start--;
            }
        }

        // If we hit the start of buffer, continue in out-buffer.
        if (Idx == start && Info[start].Cluster != cluster)
        {
            for (int i = OutLen; i > 0 && OutInfo[i - 1].Cluster == Info[start].Cluster; i--)
            {
                OutInfo[i - 1].Cluster = cluster;
            }
        }

        for (int i = start; i < end; i++)
        {
            Info[i].Cluster = cluster;
        }
    }

    internal void MergeOutClusters(int start, int end)
    {
        if (end - start < 2)
        {
            return;
        }

        int cluster = OutInfo[start].Cluster;
        for (int i = start + 1; i < end; i++)
        {
            cluster = Math.Min(cluster, OutInfo[i].Cluster);
        }

        while (start > 0 && OutInfo[start - 1].Cluster == OutInfo[start].Cluster)
        {
            start--;
        }

        while (end < OutLen && OutInfo[end - 1].Cluster == OutInfo[end].Cluster)
        {
            end++;
        }

        // If we hit the end of out-buffer, continue in buffer.
        if (end == OutLen)
        {
            for (int i = Idx; i < Len && Info[i].Cluster == OutInfo[end - 1].Cluster; i++)
            {
                Info[i].Cluster = cluster;
            }
        }

        for (int i = start; i < end; i++)
        {
            OutInfo[i].Cluster = cluster;
        }
    }

    /// <summary>Merges the current glyph's cluster away and skips it.</summary>
    internal void DeleteGlyph()
    {
        int cluster = Info[Idx].Cluster;
        if ((Idx + 1 < Len && cluster == Info[Idx + 1].Cluster) ||
            (OutLen > 0 && cluster == OutInfo[OutLen - 1].Cluster))
        {
            // Cluster survives; do nothing.
        }
        else if (OutLen > 0)
        {
            // Merge cluster backward.
            if (cluster < OutInfo[OutLen - 1].Cluster)
            {
                int oldCluster = OutInfo[OutLen - 1].Cluster;
                for (int i = OutLen; i > 0 && OutInfo[i - 1].Cluster == oldCluster; i--)
                {
                    OutInfo[i - 1].Cluster = cluster;
                }
            }
        }
        else if (Idx + 1 < Len)
        {
            // Merge cluster forward.
            MergeClusters(Idx, Idx + 2);
        }

        SkipGlyph();
    }

    internal void DeleteGlyphsInplace(Func<GlyphInfo, bool> filter)
    {
        int j = 0;
        int count = Len;
        for (int i = 0; i < count; i++)
        {
            if (filter(Info[i]))
            {
                int cluster = Info[i].Cluster;
                if (i + 1 < count && cluster == Info[i + 1].Cluster)
                {
                    continue;
                }

                if (j > 0)
                {
                    if (cluster < Info[j - 1].Cluster)
                    {
                        int oldCluster = Info[j - 1].Cluster;
                        for (int k = j; k > 0 && Info[k - 1].Cluster == oldCluster; k--)
                        {
                            Info[k - 1].Cluster = cluster;
                        }
                    }

                    continue;
                }

                if (i + 1 < count)
                {
                    MergeClusters(i, i + 2);
                }

                continue;
            }

            if (j != i)
            {
                Info[j] = Info[i];
                Pos[j] = Pos[i];
            }

            j++;
        }

        Len = j;
    }

    /// <summary>Insertion sort that merges the clusters of moved glyphs (HarfBuzz's sort).</summary>
    internal void Sort(int start, int end, Comparison<GlyphInfo> compare)
    {
        for (int i = start + 1; i < end; i++)
        {
            int j = i;
            while (j > start && compare(Info[j - 1], Info[i]) > 0)
            {
                j--;
            }

            if (i == j)
            {
                continue;
            }

            MergeClusters(j, i + 1);
            GlyphInfo t = Info[i];
            Array.Copy(Info, j, Info, j + 1, i - j);
            Info[j] = t;
        }
    }

    internal void ReverseRange(int start, int end)
    {
        Array.Reverse(Info, start, end - start);
        Array.Reverse(Pos, start, end - start);
    }

    /// <summary>Stable sort of a range without cluster merging (HarfBuzz's hb_stable_sort).</summary>
    internal void StableSort(int start, int end, Comparison<GlyphInfo> compare)
    {
        List<GlyphInfo> items = new List<GlyphInfo>(end - start);
        for (int i = start; i < end; i++)
        {
            items.Add(Info[i]);
        }

        // Insertion sort is stable and the ranges are syllables (short).
        for (int i = 1; i < items.Count; i++)
        {
            GlyphInfo t = items[i];
            int j = i - 1;
            while (j >= 0 && compare(items[j], t) > 0)
            {
                items[j + 1] = items[j];
                j--;
            }

            items[j + 1] = t;
        }

        for (int i = 0; i < items.Count; i++)
        {
            Info[start + i] = items[i];
        }
    }
}
