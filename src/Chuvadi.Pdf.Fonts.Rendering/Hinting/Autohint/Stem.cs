// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FreeType autohinter — segment/stem model (latin hinting)
// PHASE: Phase 2 — Autohinting (Component 1: stem detection)
// A detected vertical stem: the two opposing near-vertical edges of a stroke.

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting.Autohint;

/// <summary>
/// A vertical stem detected in a glyph outline: a pair of opposing
/// near-vertical edges (the left and right sides of a vertical stroke),
/// expressed as X coordinates in font design units together with the
/// vertical extent over which the two edges overlap.
/// </summary>
/// <remarks>
/// <para>
/// Stems are the primary input to grid-fitting: the autohinter snaps a stem's
/// position and width to the pixel grid so vertical strokes render crisply.
/// A stem is purely horizontal information — its <see cref="MinX"/> and
/// <see cref="MaxX"/> are the left and right edge positions, and
/// <see cref="Width"/> is their separation (the stroke thickness in font
/// units).
/// </para>
/// <para>
/// Coordinates are in the glyph's font design units (the same units as
/// <see cref="RawGlyph.X"/>), not device pixels; scaling and snapping happen
/// in later autohinting stages.
/// </para>
/// </remarks>
internal sealed class Stem
{
    /// <summary>
    /// Initialises a <see cref="Stem"/> from its two edge positions and the
    /// vertical span over which they overlap.
    /// </summary>
    /// <param name="minX">The left edge X coordinate (font units).</param>
    /// <param name="maxX">The right edge X coordinate (font units); must be greater than or equal to <paramref name="minX"/>.</param>
    /// <param name="minY">The lower bound of the overlapping vertical span (font units).</param>
    /// <param name="maxY">The upper bound of the overlapping vertical span (font units).</param>
    internal Stem(double minX, double maxX, double minY, double maxY)
    {
        MinX = minX;
        MaxX = maxX;
        MinY = minY;
        MaxY = maxY;
    }

    /// <summary>Gets the left edge X coordinate in font design units.</summary>
    internal double MinX { get; }

    /// <summary>Gets the right edge X coordinate in font design units.</summary>
    internal double MaxX { get; }

    /// <summary>Gets the lower bound of the overlapping vertical span (font units).</summary>
    internal double MinY { get; }

    /// <summary>Gets the upper bound of the overlapping vertical span (font units).</summary>
    internal double MaxY { get; }

    /// <summary>Gets the stem width (stroke thickness) in font design units.</summary>
    internal double Width => MaxX - MinX;

    /// <summary>Gets the X coordinate of the stem centre in font design units.</summary>
    internal double CenterX => (MinX + MaxX) / 2.0;

    /// <summary>Gets the height of the overlapping vertical span in font design units.</summary>
    internal double Height => MaxY - MinY;
}
