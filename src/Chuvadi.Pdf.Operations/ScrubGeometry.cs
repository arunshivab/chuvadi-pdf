// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3.3 (transforms); §8.5.4 (clipping)
// Geometry helpers for PageScrubber: affine transforms and convex clipping.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Content;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Internal geometry helpers for the page scrubber: affine point transforms,
/// matrix inversion, and convex polygon / segment clipping against the
/// (possibly transformed) crop rectangle.
/// </summary>
internal static class ScrubGeometry
{
    private const double Epsilon = 1e-9;

    internal static (double X, double Y) Transform(Matrix3x3 m, double x, double y)
    {
        return (m.A * x + m.C * y + m.E, m.B * x + m.D * y + m.F);
    }

    /// <summary>Inverts an affine matrix; returns false when it is singular.</summary>
    internal static bool TryInvert(Matrix3x3 m, out Matrix3x3 inverse)
    {
        double det = m.A * m.D - m.B * m.C;
        if (Math.Abs(det) < Epsilon)
        {
            inverse = Matrix3x3.Identity;
            return false;
        }

        double ia = m.D / det;
        double ib = -m.B / det;
        double ic = -m.C / det;
        double id = m.A / det;
        double ie = -(m.E * ia + m.F * ic);
        double if_ = -(m.E * ib + m.F * id);
        inverse = new Matrix3x3(ia, ib, ic, id, ie, if_);
        return true;
    }

    /// <summary>
    /// Maps the four corners of an axis-aligned rectangle through <paramref name="m"/>,
    /// returning them as a convex quad (CCW order preserved for a positive-determinant matrix).
    /// </summary>
    internal static List<(double X, double Y)> TransformRectCorners(
        Matrix3x3 m, double x0, double y0, double x1, double y1)
    {
        return new List<(double X, double Y)>
        {
            Transform(m, x0, y0),
            Transform(m, x1, y0),
            Transform(m, x1, y1),
            Transform(m, x0, y1),
        };
    }

    /// <summary>
    /// Clips a subject polygon against a convex clip polygon (Sutherland–Hodgman).
    /// The clip polygon's winding is detected automatically. Returns the clipped
    /// polygon vertices, or an empty list when nothing remains.
    /// </summary>
    internal static List<(double X, double Y)> ClipPolygon(
        List<(double X, double Y)> subject, List<(double X, double Y)> clip)
    {
        if (subject.Count == 0 || clip.Count < 3)
        {
            return new List<(double X, double Y)>();
        }

        double sign = SignedArea(clip) >= 0 ? 1.0 : -1.0;
        List<(double X, double Y)> output = new List<(double X, double Y)>(subject);

        for (int i = 0; i < clip.Count; i++)
        {
            (double X, double Y) a = clip[i];
            (double X, double Y) b = clip[(i + 1) % clip.Count];

            List<(double X, double Y)> input = output;
            output = new List<(double X, double Y)>();
            if (input.Count == 0)
            {
                break;
            }

            for (int j = 0; j < input.Count; j++)
            {
                (double X, double Y) cur = input[j];
                (double X, double Y) prev = input[(j - 1 + input.Count) % input.Count];

                double sCur = sign * Side(a, b, cur);
                double sPrev = sign * Side(a, b, prev);

                if (sCur >= -Epsilon)
                {
                    if (sPrev < -Epsilon)
                    {
                        output.Add(Intersect(prev, cur, a, b));
                    }

                    output.Add(cur);
                }
                else if (sPrev >= -Epsilon)
                {
                    output.Add(Intersect(prev, cur, a, b));
                }
            }
        }

        return output;
    }

    /// <summary>
    /// Clips a line segment against a convex clip polygon, returning the
    /// surviving segment endpoints, or false when the segment is fully outside.
    /// </summary>
    internal static bool ClipSegment(
        List<(double X, double Y)> clip,
        (double X, double Y) p0, (double X, double Y) p1,
        out (double X, double Y) q0, out (double X, double Y) q1)
    {
        double sign = SignedArea(clip) >= 0 ? 1.0 : -1.0;
        double tEnter = 0.0;
        double tExit = 1.0;
        double dx = p1.X - p0.X;
        double dy = p1.Y - p0.Y;

        for (int i = 0; i < clip.Count; i++)
        {
            (double X, double Y) a = clip[i];
            (double X, double Y) b = clip[(i + 1) % clip.Count];

            // Inward normal for this edge (consistent winding via sign).
            double nx = sign * -(b.Y - a.Y);
            double ny = sign * (b.X - a.X);
            double dist0 = nx * (p0.X - a.X) + ny * (p0.Y - a.Y);
            double denom = nx * dx + ny * dy;

            if (Math.Abs(denom) < Epsilon)
            {
                if (dist0 < -Epsilon)
                {
                    q0 = p0;
                    q1 = p1;
                    return false;
                }

                continue;
            }

            double t = -dist0 / denom;
            if (denom > 0)
            {
                if (t > tEnter)
                {
                    tEnter = t;
                }
            }
            else
            {
                if (t < tExit)
                {
                    tExit = t;
                }
            }

            if (tEnter > tExit)
            {
                q0 = p0;
                q1 = p1;
                return false;
            }
        }

        q0 = (p0.X + tEnter * dx, p0.Y + tEnter * dy);
        q1 = (p0.X + tExit * dx, p0.Y + tExit * dy);
        return true;
    }

    /// <summary>Returns whether a point lies inside (or on) a convex clip polygon.</summary>
    internal static bool PointInside(List<(double X, double Y)> clip, double x, double y)
    {
        double sign = SignedArea(clip) >= 0 ? 1.0 : -1.0;
        for (int i = 0; i < clip.Count; i++)
        {
            (double X, double Y) a = clip[i];
            (double X, double Y) b = clip[(i + 1) % clip.Count];
            if (sign * Side(a, b, (x, y)) < -Epsilon)
            {
                return false;
            }
        }

        return true;
    }

    private static double Side((double X, double Y) a, (double X, double Y) b, (double X, double Y) p)
    {
        return (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
    }

    private static (double X, double Y) Intersect(
        (double X, double Y) p, (double X, double Y) q,
        (double X, double Y) a, (double X, double Y) b)
    {
        double dx = q.X - p.X;
        double dy = q.Y - p.Y;
        double ex = b.X - a.X;
        double ey = b.Y - a.Y;
        double denom = dx * ey - dy * ex;
        if (Math.Abs(denom) < Epsilon)
        {
            return q;
        }

        double t = ((a.X - p.X) * ey - (a.Y - p.Y) * ex) / denom;
        return (p.X + t * dx, p.Y + t * dy);
    }

    private static double SignedArea(List<(double X, double Y)> poly)
    {
        double area = 0.0;
        for (int i = 0; i < poly.Count; i++)
        {
            (double X, double Y) a = poly[i];
            (double X, double Y) b = poly[(i + 1) % poly.Count];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area * 0.5;
    }
}
