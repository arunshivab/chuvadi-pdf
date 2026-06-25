// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.4 — Clipping path operators (W, W*)
// PHASE: v2.1 — PageRasterizer clip honouring

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering;

/// <summary>
/// A device-space clipping region used by <see cref="ScanlineRasterizer"/> to
/// restrict where a fill is painted.
/// </summary>
/// <remarks>
/// <para>
/// A region is built from one or more clip paths. Per PDF clipping semantics
/// (PDF 32000-1:2008 §8.5.4), the effective clip is the <em>intersection</em>
/// of every path: a pixel is inside the region only when it is inside all of
/// them.
/// </para>
/// <para>
/// Each clip path is classified once at construction. Axis-aligned rectangles
/// are stored as a single bounding interval and intersected with a cheap
/// min/max test (the common <c>re W n</c> case). Non-rectangular paths are
/// stored as edge tables and evaluated per scanline with the same
/// edge-crossing logic used for filling, honouring the path's fill rule.
/// </para>
/// </remarks>
public sealed class ClipRegion
{
    private const double Epsilon = 1e-6;

    private readonly List<ClipShape> _shapes;

    private ClipRegion(List<ClipShape> shapes, bool isEmpty)
    {
        _shapes = shapes;
        IsEmpty = isEmpty;
    }

    /// <summary>
    /// Gets a value indicating whether the region excludes everything, in
    /// which case no pixel is ever painted.
    /// </summary>
    public bool IsEmpty { get; }

    /// <summary>
    /// Builds a clip region from device-space clip paths and their fill rules.
    /// </summary>
    /// <param name="clipSubPaths">
    /// For each clip path, the flattened sub-paths in device space.
    /// </param>
    /// <param name="rules">The fill rule for each clip path (parallel to <paramref name="clipSubPaths"/>).</param>
    /// <returns>
    /// A region, or null when there are no clip paths (meaning "no clipping").
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="clipSubPaths"/> or <paramref name="rules"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the two lists differ in length.
    /// </exception>
    public static ClipRegion? Build(
        IReadOnlyList<List<List<PointF>>> clipSubPaths,
        IReadOnlyList<FillRule> rules)
    {
        ArgumentNullException.ThrowIfNull(clipSubPaths);
        ArgumentNullException.ThrowIfNull(rules);

        if (clipSubPaths.Count != rules.Count)
        {
            throw new ArgumentException(
                "Clip path and rule lists must have the same length.", nameof(rules));
        }

        if (clipSubPaths.Count == 0)
        {
            return null;
        }

        List<ClipShape> shapes = new List<ClipShape>(clipSubPaths.Count);
        bool isEmpty = false;

        for (int i = 0; i < clipSubPaths.Count; i++)
        {
            ClipShape shape = ClipShape.FromSubPaths(clipSubPaths[i], rules[i]);

            // A clip path that encloses no area excludes everything.
            if (shape.IsDegenerate)
            {
                isEmpty = true;
            }

            shapes.Add(shape);
        }

        return new ClipRegion(shapes, isEmpty);
    }

    /// <summary>
    /// Returns a region that is the intersection of this region and <paramref name="other"/>.
    /// </summary>
    /// <remarks>
    /// Every clip shape in both regions is already in device space, so the
    /// combined region simply carries all of their shapes: a pixel is inside it
    /// only when it is inside every shape of both regions. This is used to apply
    /// a parent (page-level) clip across a form XObject <c>Do</c>, intersecting
    /// it with the form's own inner clips.
    /// </remarks>
    /// <param name="other">The region to intersect with this one.</param>
    /// <returns>The combined intersection region.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    public ClipRegion Combine(ClipRegion other)
    {
        ArgumentNullException.ThrowIfNull(other);

        List<ClipShape> merged = new List<ClipShape>(_shapes.Count + other._shapes.Count);
        merged.AddRange(_shapes);
        merged.AddRange(other._shapes);
        return new ClipRegion(merged, IsEmpty || other.IsEmpty);
    }

