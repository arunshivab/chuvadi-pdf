// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Chuvadi.Pdf.Svg — path simplification for dense vector pages.
//
// CAD exports and large plots draw polylines with thousands of near-collinear
// points; emitted verbatim they dominate the SVG's size. Runs of consecutive
// LineTo segments are simplified with Ramer–Douglas–Peucker at a caller-chosen
// tolerance. Curves, MoveTo and Close commands pass through untouched, and a
// run's endpoints always survive, so connectivity and subpath structure are
// preserved exactly.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Rendering.DisplayList;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Simplifies straight-segment runs in a <see cref="PathGeometry"/> with the
/// Ramer–Douglas–Peucker algorithm. Only consecutive <see cref="PathCommand.LineTo"/>
/// runs are touched; moves, curves and closes pass through unchanged.
/// </summary>
internal static class PathSimplifier
{
    /// <summary>
    /// Returns a geometry equivalent to <paramref name="geometry"/> with every
    /// straight-segment run simplified at <paramref name="tolerance"/> (page
    /// points, maximum perpendicular deviation). Returns the input instance
    /// unchanged when nothing can be simplified.
    /// </summary>
    internal static PathGeometry Simplify(PathGeometry geometry, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (tolerance <= 0 || geometry.Segments.Count < 4)
        {
            return geometry;
        }

        List<PathSegment> output = new List<PathSegment>(geometry.Segments.Count);
        List<(double X, double Y)> run = new List<(double X, double Y)>();
        double curX = 0;
        double curY = 0;
        double runStartX = 0;
        double runStartY = 0;
        bool haveCurrent = false;
        bool changed = false;

        void FlushRun()
        {
            if (run.Count == 0)
            {
                return;
            }

            if (run.Count <= 2)
            {
                foreach ((double x, double y) in run)
                {
                    output.Add(new PathSegment(PathCommand.LineTo, x, y, 0, 0, 0, 0));
                }

                run.Clear();
                return;
            }

            // The run's polyline starts at the point before the first LineTo.
            List<(double X, double Y)> points = new List<(double X, double Y)>(run.Count + 1)
            {
                (runStartX, runStartY),
            };
            points.AddRange(run);

            List<(double X, double Y)> kept = DouglasPeucker(points, tolerance);
            if (kept.Count < points.Count)
            {
                changed = true;
            }

            // Skip the start point (already the current position).
            for (int i = 1; i < kept.Count; i++)
            {
                output.Add(new PathSegment(PathCommand.LineTo, kept[i].X, kept[i].Y, 0, 0, 0, 0));
            }

            run.Clear();
        }

        foreach (PathSegment seg in geometry.Segments)
        {
            switch (seg.Command)
            {
                case PathCommand.LineTo:
                    if (haveCurrent && run.Count == 0)
                    {
                        runStartX = curX;
                        runStartY = curY;
                    }

                    if (!haveCurrent)
                    {
                        // A LineTo with no known current point: pass through.
                        FlushRun();
                        output.Add(seg);
                    }
                    else
                    {
                        run.Add((seg.X1, seg.Y1));
                    }

                    curX = seg.X1;
                    curY = seg.Y1;
                    haveCurrent = true;
                    break;

                case PathCommand.MoveTo:
                    FlushRun();
                    output.Add(seg);
                    curX = seg.X1;
                    curY = seg.Y1;
                    haveCurrent = true;
                    break;

                case PathCommand.CubicTo:
                    FlushRun();
                    output.Add(seg);
                    curX = seg.X3;
                    curY = seg.Y3;
                    haveCurrent = true;
                    break;

                default:
                    FlushRun();
                    output.Add(seg);
                    break;
            }
        }

        FlushRun();
        return changed ? new PathGeometry(output) : geometry;
    }

    // Iterative Ramer–Douglas–Peucker (explicit stack, no recursion depth
    // limits on pathological inputs). Returns the kept points in order,
    // always including both endpoints.
    private static List<(double X, double Y)> DouglasPeucker(
        List<(double X, double Y)> points, double tolerance)
    {
        bool[] keep = new bool[points.Count];
        keep[0] = true;
        keep[points.Count - 1] = true;

        Stack<(int First, int Last)> ranges = new Stack<(int First, int Last)>();
        ranges.Push((0, points.Count - 1));

        while (ranges.Count > 0)
        {
            (int first, int last) = ranges.Pop();
            if (last - first < 2)
            {
                continue;
            }

            double maxDist = -1;
            int maxIndex = -1;
            (double ax, double ay) = points[first];
            (double bx, double by) = points[last];

            for (int i = first + 1; i < last; i++)
            {
                double d = PerpendicularDistance(points[i].X, points[i].Y, ax, ay, bx, by);
                if (d > maxDist)
                {
                    maxDist = d;
                    maxIndex = i;
                }
            }

            if (maxDist > tolerance && maxIndex > 0)
            {
                keep[maxIndex] = true;
                ranges.Push((first, maxIndex));
                ranges.Push((maxIndex, last));
            }
        }

        List<(double X, double Y)> kept = new List<(double X, double Y)>();
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                kept.Add(points[i]);
            }
        }

        return kept;
    }

    private static double PerpendicularDistance(
        double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared < 1e-12)
        {
            double ddx = px - ax;
            double ddy = py - ay;
            return Math.Sqrt((ddx * ddx) + (ddy * ddy));
        }

        // Distance from the point to the infinite line through a–b. RDP uses
        // the line, not the clamped segment, which is the classic formulation.
        double cross = ((px - ax) * dy) - ((py - ay) * dx);
        return Math.Abs(cross) / Math.Sqrt(lengthSquared);
    }
}
