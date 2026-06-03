// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Apple TrueType Reference Manual — glyph instructions, phantom points
//        ISO/IEC 14496-22 (OpenType) §glyf
// PHASE: Phase 2 — TrueType bytecode hinting (Stage 1: raw-glyph foundation)
// Raw, un-cubicized glyph point data that the hinting interpreter operates on.

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting;

/// <summary>
/// A glyph's raw outline as the TrueType bytecode interpreter needs it: the
/// integer control points in font design units (Y up), their on/off-curve
/// flags, the contour end indices, the glyph's instruction bytecode, and the
/// four appended phantom points.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="Chuvadi.Pdf.Fonts.Rendering.GlyphOutline"/>, this model is
/// deliberately NOT cubicized: the interpreter must move the original on- and
/// off-curve points before any curve conversion happens. Coordinates are in
/// font design units; scaling to 26.6 fixed-point device units is performed
/// later, per point size, by the interpreter.
/// </para>
/// <para>
/// The point arrays (<see cref="X"/>, <see cref="Y"/>, <see cref="OnCurve"/>)
/// hold <see cref="RealPointCount"/> contour points followed by exactly four
/// phantom points at indices <see cref="RealPointCount"/> through
/// <c>RealPointCount + 3</c>: horizontal origin, horizontal advance, vertical
/// origin, and vertical advance. The interpreter addresses phantom points by
/// these trailing indices, which is why they are appended to the same arrays
/// rather than stored separately.
/// </para>
/// <para>
/// This type is produced by the loader's raw-glyph parse path and is not used
/// by the default (non-hinted) rendering pipeline.
/// </para>
/// </remarks>
internal sealed class RawGlyph
{
    /// <summary>
    /// Initialises a <see cref="RawGlyph"/> from parsed point data.
    /// </summary>
    /// <param name="x">Point X coordinates (font units), with phantoms appended.</param>
    /// <param name="y">Point Y coordinates (font units), with phantoms appended.</param>
    /// <param name="onCurve">On-curve flags, parallel to <paramref name="x"/>.</param>
    /// <param name="contourEnds">End-point index of each contour.</param>
    /// <param name="instructions">The glyph's TrueType instruction bytecode.</param>
    /// <param name="realPointCount">Number of contour points before the phantoms.</param>
    internal RawGlyph(
        int[] x,
        int[] y,
        bool[] onCurve,
        int[] contourEnds,
        byte[] instructions,
        int realPointCount)
    {
        X = x;
        Y = y;
        OnCurve = onCurve;
        ContourEnds = contourEnds;
        Instructions = instructions;
        RealPointCount = realPointCount;
    }

    /// <summary>Point X coordinates in font design units; four phantom points appended.</summary>
    internal int[] X { get; }

    /// <summary>Point Y coordinates in font design units; four phantom points appended.</summary>
    internal int[] Y { get; }

    /// <summary>On-curve flags parallel to <see cref="X"/> and <see cref="Y"/>.</summary>
    internal bool[] OnCurve { get; }

    /// <summary>End-point index of each contour, into the contour-point range.</summary>
    internal int[] ContourEnds { get; }

    /// <summary>The glyph's TrueType instruction bytecode (may be empty).</summary>
    internal byte[] Instructions { get; }

    /// <summary>The number of real contour points, before the four phantom points.</summary>
    internal int RealPointCount { get; }

    /// <summary>The total number of points, including the four phantom points.</summary>
    internal int PointCount => X.Length;

    /// <summary>The number of contours (excludes phantom points).</summary>
    internal int ContourCount => ContourEnds.Length;
}