    /// <summary>
    /// Returns the allowed x-intervals at the given scanline Y (sampled at the
    /// pixel centre), as the intersection of every clip shape's intervals. An
    /// empty list means nothing is allowed on this row.
    /// </summary>
    /// <param name="scanY">The scanline sample Y in device space.</param>
    /// <returns>Sorted, non-overlapping allowed intervals on this row.</returns>
    public List<(double Start, double End)> AllowedIntervals(double scanY)
    {
        List<(double Start, double End)>? acc = null;

        foreach (ClipShape shape in _shapes)
        {
            List<(double Start, double End)> intervals = shape.IntervalsAt(scanY);

            if (intervals.Count == 0)
            {
                return new List<(double Start, double End)>();
            }

            acc = acc is null ? intervals : IntersectIntervals(acc, intervals);

            if (acc.Count == 0)
            {
                return acc;
            }
        }

        return acc ?? new List<(double Start, double End)>();
    }

    private static List<(double Start, double End)> IntersectIntervals(
        List<(double Start, double End)> a,
        List<(double Start, double End)> b)
    {
        List<(double Start, double End)> result = new List<(double Start, double End)>();

        foreach ((double Start, double End) ia in a)
        {
            foreach ((double Start, double End) ib in b)
            {
                double lo = Math.Max(ia.Start, ib.Start);
                double hi = Math.Min(ia.End, ib.End);

                if (hi - lo > Epsilon)
                {
                    result.Add((lo, hi));
                }
            }
        }

        return result;
    }

    // ── One clip path, classified as rectangle or general polygon ──────────

    private sealed class ClipShape
    {
        private readonly bool _isRect;
        private readonly double _rectX0;
        private readonly double _rectX1;
        private readonly double _rectY0;
        private readonly double _rectY1;
        private readonly List<ClipEdge> _edges;
        private readonly FillRule _rule;

        private ClipShape(
            bool isRect,
            double rectX0, double rectY0, double rectX1, double rectY1,
            List<ClipEdge> edges, FillRule rule, bool isDegenerate)
        {
            _isRect = isRect;
            _rectX0 = rectX0;
            _rectY0 = rectY0;
            _rectX1 = rectX1;
            _rectY1 = rectY1;
            _edges = edges;
            _rule = rule;
            IsDegenerate = isDegenerate;
        }

        internal bool IsDegenerate { get; }

        internal static ClipShape FromSubPaths(List<List<PointF>> subPaths, FillRule rule)
        {
            if (TryAsRectangle(subPaths, out double x0, out double y0, out double x1, out double y1))
            {
                bool degenerate = (x1 - x0) <= Epsilon || (y1 - y0) <= Epsilon;
                return new ClipShape(true, x0, y0, x1, y1, new List<ClipEdge>(), rule, degenerate);
            }

            List<ClipEdge> edges = BuildEdges(subPaths);
            return new ClipShape(false, 0, 0, 0, 0, edges, rule, edges.Count == 0);
        }

        internal List<(double Start, double End)> IntervalsAt(double scanY)
        {
            List<(double Start, double End)> result = new List<(double Start, double End)>();

            if (_isRect)
            {
                if (scanY >= _rectY0 && scanY < _rectY1 && _rectX1 > _rectX0)
                {
                    result.Add((_rectX0, _rectX1));
                }

                return result;
            }

            // General polygon: gather crossings at this scanline and pair them
            // per the clip path's fill rule.
            List<(double X, int Winding)> crossings = new List<(double X, int Winding)>();

            foreach (ClipEdge e in _edges)
            {
                double eYMin = Math.Min(e.Y0, e.Y1);
                double eYMax = Math.Max(e.Y0, e.Y1);

                if (scanY < eYMin || scanY >= eYMax)
                {
                    continue;
                }

                double t = (scanY - e.Y0) / (e.Y1 - e.Y0);
                double x = e.X0 + t * (e.X1 - e.X0);
                crossings.Add((x, e.Winding));
            }

            if (crossings.Count < 2)
            {
                return result;
            }

            crossings.Sort((p, q) => p.X.CompareTo(q.X));

            if (_rule == FillRule.EvenOdd)
            {
                for (int i = 0; i + 1 < crossings.Count; i += 2)
                {
                    if (crossings[i + 1].X - crossings[i].X > Epsilon)
                    {
                        result.Add((crossings[i].X, crossings[i + 1].X));
                    }
                }
            }
            else
            {
                int winding = 0;
                double spanStart = 0;
                bool inside = false;

                foreach ((double X, int Winding) c in crossings)
                {
                    bool wasInside = inside;
                    winding += c.Winding;
                    inside = winding != 0;

                    if (!wasInside && inside)
                    {
                        spanStart = c.X;
                    }
                    else if (wasInside && !inside)
                    {
                        if (c.X - spanStart > Epsilon)
                        {
                            result.Add((spanStart, c.X));
                        }
                    }
                }
            }

            return result;
        }

