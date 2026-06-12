// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FreeType autohinter — latin segment and edge detection (Y direction)
// PHASE: Phase 2.7 — Autohinting (Component 3: horizontal edge detection)
// Finds horizontal edges (flat runs of outline points) in a raw glyph outline.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting.Autohint;

/// <summary>
/// A horizontal edge: a cluster of near-flat outline runs sharing a Y
/// position — a baseline foot, an x-height top, a crossbar boundary.
/// </summary>
internal sealed class HorizontalEdge
{
    internal HorizontalEdge(double y, bool isFloor, List<int> pointIndices, double extent)
    {
        Y = y;
        IsFloor = isFloor;
        PointIndices = pointIndices;
        Extent = extent;
    }

    /// <summary>The edge's representative Y position in font units (length-weighted mean).</summary>
    internal double Y { get; }

    /// <summary>
    /// True when the outline runs left-to-right along the edge, putting the
    /// glyph's ink above it (a "floor": baseline, the bottom of a crossbar).
    /// False when it runs right-to-left with ink below (a "ceiling": an
    /// x-height top, the top of a crossbar).
    /// </summary>
    internal bool IsFloor { get; }

    /// <summary>Indices of the contour points lying on this edge.</summary>
    internal List<int> PointIndices { get; }

    /// <summary>The total horizontal extent of the runs forming the edge, in font units.</summary>
    internal double Extent { get; }

    /// <summary>The fitted device-pixel Y, set by the fitting pass.</summary>
    internal double FittedY { get; set; }

    /// <summary>Whether the fitting pass has assigned <see cref="FittedY"/>.</summary>
    internal bool IsFitted { get; set; }

    /// <summary>Whether the edge was anchored to a blue zone.</summary>
    internal bool IsBlueAnchored { get; set; }
}

/// <summary>
/// Detects horizontal edges in a glyph outline: maximal runs of consecutive
/// on-curve points that are flat in Y, grouped across contours by Y proximity.
/// The Y-direction counterpart of <see cref="StemDetector"/>.
/// </summary>
internal static class HorizontalEdgeDetector
{
    // Consecutive on-curve points within this fraction of em in Y belong to
    // the same flat run. Real fonts draw flats exactly level, so the tolerance
    // only needs to absorb deliberate near-flat design wobble.
    private const double FlatToleranceEmFraction = 0.008;

    // A run must extend at least this fraction of em horizontally to count as
    // an edge segment — rejects point-sized nicks while keeping serif feet.
    private const double MinRunExtentEmFraction = 0.03;

    // Runs whose representative Y values lie within this fraction of em of
    // each other merge into one edge (the two serif feet of an "H", the
    // separate bowls of an "m" at the baseline).
    private const double GroupToleranceEmFraction = 0.015;

