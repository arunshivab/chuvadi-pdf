// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.2.3 — Cubic Bezier curves
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// Converts Bezier curves to line segments for rasterization.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// Flattens a <see cref="Path"/> containing cubic Bezier curves into a
/// sequence of straight line segments suitable for scanline rasterization.
/// </summary>
/// <remarks>
/// Uses adaptive subdivision: a curve segment is split in two when its
/// midpoint deviates from the chord by more than the flatness tolerance.
/// This produces fewer segments for nearly-straight curves while maintaining
/// accuracy where curvature is high.
///
/// The output is a list of sub-paths, each being an ordered list of
/// <see cref="PointF"/> vertices. Closed sub-paths include the closing edge
/// implicitly (the rasterizer connects the last point back to the first).
///
/// PDF 32000-1:2008 §8.5.2.3 — Bezier curves.
/// </remarks>
public sealed class PathFlattener
{
    private readonly double _flatness;

    /// <summary>
    /// Initialises a <see cref="PathFlattener"/> with the given flatness tolerance.
    /// </summary>
    /// <param name="flatness">
    /// Maximum permitted deviation of a flattened segment from the true curve,
    /// in the same units as the path coordinates (PDF points).
    /// Smaller values = more segments = higher accuracy.
    /// Typical values: 0.1 (high quality) to 1.0 (fast).
    /// </param>
    public PathFlattener(double flatness = 0.25)
    {
        if (flatness <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flatness), "Flatness must be positive.");
        }

        _flatness = flatness;
    }

    /// <summary>
    /// Gets the flatness tolerance in path coordinate units.
    /// </summary>
    public double Flatness => _flatness;

    /// <summary>
    /// Flattens a path into a list of sub-paths.
    /// Each sub-path is a list of vertex points.
    /// </summary>
    /// <param name="path">The source path to flatten.</param>
    /// <returns>
    /// A list of sub-paths. Each sub-path is a non-empty list of points.
    /// The caller is responsible for applying the fill rule across sub-paths.
    /// </returns>
    public List<List<PointF>> Flatten(Path path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        List<List<PointF>> result = new List<List<PointF>>();
        List<PointF>? currentSubPath = null;
        PointF subPathStart = PointF.Zero;
        PointF currentPoint = PointF.Zero;

        foreach (PathSegment seg in path.Segments)
        {
            switch (seg.Kind)
            {
                case PathSegmentKind.MoveTo:
                    if (currentSubPath is not null && currentSubPath.Count > 1)
                    {
                        result.Add(currentSubPath);
                    }

                    currentSubPath = new List<PointF>();
                    currentSubPath.Add(seg.P0);
                    subPathStart = seg.P0;
                    currentPoint = seg.P0;
                    break;

                case PathSegmentKind.LineTo:
                    if (currentSubPath is null)
                    {
                        currentSubPath = new List<PointF>();
                        currentSubPath.Add(currentPoint);
                        subPathStart = currentPoint;
                    }

                    currentSubPath.Add(seg.P0);
                    currentPoint = seg.P0;
                    break;

                case PathSegmentKind.CubicBezierTo:
                    if (currentSubPath is null)
                    {
                        currentSubPath = new List<PointF>();
                        currentSubPath.Add(currentPoint);
                        subPathStart = currentPoint;
                    }

                    FlattenCubic(currentPoint, seg.P0, seg.P1, seg.P2, currentSubPath);
                    currentPoint = seg.P2;
                    break;

                case PathSegmentKind.ClosePath:
                    if (currentSubPath is not null && currentSubPath.Count > 1)
                    {
                        // Add closing edge only if start != end.
                        PointF last = currentSubPath[currentSubPath.Count - 1];

                        if (last.X != subPathStart.X || last.Y != subPathStart.Y)
                        {
                            currentSubPath.Add(subPathStart);
                        }

                        result.Add(currentSubPath);
                    }

                    currentSubPath = null;
                    currentPoint = subPathStart;
                    break;
            }
        }

        // Add any open sub-path.
        if (currentSubPath is not null && currentSubPath.Count > 1)
        {
            result.Add(currentSubPath);
        }

        return result;
    }

    // ── Adaptive cubic Bezier subdivision ────────────────────────────────

    // Deepest subdivision: at most 2^16 segments per curve (3.35.0, GFX-01).
    private const int MaxDepth = 16;

    /// <summary>
    /// Subdivides a cubic Bezier curve until every piece is within the
    /// flatness tolerance, or MaxDepth halvings deep (de Casteljau). Iterative,
    /// so no input can exhaust the stack (3.35.0, GFX-01; before, it recursed
    /// without a limit and a non-finite coordinate ended the process). A curve
    /// with a coordinate that is not finite adds nothing.
    /// </summary>
    private void FlattenCubic(
        PointF p0, PointF p1, PointF p2, PointF p3,
        List<PointF> output)
    {
        if (!IsFinite(p0) || !IsFinite(p1) || !IsFinite(p2) || !IsFinite(p3))
        {
            return;
        }

        Stack<(PointF P0, PointF P1, PointF P2, PointF P3, int Depth)> pending =
            new Stack<(PointF P0, PointF P1, PointF P2, PointF P3, int Depth)>();
        pending.Push((p0, p1, p2, p3, 0));
        while (pending.Count > 0)
        {
            (PointF a, PointF b, PointF c, PointF d, int depth) = pending.Pop();
            if (depth >= MaxDepth || Error(a, b, c, d) <= _flatness)
            {
                // Flat enough: emit the endpoint.
                output.Add(d);
                continue;
            }

            // Subdivide at t = 0.5.
            double m01x = (a.X + b.X) * 0.5;
            double m01y = (a.Y + b.Y) * 0.5;
            double m12x = (b.X + c.X) * 0.5;
            double m12y = (b.Y + c.Y) * 0.5;
            double m23x = (c.X + d.X) * 0.5;
            double m23y = (c.Y + d.Y) * 0.5;
            double m012x = (m01x + m12x) * 0.5;
            double m012y = (m01y + m12y) * 0.5;
            double m123x = (m12x + m23x) * 0.5;
            double m123y = (m12y + m23y) * 0.5;
            PointF mid = new PointF((m012x + m123x) * 0.5, (m012y + m123y) * 0.5);

            // The second half is pushed first so the first half is emitted first.
            pending.Push((mid, new PointF(m123x, m123y), new PointF(m23x, m23y), d, depth + 1));
            pending.Push((a, new PointF(m01x, m01y), new PointF(m012x, m012y), mid, depth + 1));
        }
    }

    // How far the control points deviate from the chord p0-p3.
    private static double Error(PointF p0, PointF p1, PointF p2, PointF p3)
    {
        double dx = p3.X - p0.X;
        double dy = p3.Y - p0.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-10)
        {
            return Math.Max(Distance(p1, p0), Distance(p2, p0));
        }

        double invLen = 1.0 / len;
        double nx = -dy * invLen;
        double ny = dx * invLen;
        double d1 = Math.Abs(nx * (p1.X - p0.X) + ny * (p1.Y - p0.Y));
        double d2 = Math.Abs(nx * (p2.X - p0.X) + ny * (p2.Y - p0.Y));
        return d1 > d2 ? d1 : d2;
    }

    private static bool IsFinite(PointF p) => double.IsFinite(p.X) && double.IsFinite(p.Y);

    private static double Distance(PointF a, PointF b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
