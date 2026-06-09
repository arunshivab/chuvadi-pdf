// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FreeType autohinter — latin blue zones (alignment zones)
// PHASE: Phase 2 — Autohinting (Component 2: alignment zones)
// A single horizontal alignment zone (baseline, x-height, cap-height, ...).

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting.Autohint;

/// <summary>
/// A horizontal alignment zone ("blue zone"): a reference line that glyph
/// features snap to, such as the baseline, the x-height line, or the
/// cap-height line. Positions are in font design units (Y up).
/// </summary>
/// <remarks>
/// <para>
/// A zone has a flat reference position - the Y at which flat-edged glyphs sit
/// (the flat bottom of an "n" on the baseline, the flat top of an "x" at the
/// x-height) - and an overshoot band, because round glyphs extend slightly past
/// the flat line (the bottom of an "o" dips below the baseline; its top rises
/// above the x-height). The band runs from <see cref="Min"/> to <see cref="Max"/>
/// and contains <see cref="Position"/>.
/// </para>
/// <para>
/// <see cref="IsTop"/> distinguishes zones aligned at the top of glyphs
/// (x-height, cap-height) from those aligned at the bottom (baseline,
/// descender). The autohinter rounds a zone's <see cref="Position"/> to the
/// pixel grid and aligns the glyph coordinates that fall within its band to the
/// rounded line, keeping horizontal features crisp and consistent across the
/// whole font.
/// </para>
/// </remarks>
internal sealed class BlueZone
{
    /// <summary>
    /// Initialises a <see cref="BlueZone"/>.
    /// </summary>
    /// <param name="position">The flat reference Y in font units.</param>
    /// <param name="min">The lower edge of the overshoot band (font units).</param>
    /// <param name="max">The upper edge of the overshoot band (font units).</param>
    /// <param name="isTop">True for top-aligned zones (x-height, cap-height); false for bottom-aligned (baseline).</param>
    internal BlueZone(double position, double min, double max, bool isTop)
    {
        Position = position;
        Min = min;
        Max = max;
        IsTop = isTop;
    }

    /// <summary>Gets the flat reference Y in font design units.</summary>
    internal double Position { get; }

    /// <summary>Gets the lower edge of the overshoot band in font design units.</summary>
    internal double Min { get; }

    /// <summary>Gets the upper edge of the overshoot band in font design units.</summary>
    internal double Max { get; }

    /// <summary>Gets a value indicating whether this zone aligns the tops of glyphs.</summary>
    internal bool IsTop { get; }

    /// <summary>Gets the height of the overshoot band in font design units.</summary>
    internal double Height => Max - Min;

    /// <summary>
    /// Determines whether a Y coordinate lies within this zone's overshoot band.
    /// </summary>
    /// <param name="y">The Y coordinate to test (font units).</param>
    /// <returns>True when <paramref name="y"/> is within [<see cref="Min"/>, <see cref="Max"/>].</returns>
    internal bool Contains(double y)
    {
        return y >= Min && y <= Max;
    }
}
