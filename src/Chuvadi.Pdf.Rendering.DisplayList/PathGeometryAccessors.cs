// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 3 — vector extraction accessors.
//
// Pure, allocation-light query accessors over PathGeometry: curve flattening,
// axis-aligned bounds, signed area, and point containment. These turn the raw
// move/line/cubic/close segment list into queryable geometry for extraction
// consumers, with no dependency on the renderer or graphics-state walking. Curves
// are flattened by adaptive de Casteljau subdivision to a caller-chosen tolerance.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Query accessors over <see cref="PathGeometry"/>: flattening, bounds, signed
/// area, and point containment.
/// </summary>
public static class PathGeometryAccessors
{
    private const double DefaultTolerance = 0.25;
    private const int MaxSubdivisionDepth = 32;

    /// <summary>
    /// Flattens the path into one polyline per subpath, subdividing cubic curves
    /// until they are within <paramref name="tolerance"/> of the true curve. A
    /// closed subpath ends with its start point repeated.
    /// </summary>
    /// <param name="geometry">The path to flatten.</param>
    /// <param name="tolerance">Maximum curve deviation, in geometry units.</param>
    /// <returns>The subpaths, each an ordered list of points.</returns>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Flatten(
        this PathGeometry geometry, double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        double tol = tolerance > 0 ? tolerance : DefaultTolerance;

        List<IReadOnlyList<(double X, double Y)>> subpaths =
            new List<IReadOnlyList<(double X, double Y)>>();
        List<(double X, double Y)>? current = null;
        double startX = 0;
        double startY = 0;
        double curX = 0;
        double curY = 0;

        foreach (PathSegment segment in geometry.Segments)
        {
            switch (segment.Command)
            {
                case PathCommand.MoveTo:
                    if (current is not null && current.Count > 0)
                    {
                        subpaths.Add(current);
                    }

                    current = new List<(double X, double Y)> { (segment.X1, segment.Y1) };
                    startX = segment.X1;
                    startY = segment.Y1;
                    curX = segment.X1;
                    curY = segment.Y1;
                    break;

                case PathCommand.LineTo:
                    current ??= StartImplicitSubpath(curX, curY, ref startX, ref startY);
                    current.Add((segment.X1, segment.Y1));
                    curX = segment.X1;
                    curY = segment.Y1;
                    break;

                case PathCommand.CubicTo:
                    current ??= StartImplicitSubpath(curX, curY, ref startX, ref startY);
                    FlattenCubic(
                        curX, curY,
                        segment.X1, segment.Y1,
                        segment.X2, segment.Y2,
                        segment.X3, segment.Y3,
                        tol, current, 0);
                    curX = segment.X3;
                    curY = segment.Y3;
                    break;

                case PathCommand.Close:
                    current?.Add((startX, startY));
                    curX = startX;
                    curY = startY;
                    break;

                default:
                    break;
            }
        }

        if (current is not null && current.Count > 0)
        {
            subpaths.Add(current);
        }

