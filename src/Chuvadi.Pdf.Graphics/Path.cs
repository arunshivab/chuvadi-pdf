// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.2 — Path construction
//        PDF 32000-1:2008 §8.5.3 — Path painting
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// A mutable path built from PathSegment operations.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// A mutable vector graphics path built from moveto, lineto, curve, and close operations.
/// </summary>
/// <remarks>
/// A <see cref="Path"/> is a sequence of <see cref="PathSegment"/> values.
/// Sub-paths begin with a MoveTo segment and end with a ClosePath segment
/// or the next MoveTo. Empty paths produce no output when painted.
///
/// PDF 32000-1:2008 §8.5.2 — Path construction operators.
/// PDF 32000-1:2008 §8.5.3 — Path painting operators.
/// </remarks>
public sealed class Path
{
    private readonly List<PathSegment> _segments;
    private PointF _currentPoint;
    private bool _hasCurrentPoint;

    /// <summary>Initialises an empty path.</summary>
    public Path()
    {
        _segments = new List<PathSegment>();
        _currentPoint = PointF.Zero;
        _hasCurrentPoint = false;
    }

    /// <summary>Gets the segments that make up this path.</summary>
    public IReadOnlyList<PathSegment> Segments => _segments;

    /// <summary>Gets the number of segments.</summary>
    public int Count => _segments.Count;

    /// <summary>Returns true when the path contains no segments.</summary>
    public bool IsEmpty => _segments.Count == 0;

    /// <summary>Gets the current point (last moveto or endpoint drawn to).</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no current point exists (path is empty and no MoveTo has been issued).
    /// </exception>
    public PointF CurrentPoint
    {
        get
        {
            if (!_hasCurrentPoint)
            {
                throw new InvalidOperationException(
                    "No current point — call MoveTo before drawing.");
            }

            return _currentPoint;
        }
    }

    // ── Construction methods ──────────────────────────────────────────────

    /// <summary>
    /// Begins a new sub-path at the given point.
    /// PDF operator 'm'. PDF 32000-1:2008 §8.5.2.1.
    /// </summary>
    public Path MoveTo(double x, double y)
    {
        _segments.Add(PathSegment.MoveTo(x, y));
        _currentPoint = new PointF(x, y);
        _hasCurrentPoint = true;
        return this;
    }

    /// <summary>Begins a new sub-path at the given point.</summary>
    public Path MoveTo(PointF point) => MoveTo(point.X, point.Y);

    /// <summary>
    /// Appends a straight line from the current point to (x, y).
    /// PDF operator 'l'. PDF 32000-1:2008 §8.5.2.2.
    /// </summary>
    public Path LineTo(double x, double y)
    {
        EnsureCurrentPoint("LineTo");
        _segments.Add(PathSegment.LineTo(x, y));
        _currentPoint = new PointF(x, y);
        return this;
    }

    /// <summary>Appends a line to the given point.</summary>
    public Path LineTo(PointF point) => LineTo(point.X, point.Y);

    /// <summary>
    /// Appends a cubic Bezier curve.
    /// PDF operator 'c': current point → cp1 → cp2 → endpoint.
    /// PDF 32000-1:2008 §8.5.2.3.
    /// </summary>
    public Path CubicBezierTo(PointF cp1, PointF cp2, PointF endpoint)
    {
        EnsureCurrentPoint("CubicBezierTo");
        _segments.Add(PathSegment.CubicBezierTo(cp1, cp2, endpoint));
        _currentPoint = endpoint;
        return this;
    }

    /// <summary>Appends a cubic Bezier curve from coordinates.</summary>
    public Path CubicBezierTo(
        double cx1, double cy1,
        double cx2, double cy2,
        double ex, double ey)
    {
        return CubicBezierTo(
            new PointF(cx1, cy1),
            new PointF(cx2, cy2),
            new PointF(ex, ey));
    }

    /// <summary>
    /// Closes the current sub-path with a line back to the start of the sub-path.
    /// PDF operator 'h'. PDF 32000-1:2008 §8.5.2.7.
    /// </summary>
    public Path ClosePath()
    {
        if (_hasCurrentPoint)
        {
            _segments.Add(PathSegment.ClosePath());
            _hasCurrentPoint = false;
        }

        return this;
    }

