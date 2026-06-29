// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;

namespace Chuvadi.Pdf.Text.Shaping.OpenType;

/// <summary>A mutable glyph slot in the shaping buffer.</summary>
internal sealed class GlyphSlot
{
    /// <summary>Current glyph id. Updated by GSUB substitution.</summary>
    internal int GlyphId { get; set; }

    /// <summary>Source cluster index in the original string.</summary>
    internal int Cluster { get; set; }

    /// <summary>Horizontal advance in font units. Updated by GPOS.</summary>
    internal int XAdvance { get; set; }

    /// <summary>Horizontal placement offset in font units.</summary>
    internal int XOffset { get; set; }

    /// <summary>Vertical placement offset in font units (up positive).</summary>
    internal int YOffset { get; set; }

    /// <summary>When true this slot has been deleted by a substitution and must be skipped.</summary>
    internal bool Deleted { get; set; }

    internal GlyphSlot(int glyphId, int cluster, int xAdvance)
    {
        GlyphId = glyphId;
        Cluster = cluster;
        XAdvance = xAdvance;
    }
}

/// <summary>
/// Mutable list of glyph slots that the shaping pipeline reads and writes.
/// Provides iteration helpers that skip deleted slots.
/// </summary>
internal sealed class GlyphBuffer
{
    private readonly List<GlyphSlot> _slots;

    internal GlyphBuffer(List<GlyphSlot> slots)
    {
        _slots = slots;
    }

    internal int Count => _slots.Count;

    internal GlyphSlot this[int index] => _slots[index];

    /// <summary>Returns the glyph id at <paramref name="index"/>, or -1 when deleted.</summary>
    internal int GlyphIdAt(int index)
        => _slots[index].Deleted ? -1 : _slots[index].GlyphId;

    /// <summary>
    /// Replaces the slot at <paramref name="index"/> with <paramref name="newGlyphId"/>
    /// and marks the slots at <paramref name="deleteFrom"/> through
    /// <paramref name="index"/>-1 as deleted (consumed by a multi-glyph substitution).
    /// </summary>
    internal void Substitute(int index, int deleteFrom, int newGlyphId)
    {
        _slots[index].GlyphId = newGlyphId;
        for (int i = deleteFrom; i < index; i++)
        {
            _slots[i].Deleted = true;
        }
    }

    /// <summary>Inserts <paramref name="newSlots"/> at <paramref name="index"/>, replacing one slot.</summary>
    internal void Expand(int index, List<GlyphSlot> newSlots)
    {
        _slots.RemoveAt(index);
        _slots.InsertRange(index, newSlots);
    }

    /// <summary>Returns active (non-deleted) slots in order.</summary>
    internal IEnumerable<GlyphSlot> ActiveSlots()
    {
        foreach (GlyphSlot slot in _slots)
        {
            if (!slot.Deleted)
            {
                yield return slot;
            }
        }
    }
}
