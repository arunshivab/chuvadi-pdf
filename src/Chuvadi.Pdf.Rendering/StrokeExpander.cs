// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.4.3 — Stroke properties
//        PDF 32000-1:2008 §8.5.3.2 — Stroking
// PHASE: Phase 2 — Chuvadi.Pdf.Rendering
// Expands a stroked path into a filled path for rasterization.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering;

/// <summary>
/// Converts a stroked path into a filled path by expanding each segment
/// by half the stroke width on each side.
/// </summary>
/// <remarks>
/// Phase 2 implements butt caps and bevel/miter joins.
/// Round caps and round joins are approximated with bevels.
/// The output is a list of sub-paths suitable for
/// <see cref="ScanlineRasterizer"/> with the non-zero winding fill rule.
///
/// PDF 32000-1:2008 §8.5.3.2 — Stroking.
/// </remarks>
public sealed class StrokeExpander
{
    /// <summary>
    /// Expands the flattened sub-paths of a stroked path into a filled outline.
    /// </summary>
    /// <param name="subPaths">Flattened sub-paths from <see cref="PathFlattener"/>.</param>
    /// <param name="style">The stroke style (width, cap, join, miter limit).</param>
    /// <returns>
    /// A list of filled sub-paths forming the stroke outline.
    /// Use <see cref="FillRule.NonZeroWinding"/> when rasterizing.
    /// </returns>
    public List<List<PointF>> Expand(List<List<PointF>> subPaths, StrokeStyle style)
    {
        if (subPaths is null)
        {
            throw new ArgumentNullException(nameof(subPaths));
        }

        if (style is null)
        {
            throw new ArgumentNullException(nameof(style));
        }

        List<List<PointF>> result = new List<List<PointF>>();
        double halfWidth = style.Width * 0.5;

        if (halfWidth <= 0)
        {
            return result;
        }

        foreach (List<PointF> subPath in subPaths)
        {
            if (subPath.Count < 2)
            {
                continue;
            }

            List<List<PointF>> expanded = ExpandSubPath(subPath, halfWidth, style);
            result.AddRange(expanded);
        }

        return result;
    }

    // ── Sub-path expansion ────────────────────────────────────────────────

    private static List<List<PointF>> ExpandSubPath(
        List<PointF> points, double halfWidth, StrokeStyle style)
    {
        int n = points.Count;
        bool isClosed = points[0].X == points[n - 1].X &&
                        points[0].Y == points[n - 1].Y;

        List<PointF> left = new List<PointF>(n * 2);
        List<PointF> right = new List<PointF>(n * 2);

        // Track the previous segment direction so each interior join can be
        // mitred (offset lines intersected) instead of naively snapped to one
        // segment, which would tilt a flat edge by up to half the stroke width.
        double prevDx = 0, prevDy = 0;
        bool havePrev = false;

        for (int i = 0; i < n - 1; i++)
        {
            PointF p0 = points[i];
            PointF p1 = points[i + 1];

            (PointF l0, PointF l1, PointF r0, PointF r1) =
                OffsetSegment(p0, p1, halfWidth);

            double curDx = p1.X - p0.X;
            double curDy = p1.Y - p0.Y;

            if (i == 0)
            {
                left.Add(l0);
                right.Add(r0);
            }
            else if (havePrev && left.Count > 0)
            {
                // Mitre the join: intersect the previous and current offset
                // lines on each side. Fall back to the current segment's start
                // offset (bevel) when the lines are parallel or the mitre is
                // excessively long.
                PointF prevL = left[left.Count - 1];
                PointF prevR = right[right.Count - 1];

                if (TryMitre(prevL, prevDx, prevDy, l0, curDx, curDy, halfWidth, out PointF mitL))
                {
                    left[left.Count - 1] = mitL;
                }
                else
                {
                    left[left.Count - 1] = l0;
                }

                if (TryMitre(prevR, prevDx, prevDy, r0, curDx, curDy, halfWidth, out PointF mitR))
                {
                    right[right.Count - 1] = mitR;
                }
                else
                {
                    right[right.Count - 1] = r0;
                }
            }

            left.Add(l1);
            right.Add(r1);

            prevDx = curDx;
            prevDy = curDy;
            havePrev = true;
        }

        // Apply end caps for open paths
        if (!isClosed && n >= 2)
        {
            AddCap(left, right, points[0], points[1], halfWidth, style.Cap, start: true);
            AddCap(left, right, points[n - 1], points[n - 2], halfWidth, style.Cap, start: false);
        }

        // Build the outline polygon: left side forward + right side backward
        List<PointF> outline = new List<PointF>(left.Count + right.Count);
        outline.AddRange(left);

        for (int i = right.Count - 1; i >= 0; i--)
        {
            outline.Add(right[i]);
        }

        if (outline.Count > 0)
        {
            outline.Add(outline[0]); // close
        }

        return new List<List<PointF>> { outline };
    }

    private static bool TryMitre(
        PointF prevOff, double prevDx, double prevDy,
        PointF curOff, double curDx, double curDy,
        double halfWidth, out PointF result)
    {
        result = curOff;
        double denom = (prevDx * curDy) - (prevDy * curDx);

        if (Math.Abs(denom) < 1e-9)
        {
            return false;
        }

        double t = (((curOff.X - prevOff.X) * curDy) - ((curOff.Y - prevOff.Y) * curDx)) / denom;
        PointF p = new PointF(prevOff.X + (prevDx * t), prevOff.Y + (prevDy * t));

        double mdx = p.X - curOff.X;
        double mdy = p.Y - curOff.Y;

        if (Math.Sqrt((mdx * mdx) + (mdy * mdy)) > halfWidth * 8.0)
        {
            return false;
        }

        result = p;
        return true;
    }

    private static (PointF l0, PointF l1, PointF r0, PointF r1) OffsetSegment(
        PointF p0, PointF p1, double halfWidth)
    {
        double dx = p1.X - p0.X;
        double dy = p1.Y - p0.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);

        if (len < 1e-10)
        {
            return (p0, p1, p0, p1);
        }

        // Perpendicular unit vector (left side of direction of travel)
        double nx = -dy / len;
        double ny = dx / len;

        PointF l0 = new PointF(p0.X + nx * halfWidth, p0.Y + ny * halfWidth);
        PointF l1 = new PointF(p1.X + nx * halfWidth, p1.Y + ny * halfWidth);
        PointF r0 = new PointF(p0.X - nx * halfWidth, p0.Y - ny * halfWidth);
        PointF r1 = new PointF(p1.X - nx * halfWidth, p1.Y - ny * halfWidth);

        return (l0, l1, r0, r1);
    }

    private static void AddCap(
        List<PointF> left, List<PointF> right,
        PointF endPt, PointF prevPt,
        double halfWidth, LineCap cap, bool start)
    {
        if (cap == LineCap.Square)
        {
            double dx = endPt.X - prevPt.X;
            double dy = endPt.Y - prevPt.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);

            if (len < 1e-10)
            {
                return;
            }

            double ex = dx / len * halfWidth;
            double ey = dy / len * halfWidth;

            if (start)
            {
                left[0] = new PointF(left[0].X - ex, left[0].Y - ey);
                right[0] = new PointF(right[0].X - ex, right[0].Y - ey);
            }
            else
            {
                int last = left.Count - 1;
                left[last] = new PointF(left[last].X + ex, left[last].Y + ey);
                right[last] = new PointF(right[last].X + ex, right[last].Y + ey);
            }
        }
        // Butt and Round caps: butt needs no adjustment; round approximated as bevel in Phase 2
    }
}