    // ── Convenience shapes ────────────────────────────────────────────────

    /// <summary>Appends a complete rectangle as a closed sub-path.</summary>
    public Path Rectangle(RectangleF rect)
    {
        return MoveTo(rect.X, rect.Y)
            .LineTo(rect.Right, rect.Y)
            .LineTo(rect.Right, rect.Top)
            .LineTo(rect.X, rect.Top)
            .ClosePath();
    }

    /// <summary>Appends a complete rectangle from coordinates.</summary>
    public Path Rectangle(double x, double y, double width, double height)
    {
        return Rectangle(new RectangleF(x, y, width, height));
    }

    /// <summary>
    /// Appends an ellipse approximated by four cubic Bezier curves.
    /// The Bezier approximation constant k ≈ 0.5523 is industry standard.
    /// </summary>
    public Path Ellipse(double cx, double cy, double rx, double ry)
    {
        const double K = 0.5522847498;
        double kx = rx * K;
        double ky = ry * K;

        return MoveTo(cx + rx, cy)
            .CubicBezierTo(cx + rx, cy + ky, cx + kx, cy + ry, cx, cy + ry)
            .CubicBezierTo(cx - kx, cy + ry, cx - rx, cy + ky, cx - rx, cy)
            .CubicBezierTo(cx - rx, cy - ky, cx - kx, cy - ry, cx, cy - ry)
            .CubicBezierTo(cx + kx, cy - ry, cx + rx, cy - ky, cx + rx, cy)
            .ClosePath();
    }

    /// <summary>
    /// Appends a closed triangular arrowhead pointing at <paramref name="tip"/>
    /// along the direction from <paramref name="tail"/> (geometry from
    /// <see cref="ArrowGeometry.HeadPoints"/>). A zero-length arrow appends
    /// nothing.
    /// </summary>
    /// <param name="tail">The start of the arrow shaft.</param>
    /// <param name="tip">The point the arrowhead touches.</param>
    /// <param name="headLength">Distance from the tip to the head's base.</param>
    /// <param name="headWidth">Full width of the head's base.</param>
    /// <returns>This path, for chaining.</returns>
    public Path ArrowHead(PointF tail, PointF tip, double headLength, double headWidth)
    {
        PointF[] head = ArrowGeometry.HeadPoints(tail, tip, headLength, headWidth);
        if (head.Length == 0)
        {
            return this;
        }

        return MoveTo(head[0])
            .LineTo(head[1])
            .LineTo(head[2])
            .ClosePath();
    }

    /// <summary>Removes all segments and resets the current point.</summary>
    public void Clear()
    {
        _segments.Clear();
        _hasCurrentPoint = false;
    }

    /// <summary>
    /// Returns the bounding box of all segment endpoints (not curve extrema).
    /// A full tight bound for curves requires flattening first.
    /// </summary>
    public RectangleF EndpointBounds()
    {
        if (_segments.Count == 0)
        {
            return RectangleF.Zero;
        }

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        foreach (PathSegment seg in _segments)
        {
            if (seg.Kind == PathSegmentKind.ClosePath)
            {
                continue;
            }

            ExpandBounds(seg.P0, ref minX, ref minY, ref maxX, ref maxY);

            if (seg.Kind == PathSegmentKind.CubicBezierTo)
            {
                ExpandBounds(seg.P1, ref minX, ref minY, ref maxX, ref maxY);
                ExpandBounds(seg.P2, ref minX, ref minY, ref maxX, ref maxY);
            }
        }

        return RectangleF.FromCorners(minX, minY, maxX, maxY);
    }

    private static void ExpandBounds(
        PointF p,
        ref double minX, ref double minY,
        ref double maxX, ref double maxY)
    {
        if (p.X < minX) { minX = p.X; }
        if (p.Y < minY) { minY = p.Y; }
        if (p.X > maxX) { maxX = p.X; }
        if (p.Y > maxY) { maxY = p.Y; }
    }

    private void EnsureCurrentPoint(string operation)
    {
        if (!_hasCurrentPoint)
        {
            throw new InvalidOperationException(
                $"{operation} requires a current point. Call MoveTo first.");
        }
    }
}