        return subpaths;
    }

    /// <summary>
    /// Computes the tight axis-aligned bounding box of the flattened geometry, or
    /// an empty rectangle at the origin when the path has no points.
    /// </summary>
    /// <param name="geometry">The path to measure.</param>
    /// <param name="tolerance">Curve-flattening tolerance.</param>
    /// <returns>The bounding box.</returns>
    public static Rect Bounds(this PathGeometry geometry, double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        bool any = false;

        foreach (IReadOnlyList<(double X, double Y)> subpath in geometry.Flatten(tolerance))
        {
            foreach ((double X, double Y) point in subpath)
            {
                any = true;
                if (point.X < minX) { minX = point.X; }
                if (point.Y < minY) { minY = point.Y; }
                if (point.X > maxX) { maxX = point.X; }
                if (point.Y > maxY) { maxY = point.Y; }
            }
        }

        if (!any)
        {
            return new Rect(0, 0, 0, 0);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Computes the signed area enclosed by the flattened subpaths (positive for
    /// counter-clockwise winding in a y-up frame). Each subpath is treated as
    /// closed.
    /// </summary>
    /// <param name="geometry">The path to measure.</param>
    /// <param name="tolerance">Curve-flattening tolerance.</param>
    /// <returns>The summed signed area.</returns>
    public static double SignedArea(this PathGeometry geometry, double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        double total = 0;
        foreach (IReadOnlyList<(double X, double Y)> subpath in geometry.Flatten(tolerance))
        {
            int n = subpath.Count;
            if (n < 3)
            {
                continue;
            }

            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                (double X, double Y) a = subpath[i];
                (double X, double Y) b = subpath[(i + 1) % n];
                sum += (a.X * b.Y) - (b.X * a.Y);
            }

            total += sum / 2.0;
        }

        return total;
    }

    /// <summary>
    /// Tests whether the point (<paramref name="x"/>, <paramref name="y"/>) lies
    /// inside the path under the given fill rule.
    /// </summary>
    /// <param name="geometry">The path.</param>
    /// <param name="x">Point x.</param>
    /// <param name="y">Point y.</param>
    /// <param name="rule">Fill rule deciding insideness.</param>
    /// <param name="tolerance">Curve-flattening tolerance.</param>
    /// <returns>True when the point is inside.</returns>
    public static bool Contains(
        this PathGeometry geometry, double x, double y, FillRule rule, double tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        bool oddParity = false;
        int winding = 0;

        foreach (IReadOnlyList<(double X, double Y)> subpath in geometry.Flatten(tolerance))
        {
            int n = subpath.Count;
            if (n < 2)
            {
                continue;
            }

            for (int i = 0; i < n; i++)
            {
                (double X, double Y) a = subpath[i];
                (double X, double Y) b = subpath[(i + 1) % n];

                if ((a.Y > y) != (b.Y > y))
                {
                    double t = (y - a.Y) / (b.Y - a.Y);
                    double crossX = a.X + (t * (b.X - a.X));
                    if (crossX > x)
                    {
                        oddParity = !oddParity;
                        winding += b.Y > a.Y ? 1 : -1;
                    }
                }
            }
        }

        return rule == FillRule.EvenOdd ? oddParity : winding != 0;
    }

    private static List<(double X, double Y)> StartImplicitSubpath(
        double curX, double curY, ref double startX, ref double startY)
    {
        startX = curX;
        startY = curY;
        return new List<(double X, double Y)> { (curX, curY) };
    }

    private static void FlattenCubic(
        double x0, double y0,
        double x1, double y1,
        double x2, double y2,
        double x3, double y3,
        double tolerance,
        List<(double X, double Y)> output,
        int depth)
    {
        if (depth >= MaxSubdivisionDepth || IsCubicFlat(x0, y0, x1, y1, x2, y2, x3, y3, tolerance))
        {
            output.Add((x3, y3));
            return;
        }

        double x01 = (x0 + x1) / 2;
        double y01 = (y0 + y1) / 2;
        double x12 = (x1 + x2) / 2;
        double y12 = (y1 + y2) / 2;
        double x23 = (x2 + x3) / 2;
        double y23 = (y2 + y3) / 2;
        double x012 = (x01 + x12) / 2;
        double y012 = (y01 + y12) / 2;
        double x123 = (x12 + x23) / 2;
        double y123 = (y12 + y23) / 2;
        double xm = (x012 + x123) / 2;
        double ym = (y012 + y123) / 2;

        FlattenCubic(x0, y0, x01, y01, x012, y012, xm, ym, tolerance, output, depth + 1);
        FlattenCubic(xm, ym, x123, y123, x23, y23, x3, y3, tolerance, output, depth + 1);
    }

    // Flat when both control points lie within `tolerance` of the chord p0->p3.
    private static bool IsCubicFlat(
        double x0, double y0,
        double x1, double y1,
        double x2, double y2,
        double x3, double y3,
        double tolerance)
    {
        double dx = x3 - x0;
        double dy = y3 - y0;
        double chordSquared = (dx * dx) + (dy * dy);

        if (chordSquared < 1e-18)
        {
            double c1 = ((x1 - x0) * (x1 - x0)) + ((y1 - y0) * (y1 - y0));
            double c2 = ((x2 - x0) * (x2 - x0)) + ((y2 - y0) * (y2 - y0));
            double tol2 = tolerance * tolerance;
            return c1 <= tol2 && c2 <= tol2;
        }

        double cross1 = Math.Abs(((x1 - x0) * dy) - ((y1 - y0) * dx));
        double cross2 = Math.Abs(((x2 - x0) * dy) - ((y2 - y0) * dx));
        double maxCross = Math.Max(cross1, cross2);

        // distance = cross / |chord|; flat when distance <= tolerance.
        return (maxCross * maxCross) <= (tolerance * tolerance * chordSquared);
    }
}
