using System;

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting;

/// <summary>
/// A set of points the hinting interpreter manipulates: either the twilight
/// zone (zone 0) or the glyph zone (zone 1). Each point carries its current
/// (grid-fitted) position and its original (scaled but unhinted) position in
/// 26.6 fixed point, together with per-axis touch flags used by the
/// interpolation operators added in a later stage.
/// </summary>
internal sealed class Zone
{
    /// <summary>Creates a zone of the given capacity.</summary>
    /// <param name="pointCount">Number of points, including phantom points for a glyph zone.</param>
    /// <param name="contourEnds">Contour end-point indices (empty for the twilight zone).</param>
    /// <param name="onCurve">On-curve flags, one per point (all false for the twilight zone).</param>
    internal Zone(int pointCount, int[] contourEnds, bool[] onCurve)
    {
        ArgumentNullException.ThrowIfNull(contourEnds);
        ArgumentNullException.ThrowIfNull(onCurve);

        if (pointCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pointCount));
        }

        PointCount = pointCount;
        CurrentX = new int[pointCount];
        CurrentY = new int[pointCount];
        OriginalX = new int[pointCount];
        OriginalY = new int[pointCount];
        TouchedX = new bool[pointCount];
        TouchedY = new bool[pointCount];
        OnCurve = onCurve;
        ContourEnds = contourEnds;
    }

    /// <summary>The number of points in the zone.</summary>
    internal int PointCount { get; }

    /// <summary>Current (grid-fitted) X coordinates, 26.6 fixed point.</summary>
    internal int[] CurrentX { get; }

    /// <summary>Current (grid-fitted) Y coordinates, 26.6 fixed point.</summary>
    internal int[] CurrentY { get; }

    /// <summary>Original (scaled, unhinted) X coordinates, 26.6 fixed point.</summary>
    internal int[] OriginalX { get; }

    /// <summary>Original (scaled, unhinted) Y coordinates, 26.6 fixed point.</summary>
    internal int[] OriginalY { get; }

    /// <summary>Per-point touch flags on the X axis.</summary>
    internal bool[] TouchedX { get; }

    /// <summary>Per-point touch flags on the Y axis.</summary>
    internal bool[] TouchedY { get; }

    /// <summary>On-curve flags, one per point.</summary>
    internal bool[] OnCurve { get; }

    /// <summary>Contour end-point indices.</summary>
    internal int[] ContourEnds { get; }

    /// <summary>
    /// Creates an empty twilight zone of the given capacity: all points at the
    /// origin, no contours, no on-curve flags set.
    /// </summary>
    /// <param name="pointCount">The twilight point capacity from the font's <c>maxp</c> table.</param>
    internal static Zone CreateTwilight(int pointCount)
    {
        return new Zone(pointCount, Array.Empty<int>(), new bool[Math.Max(pointCount, 0)]);
    }
}