        private static bool TryAsRectangle(
            List<List<PointF>> subPaths,
            out double x0, out double y0, out double x1, out double y1)
        {
            x0 = y0 = x1 = y1 = 0;

            if (subPaths.Count != 1)
            {
                return false;
            }

            List<PointF> pts = subPaths[0];

            // A flattened rectangle is 4 distinct corners, optionally with a
            // closing point repeating the first (5 entries).
            int n = pts.Count;

            if (n == 5 && Same(pts[0], pts[4]))
            {
                n = 4;
            }

            if (n != 4)
            {
                return false;
            }

            double minX = pts[0].X, maxX = pts[0].X;
            double minY = pts[0].Y, maxY = pts[0].Y;

            for (int i = 1; i < 4; i++)
            {
                if (pts[i].X < minX) { minX = pts[i].X; }
                if (pts[i].X > maxX) { maxX = pts[i].X; }
                if (pts[i].Y < minY) { minY = pts[i].Y; }
                if (pts[i].Y > maxY) { maxY = pts[i].Y; }
            }

            // Every vertex must sit on a corner of the bounding box for the
            // shape to be an axis-aligned rectangle.
            for (int i = 0; i < 4; i++)
            {
                bool onX = NearlyEqual(pts[i].X, minX) || NearlyEqual(pts[i].X, maxX);
                bool onY = NearlyEqual(pts[i].Y, minY) || NearlyEqual(pts[i].Y, maxY);

                if (!onX || !onY)
                {
                    return false;
                }
            }

            x0 = minX;
            y0 = minY;
            x1 = maxX;
            y1 = maxY;
            return true;
        }

        private static List<ClipEdge> BuildEdges(List<List<PointF>> subPaths)
        {
            List<ClipEdge> edges = new List<ClipEdge>();

            foreach (List<PointF> subPath in subPaths)
            {
                if (subPath.Count < 2)
                {
                    continue;
                }

                for (int i = 0; i < subPath.Count - 1; i++)
                {
                    PointF p0 = subPath[i];
                    PointF p1 = subPath[i + 1];

                    if (Math.Abs(p0.Y - p1.Y) < Epsilon)
                    {
                        continue;
                    }

                    int winding = p1.Y > p0.Y ? 1 : -1;
                    edges.Add(new ClipEdge(p0.X, p0.Y, p1.X, p1.Y, winding));
                }

                // Close the sub-path if the builder did not repeat the first point.
                PointF first = subPath[0];
                PointF last = subPath[subPath.Count - 1];

                if (!Same(first, last) && Math.Abs(first.Y - last.Y) >= Epsilon)
                {
                    int winding = first.Y > last.Y ? 1 : -1;
                    edges.Add(new ClipEdge(last.X, last.Y, first.X, first.Y, winding));
                }
            }

            return edges;
        }

        private static bool Same(PointF a, PointF b)
            => NearlyEqual(a.X, b.X) && NearlyEqual(a.Y, b.Y);

        private static bool NearlyEqual(double a, double b)
            => Math.Abs(a - b) < Epsilon;
    }

    private readonly struct ClipEdge
    {
        internal ClipEdge(double x0, double y0, double x1, double y1, int winding)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            Winding = winding;
        }

        internal double X0 { get; }
        internal double Y0 { get; }
        internal double X1 { get; }
        internal double Y1 { get; }
        internal int Winding { get; }
    }
}