    /// <summary>
    /// Detects the horizontal edges of the supplied glyph.
    /// </summary>
    /// <param name="glyph">The raw glyph outline (font units); phantom points are ignored.</param>
    /// <param name="unitsPerEm">The font's units-per-em, used to scale tolerances.</param>
    /// <returns>The edges, ordered bottom to top. Empty for degenerate outlines.</returns>
    internal static List<HorizontalEdge> Detect(RawGlyph glyph, int unitsPerEm)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        if (unitsPerEm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitsPerEm), "Units-per-em must be positive.");
        }

        double flatTolerance = unitsPerEm * FlatToleranceEmFraction;
        double minExtent = unitsPerEm * MinRunExtentEmFraction;
        double groupTolerance = unitsPerEm * GroupToleranceEmFraction;

        List<Run> runs = CollectRuns(glyph, flatTolerance, minExtent);
        return GroupRuns(runs, groupTolerance);
    }

    // A maximal flat run of consecutive on-curve points within one contour.
    private sealed class Run
    {
        internal Run(double y, double extent, bool isFloor, List<int> points)
        {
            Y = y;
            Extent = extent;
            IsFloor = isFloor;
            Points = points;
        }

        internal double Y { get; }

        internal double Extent { get; }

        internal bool IsFloor { get; }

        internal List<int> Points { get; }
    }

    private static List<Run> CollectRuns(RawGlyph glyph, double flatTolerance, double minExtent)
    {
        List<Run> runs = new();
        int contourStart = 0;

        foreach (int contourEnd in glyph.ContourEnds)
        {
            // The contour's on-curve point indices, in outline order.
            List<int> onCurve = new();
            for (int i = contourStart; i <= contourEnd; i++)
            {
                if (glyph.OnCurve[i])
                {
                    onCurve.Add(i);
                }
            }
            contourStart = contourEnd + 1;

            int n = onCurve.Count;
            if (n < 2)
            {
                continue;
            }

            // Mark each cyclic step (p → next) as flat or not.
            bool[] flat = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int a = onCurve[i];
                int b = onCurve[(i + 1) % n];
                double dy = Math.Abs(glyph.Y[b] - glyph.Y[a]);
                double dx = Math.Abs(glyph.X[b] - glyph.X[a]);
                flat[i] = dy <= flatTolerance && dx > dy;
            }

            // Walk maximal flat runs (cyclically; each step consumed once).
            bool[] consumed = new bool[n];
            for (int start = 0; start < n; start++)
            {
                if (!flat[start] || consumed[start])
                {
                    continue;
                }

                // Rewind to the run's first step (stop after a full lap).
                int first = start;
                int guard = 0;
                while (flat[(first - 1 + n) % n] && guard < n)
                {
                    first = (first - 1 + n) % n;
                    guard++;
                }

                // Collect the run forward from `first`.
                List<int> points = new();
                double sumDx = 0;
                double minX = double.MaxValue;
                double maxX = double.MinValue;
                double weightedY = 0;
                double weight = 0;

                int step = first;
                points.Add(onCurve[step]);
                while (flat[step] && !consumed[step])
                {
                    consumed[step] = true;
                    int a = onCurve[step];
                    int b = onCurve[(step + 1) % n];
                    points.Add(b);

                    double dx = glyph.X[b] - glyph.X[a];
                    double segLen = Math.Abs(dx);
                    sumDx += dx;
                    weightedY += ((glyph.Y[a] + glyph.Y[b]) / 2.0) * segLen;
                    weight += segLen;
                    minX = Math.Min(minX, Math.Min(glyph.X[a], glyph.X[b]));
                    maxX = Math.Max(maxX, Math.Max(glyph.X[a], glyph.X[b]));

                    step = (step + 1) % n;
                }

                double extent = maxX - minX;
                if (extent < minExtent || weight <= 0)
                {
                    continue;
                }

                runs.Add(new Run(
                    y: weightedY / weight,
                    extent: extent,
                    isFloor: sumDx > 0,
                    points: points));
            }
        }

        return runs;
    }

    private static List<HorizontalEdge> GroupRuns(List<Run> runs, double groupTolerance)
    {
        List<HorizontalEdge> edges = new();
        if (runs.Count == 0)
        {
            return edges;
        }

        runs.Sort((a, b) => a.Y.CompareTo(b.Y));

        int groupStart = 0;
        for (int i = 1; i <= runs.Count; i++)
        {
            bool boundary = i == runs.Count ||
                runs[i].Y - runs[i - 1].Y > groupTolerance ||
                runs[i].IsFloor != runs[groupStart].IsFloor;
            if (!boundary)
            {
                continue;
            }

            double weightedY = 0;
            double weight = 0;
            double extent = 0;
            List<int> points = new();
            for (int r = groupStart; r < i; r++)
            {
                weightedY += runs[r].Y * runs[r].Extent;
                weight += runs[r].Extent;
                extent += runs[r].Extent;
                points.AddRange(runs[r].Points);
            }

            edges.Add(new HorizontalEdge(
                y: weightedY / weight,
                isFloor: runs[groupStart].IsFloor,
                pointIndices: points,
                extent: extent));

            groupStart = i;
        }

        return edges;
    }
}
