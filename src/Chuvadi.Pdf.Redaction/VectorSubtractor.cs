// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5 — Path construction and painting
// PHASE: Chuvadi.Pdf.Redaction — precise vector subtraction (VectorRedactionMode.SubtractPrecise)
//
// Removes only the geometry whose ink can touch a redaction rectangle, instead
// of dropping an intersecting path wholesale. Safety contract (B15): a KEEP
// decision is made only when the kept geometry's ink provably misses every
// redaction rectangle (stroke ink allowance = half line width scaled by the
// miter limit, curve extents bounded by their control hulls); every doubtful
// construct — curves crossing a region, fill-rule interactions between
// subpaths, non-invertible matrices, unexpected operands — falls back to
// removal, never to retention. Removal is byte-level: subtracted geometry does
// not survive anywhere in the output stream.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Redaction;

/// <summary>
/// Subtracts redaction regions from a buffered painted path at the finest
/// granularity that is provably safe: whole subpaths are kept when their ink
/// cannot touch any redaction rectangle, straight stroked segments are split
/// at region boundaries, and axis-aligned rectangle fills are decomposed into
/// the surviving sub-rectangles. Everything else is removed (over-redaction in
/// doubt, per the library's conservative redaction principle).
/// </summary>
internal static class VectorSubtractor
{
    private const double Epsilon = 1e-9;
    private const double MinPieceLength = 1e-6;

    /// <summary>
    /// Attempts a precise subtraction of the redaction regions from the
    /// buffered path. Returns the replacement content-stream fragment
    /// (construction operators plus the paint operator), an empty string when
    /// nothing survives, or <see langword="null"/> when the construct is too
    /// doubtful to subtract precisely and the caller must drop the whole path
    /// (the legacy behaviour).
    /// </summary>
    internal static string? TrySubtract(
        List<(List<PdfToken> Operands, string Op)> buffer,
        string paintOp,
        RedactState state,
        IReadOnlyList<RedactContext> contexts)
    {
        bool strokes = paintOp == "S" || paintOp == "s"
            || paintOp == "B" || paintOp == "B*" || paintOp == "b" || paintOp == "b*";
        bool fillsAndStrokes = paintOp == "B" || paintOp == "B*" || paintOp == "b" || paintOp == "b*";
        bool strokeOnly = paintOp == "S" || paintOp == "s";

        // An ExtGState whose line width could not be resolved means the stroke
        // ink allowance is unknown — keeping anything would be unsafe.
        if (strokes && !state.StrokeStateReliable)
        {
            return null;
        }

        List<Subpath>? subpaths = ParseSubpaths(buffer);
        if (subpaths is null || subpaths.Count == 0)
        {
            return null;
        }

        // s / b / b* close the final subpath before painting; model the closing
        // edge so its ink participates in the keep tests. A subpath closed this
        // way (not by an explicit h) needs an explicit h when re-emitted under
        // plain S.
        if (paintOp == "s" || paintOp == "b" || paintOp == "b*")
        {
            Subpath last = subpaths[subpaths.Count - 1];
            if (!last.Closed)
            {
                last.Close();
                last.NeedsExplicitClose = true;
            }
        }

        List<PointF[]>? quads = BuildUserSpaceQuads(state, contexts);
        if (quads is null || quads.Count == 0)
        {
            return null;
        }

        double margin = 0.0;
        List<PointF[]> keepTestQuads = quads;
        if (strokes)
        {
            margin = Math.Max(state.LineWidth, 1.0) / 2.0 * (Math.Max(state.MiterLimit, 1.0) + 1.0);
            keepTestQuads = new List<PointF[]>(quads.Count);
            foreach (PointF[] quad in quads)
            {
                PointF[]? expanded = ExpandConvexQuad(quad, margin);
                if (expanded is null)
                {
                    return null;
                }

                keepTestQuads.Add(expanded);
            }
        }

        // Classify each subpath: Keep (ink provably misses every region),
        // Remove (fully inside a region — removal is always safe), or Partial.
        SubpathFate[] fates = new SubpathFate[subpaths.Count];
        bool anyNonKeep = false;
        for (int i = 0; i < subpaths.Count; i++)
        {
            fates[i] = Classify(subpaths[i], quads, keepTestQuads);
            if (fates[i] != SubpathFate.Keep)
            {
                anyNonKeep = true;
            }
        }

        if (!anyNonKeep)
        {
            // The whole-path bounding box overlapped, but no individual
            // subpath's ink can touch a region: keep everything verbatim.
            return EmitVerbatim(subpaths, paintOp);
        }

        if (fillsAndStrokes)
        {
            // Composite paints couple the fill rule and the stroke; partial
            // subtraction of either risks changing the other. Doubt → drop.
            return null;
        }

        if (strokeOnly)
        {
            return EmitStrokes(subpaths, fates, keepTestQuads);
        }

        return EmitFills(subpaths, fates, quads, paintOp);
    }

    // ── Classification ─────────────────────────────────────────────────────

    private enum SubpathFate
    {
        Keep,
        Remove,
        Partial,
    }

    private static SubpathFate Classify(
        Subpath subpath, List<PointF[]> rawQuads, List<PointF[]> keepTestQuads)
    {
        bool touchesAny = false;
        foreach (PointF[] quad in keepTestQuads)
        {
            if (BoxOverlapsQuad(subpath.MinX, subpath.MinY, subpath.MaxX, subpath.MaxY, quad))
            {
                touchesAny = true;
                break;
            }
        }

        if (!touchesAny)
        {
            return SubpathFate.Keep;
        }

        // Fully inside one raw quad (every point, control points included) —
        // removal is unconditionally safe.
        foreach (PointF[] quad in rawQuads)
        {
            if (AllPointsInside(subpath, quad))
            {
                return SubpathFate.Remove;
            }
        }

        return SubpathFate.Partial;
    }

    private static bool AllPointsInside(Subpath subpath, PointF[] quad)
    {
        if (!PointInConvex(subpath.Start, quad))
        {
            return false;
        }

        foreach (PathSegment seg in subpath.Segments)
        {
            if (!PointInConvex(seg.P1, quad))
            {
                return false;
            }

            if (seg.IsCurve
                && (!PointInConvex(seg.C1, quad) || !PointInConvex(seg.C2, quad)))
            {
                return false;
            }
        }

        return true;
    }

    // ── Emission ───────────────────────────────────────────────────────────

    private static string EmitVerbatim(List<Subpath> subpaths, string paintOp)
    {
        StringBuilder sb = new StringBuilder();
        foreach (Subpath subpath in subpaths)
        {
            foreach (string raw in subpath.RawOps)
            {
                sb.Append(raw).Append('\n');
            }
        }

        sb.Append(paintOp).Append('\n');
        return sb.ToString();
    }

    private static string? EmitFills(
        List<Subpath> subpaths, SubpathFate[] fates, List<PointF[]> quads, string paintOp)
    {
        if (subpaths.Count > 1)
        {
            // Removing one subpath of a multi-subpath fill can change the fill
            // rule's result elsewhere (holes fill in). Doubt → drop whole.
            return null;
        }

        Subpath only = subpaths[0];
        if (fates[0] == SubpathFate.Remove)
        {
            return string.Empty;
        }

        // Partial single-subpath fill: only an axis-aligned rectangle minus
        // axis-aligned regions decomposes exactly; anything else drops whole
        // (which, with a single subpath, equals the legacy behaviour).
        if (!only.IsRect)
        {
            return string.Empty;
        }

        double minX = Math.Min(only.RectX, only.RectX + only.RectW);
        double maxX = Math.Max(only.RectX, only.RectX + only.RectW);
        double minY = Math.Min(only.RectY, only.RectY + only.RectH);
        double maxY = Math.Max(only.RectY, only.RectY + only.RectH);

        List<(double MinX, double MinY, double MaxX, double MaxY)> pieces =
            new List<(double MinX, double MinY, double MaxX, double MaxY)>
            {
                (minX, minY, maxX, maxY),
            };

        foreach (PointF[] quad in quads)
        {
            if (!BoxOverlapsQuad(minX, minY, maxX, maxY, quad))
            {
                continue;
            }

            if (!IsAxisAlignedQuad(quad, out double qMinX, out double qMinY, out double qMaxX, out double qMaxY))
            {
                // A rotated region over a rectangle fill: no exact axis-aligned
                // decomposition exists. Doubt → the rectangle goes whole.
                return string.Empty;
            }

            // Inflate the subtracted region by a hair so the emitted pieces'
            // 6-decimal coordinate rounding can never nudge an edge back
            // inside the region (over-redaction by 1e-4 pt is invisible).
            const double SafetyInflation = 1e-4;
            qMinX -= SafetyInflation;
            qMinY -= SafetyInflation;
            qMaxX += SafetyInflation;
            qMaxY += SafetyInflation;

            List<(double MinX, double MinY, double MaxX, double MaxY)> next =
                new List<(double MinX, double MinY, double MaxX, double MaxY)>();
            foreach ((double MinX, double MinY, double MaxX, double MaxY) piece in pieces)
            {
                SubtractAxisRect(piece, qMinX, qMinY, qMaxX, qMaxY, next);
            }

            pieces = next;
            if (pieces.Count == 0)
            {
                return string.Empty;
            }
        }

        StringBuilder sb = new StringBuilder();
        foreach ((double MinX, double MinY, double MaxX, double MaxY) piece in pieces)
        {
            sb.Append(Num(piece.MinX)).Append(' ')
                .Append(Num(piece.MinY)).Append(' ')
                .Append(Num(piece.MaxX - piece.MinX)).Append(' ')
                .Append(Num(piece.MaxY - piece.MinY)).Append(" re\n");
        }

        sb.Append(paintOp).Append('\n');
        return sb.ToString();
    }

    private static string EmitStrokes(
        List<Subpath> subpaths, SubpathFate[] fates, List<PointF[]> expandedQuads)
    {
        StringBuilder sb = new StringBuilder();
        bool emittedAny = false;

        for (int i = 0; i < subpaths.Count; i++)
        {
            Subpath subpath = subpaths[i];
            if (fates[i] == SubpathFate.Remove)
            {
                continue;
            }

            if (fates[i] == SubpathFate.Keep)
            {
                foreach (string raw in subpath.RawOps)
                {
                    sb.Append(raw).Append('\n');
                }

                if (subpath.NeedsExplicitClose)
                {
                    sb.Append("h\n");
                }

                emittedAny = true;
                continue;
            }

            // Partial: curves crossing a region cannot be split safely — the
            // whole subpath is removed (over-redaction in doubt).
            if (subpath.HasCurves)
            {
                continue;
            }

            emittedAny |= EmitClippedPolyline(sb, subpath, expandedQuads);
        }

        if (!emittedAny)
        {
            return string.Empty;
        }

        // Kept-whole closed subpaths carry their own explicit "h"; split
        // polylines are open by construction, so plain S is always correct.
        sb.Append("S\n");
        return sb.ToString();
    }

    private static bool EmitClippedPolyline(
        StringBuilder sb, Subpath subpath, List<PointF[]> expandedQuads)
    {
        bool emitted = false;
        bool penDown = false;
        PointF penAt = default;

        foreach (PathSegment seg in subpath.Segments)
        {
            List<(double A, double B)> kept = KeptIntervals(seg.P0, seg.P1, expandedQuads);
            foreach ((double A, double B) interval in kept)
            {
                PointF a = Lerp(seg.P0, seg.P1, interval.A);
                PointF b = Lerp(seg.P0, seg.P1, interval.B);
                if (a.DistanceTo(b) < MinPieceLength)
                {
                    continue;
                }

                if (!penDown || penAt.DistanceTo(a) > Epsilon)
                {
                    sb.Append(Num(a.X)).Append(' ').Append(Num(a.Y)).Append(" m\n");
                }

                sb.Append(Num(b.X)).Append(' ').Append(Num(b.Y)).Append(" l\n");
                penDown = true;
                penAt = b;
                emitted = true;
            }

            if (kept.Count == 0 || Math.Abs(kept[kept.Count - 1].B - 1.0) > Epsilon)
            {
                // The segment's tail was subtracted: the next piece must not be
                // joined to this one.
                penDown = false;
            }
        }

        return emitted;
    }

    private static List<(double A, double B)> KeptIntervals(
        PointF p0, PointF p1, List<PointF[]> quads)
    {
        List<(double A, double B)> kept = new List<(double A, double B)>
        {
            (0.0, 1.0),
        };

        foreach (PointF[] quad in quads)
        {
            if (!SegmentQuadInterval(p0, p1, quad, out double tIn, out double tOut))
            {
                continue;
            }

            List<(double A, double B)> next = new List<(double A, double B)>();
            foreach ((double A, double B) piece in kept)
            {
                if (tOut <= piece.A || tIn >= piece.B)
                {
                    next.Add(piece);
                    continue;
                }

                if (tIn > piece.A)
                {
                    next.Add((piece.A, tIn));
                }

                if (tOut < piece.B)
                {
                    next.Add((tOut, piece.B));
                }
            }

            kept = next;
            if (kept.Count == 0)
            {
                break;
            }
        }

        return kept;
    }

    // ── Geometry primitives ────────────────────────────────────────────────

    // Clips the segment p0→p1 against a convex CCW quad (Cyrus–Beck). Returns
    // the [tIn, tOut] parameter interval of the portion inside, or false when
    // the segment misses the quad.
    private static bool SegmentQuadInterval(
        PointF p0, PointF p1, PointF[] quad, out double tIn, out double tOut)
    {
        tIn = 0.0;
        tOut = 1.0;
        double dx = p1.X - p0.X;
        double dy = p1.Y - p0.Y;

        for (int i = 0; i < quad.Length; i++)
        {
            PointF v0 = quad[i];
            PointF v1 = quad[(i + 1) % quad.Length];

            // Inward normal of a CCW edge.
            double nx = -(v1.Y - v0.Y);
            double ny = v1.X - v0.X;

            double a = nx * (p0.X - v0.X) + ny * (p0.Y - v0.Y);
            double b = nx * dx + ny * dy;

            if (Math.Abs(b) < Epsilon)
            {
                if (a < 0)
                {
                    return false;
                }

                continue;
            }

            double t = -a / b;
            if (b > 0)
            {
                if (t > tIn)
                {
                    tIn = t;
                }
            }
            else if (t < tOut)
            {
                tOut = t;
            }

            if (tIn >= tOut)
            {
                return false;
            }
        }

        return true;
    }

    private static bool PointInConvex(PointF p, PointF[] quad)
    {
        for (int i = 0; i < quad.Length; i++)
        {
            PointF v0 = quad[i];
            PointF v1 = quad[(i + 1) % quad.Length];
            double cross = (v1.X - v0.X) * (p.Y - v0.Y) - (v1.Y - v0.Y) * (p.X - v0.X);
            if (cross < -Epsilon)
            {
                return false;
            }
        }

        return true;
    }

    // Separating-axis overlap test between an axis-aligned box and a convex quad.
    private static bool BoxOverlapsQuad(
        double minX, double minY, double maxX, double maxY, PointF[] quad)
    {
        // Quad projected on the box's axes.
        double qMinX = double.MaxValue;
        double qMaxX = double.MinValue;
        double qMinY = double.MaxValue;
        double qMaxY = double.MinValue;
        foreach (PointF v in quad)
        {
            qMinX = Math.Min(qMinX, v.X);
            qMaxX = Math.Max(qMaxX, v.X);
            qMinY = Math.Min(qMinY, v.Y);
            qMaxY = Math.Max(qMaxY, v.Y);
        }

        if (qMaxX < minX || qMinX > maxX || qMaxY < minY || qMinY > maxY)
        {
            return false;
        }

        // Box corners projected on each quad edge normal.
        for (int i = 0; i < quad.Length; i++)
        {
            PointF v0 = quad[i];
            PointF v1 = quad[(i + 1) % quad.Length];
            double nx = -(v1.Y - v0.Y);
            double ny = v1.X - v0.X;

            double quadMin = double.MaxValue;
            double quadMax = double.MinValue;
            foreach (PointF v in quad)
            {
                double proj = nx * v.X + ny * v.Y;
                quadMin = Math.Min(quadMin, proj);
                quadMax = Math.Max(quadMax, proj);
            }

            double p00 = (nx * minX) + (ny * minY);
            double p10 = (nx * maxX) + (ny * minY);
            double p01 = (nx * minX) + (ny * maxY);
            double p11 = (nx * maxX) + (ny * maxY);
            double boxMin = Math.Min(Math.Min(p00, p10), Math.Min(p01, p11));
            double boxMax = Math.Max(Math.Max(p00, p10), Math.Max(p01, p11));

            if (boxMax < quadMin || boxMin > quadMax)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAxisAlignedQuad(
        PointF[] quad, out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = double.MaxValue;
        minY = double.MaxValue;
        maxX = double.MinValue;
        maxY = double.MinValue;

        for (int i = 0; i < quad.Length; i++)
        {
            PointF v0 = quad[i];
            PointF v1 = quad[(i + 1) % quad.Length];
            double ex = v1.X - v0.X;
            double ey = v1.Y - v0.Y;
            if (Math.Abs(ex) > Epsilon && Math.Abs(ey) > Epsilon)
            {
                return false;
            }

            minX = Math.Min(minX, v0.X);
            minY = Math.Min(minY, v0.Y);
            maxX = Math.Max(maxX, v0.X);
            maxY = Math.Max(maxY, v0.Y);
        }

        return true;
    }

    private static void SubtractAxisRect(
        (double MinX, double MinY, double MaxX, double MaxY) piece,
        double qMinX, double qMinY, double qMaxX, double qMaxY,
        List<(double MinX, double MinY, double MaxX, double MaxY)> result)
    {
        if (qMinX >= piece.MaxX || qMaxX <= piece.MinX
            || qMinY >= piece.MaxY || qMaxY <= piece.MinY)
        {
            result.Add(piece);
            return;
        }

        // Left strip.
        if (qMinX > piece.MinX)
        {
            AddPiece(result, piece.MinX, piece.MinY, qMinX, piece.MaxY);
        }

        // Right strip.
        if (qMaxX < piece.MaxX)
        {
            AddPiece(result, qMaxX, piece.MinY, piece.MaxX, piece.MaxY);
        }

        double midMinX = Math.Max(piece.MinX, qMinX);
        double midMaxX = Math.Min(piece.MaxX, qMaxX);

        // Bottom strip.
        if (qMinY > piece.MinY)
        {
            AddPiece(result, midMinX, piece.MinY, midMaxX, qMinY);
        }

        // Top strip.
        if (qMaxY < piece.MaxY)
        {
            AddPiece(result, midMinX, qMaxY, midMaxX, piece.MaxY);
        }
    }

    private static void AddPiece(
        List<(double MinX, double MinY, double MaxX, double MaxY)> result,
        double minX, double minY, double maxX, double maxY)
    {
        if (maxX - minX > MinPieceLength && maxY - minY > MinPieceLength)
        {
            result.Add((minX, minY, maxX, maxY));
        }
    }

    // Offsets each edge of a convex CCW quad outward by the margin and
    // re-intersects adjacent edges. Returns null for degenerate quads.
    private static PointF[]? ExpandConvexQuad(PointF[] quad, double margin)
    {
        int n = quad.Length;
        (double Px, double Py, double Dx, double Dy)[] lines =
            new (double Px, double Py, double Dx, double Dy)[n];

        for (int i = 0; i < n; i++)
        {
            PointF v0 = quad[i];
            PointF v1 = quad[(i + 1) % n];
            double ex = v1.X - v0.X;
            double ey = v1.Y - v0.Y;
            double len = Math.Sqrt((ex * ex) + (ey * ey));
            if (len < Epsilon)
            {
                return null;
            }

            // Outward normal of a CCW edge.
            double ox = ey / len;
            double oy = -ex / len;
            lines[i] = (v0.X + (ox * margin), v0.Y + (oy * margin), ex, ey);
        }

        PointF[] expanded = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            (double Px, double Py, double Dx, double Dy) prev = lines[(i + n - 1) % n];
            (double Px, double Py, double Dx, double Dy) cur = lines[i];

            double denom = (prev.Dx * cur.Dy) - (prev.Dy * cur.Dx);
            if (Math.Abs(denom) < Epsilon)
            {
                return null;
            }

            double t = (((cur.Px - prev.Px) * cur.Dy) - ((cur.Py - prev.Py) * cur.Dx)) / denom;
            expanded[i] = new PointF(prev.Px + (t * prev.Dx), prev.Py + (t * prev.Dy));
        }

        return expanded;
    }

    private static PointF Lerp(PointF a, PointF b, double t)
    {
        return new PointF(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
    }

    // ── Redaction quads in user space ──────────────────────────────────────

    // Maps every redaction rectangle (page device space) into the current user
    // space as a convex CCW quad. Returns null when any matrix is singular or
    // any quad degenerates — the caller then falls back to the whole-path drop.
    private static List<PointF[]>? BuildUserSpaceQuads(
        RedactState state, IReadOnlyList<RedactContext> contexts)
    {
        Transform ctmInverse;
        try
        {
            ctmInverse = state.Ctm.Invert();
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        List<PointF[]> quads = new List<PointF[]>();
        for (int c = 0; c < contexts.Count; c++)
        {
            RedactContext context = contexts[c];
            if (context.Rects.Count == 0)
            {
                continue;
            }

            Transform baseInverse;
            try
            {
                baseInverse = context.BaseCtm.Invert();
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            foreach (RectangleF r in context.Rects)
            {
                PointF[] quad = new PointF[4];
                quad[0] = ToUser(r.X, r.Y, baseInverse, ctmInverse);
                quad[1] = ToUser(r.X + r.Width, r.Y, baseInverse, ctmInverse);
                quad[2] = ToUser(r.X + r.Width, r.Y + r.Height, baseInverse, ctmInverse);
                quad[3] = ToUser(r.X, r.Y + r.Height, baseInverse, ctmInverse);

                double area2 = SignedArea2(quad);
                if (Math.Abs(area2) < Epsilon)
                {
                    return null;
                }

                if (area2 < 0)
                {
                    Array.Reverse(quad);
                }

                quads.Add(quad);
            }
        }

        return quads;
    }

    private static PointF ToUser(
        double deviceX, double deviceY, Transform baseInverse, Transform ctmInverse)
    {
        return ctmInverse.TransformPoint(baseInverse.TransformPoint(new PointF(deviceX, deviceY)));
    }

    private static double SignedArea2(PointF[] polygon)
    {
        double sum = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            PointF v0 = polygon[i];
            PointF v1 = polygon[(i + 1) % polygon.Length];
            sum += (v0.X * v1.Y) - (v1.X * v0.Y);
        }

        return sum;
    }

    // ── Subpath model + parsing ────────────────────────────────────────────

    private readonly struct PathSegment
    {
        public PathSegment(PointF p0, PointF p1)
        {
            P0 = p0;
            P1 = p1;
            C1 = p0;
            C2 = p1;
            IsCurve = false;
        }

        public PathSegment(PointF p0, PointF c1, PointF c2, PointF p1)
        {
            P0 = p0;
            P1 = p1;
            C1 = c1;
            C2 = c2;
            IsCurve = true;
        }

        public PointF P0 { get; }

        public PointF P1 { get; }

        public PointF C1 { get; }

        public PointF C2 { get; }

        public bool IsCurve { get; }
    }

    private sealed class Subpath
    {
        public PointF Start { get; set; }

        public PointF Current { get; set; }

        public List<PathSegment> Segments { get; } = new List<PathSegment>();

        public List<string> RawOps { get; } = new List<string>();

        public bool Closed { get; private set; }

        public bool HasCurves { get; set; }

        public bool IsRect { get; set; }

        public double RectX { get; set; }

        public double RectY { get; set; }

        public double RectW { get; set; }

        public double RectH { get; set; }

        // True when the subpath was closed by the paint operator (s / b / b*)
        // rather than an explicit h, so a verbatim re-emit under plain S needs
        // an explicit h appended.
        public bool NeedsExplicitClose { get; set; }

        public double MinX { get; private set; } = double.MaxValue;

        public double MinY { get; private set; } = double.MaxValue;

        public double MaxX { get; private set; } = double.MinValue;

        public double MaxY { get; private set; } = double.MinValue;

        public void Begin(PointF start)
        {
            Start = start;
            Current = start;
            Grow(start);
        }

        public void AddLine(PointF to)
        {
            Segments.Add(new PathSegment(Current, to));
            Current = to;
            Grow(to);
        }

        public void AddCurve(PointF c1, PointF c2, PointF to)
        {
            Segments.Add(new PathSegment(Current, c1, c2, to));
            HasCurves = true;
            Current = to;
            Grow(c1);
            Grow(c2);
            Grow(to);
        }

        public void Close()
        {
            if (Closed)
            {
                return;
            }

            if (Current.DistanceTo(Start) > Epsilon)
            {
                Segments.Add(new PathSegment(Current, Start));
            }

            Current = Start;
            Closed = true;
        }

        private void Grow(PointF p)
        {
            MinX = Math.Min(MinX, p.X);
            MinY = Math.Min(MinY, p.Y);
            MaxX = Math.Max(MaxX, p.X);
            MaxY = Math.Max(MaxY, p.Y);
        }
    }

    // Parses the buffered construction operators into subpaths with user-space
    // coordinates. Returns null for anything unexpected (missing or
    // non-numeric operands, a drawing operator with no current point) — the
    // caller then falls back to the whole-path drop.
    private static List<Subpath>? ParseSubpaths(
        List<(List<PdfToken> Operands, string Op)> buffer)
    {
        List<Subpath> subpaths = new List<Subpath>();
        Subpath? current = null;

        foreach ((List<PdfToken> operands, string op) in buffer)
        {
            switch (op)
            {
                case "m":
                    if (!TryNumbers(operands, 2, out double[] mv))
                    {
                        return null;
                    }

                    current = new Subpath();
                    current.Begin(new PointF(mv[0], mv[1]));
                    current.RawOps.Add(RawLine(operands, op));
                    subpaths.Add(current);
                    break;

                case "l":
                    if (current is null || !TryNumbers(operands, 2, out double[] lv))
                    {
                        return null;
                    }

                    current.AddLine(new PointF(lv[0], lv[1]));
                    current.RawOps.Add(RawLine(operands, op));
                    break;

                case "c":
                    if (current is null || !TryNumbers(operands, 6, out double[] cv))
                    {
                        return null;
                    }

                    current.AddCurve(
                        new PointF(cv[0], cv[1]), new PointF(cv[2], cv[3]), new PointF(cv[4], cv[5]));
                    current.RawOps.Add(RawLine(operands, op));
                    break;

                case "v":
                    if (current is null || !TryNumbers(operands, 4, out double[] vv))
                    {
                        return null;
                    }

                    current.AddCurve(
                        current.Current, new PointF(vv[0], vv[1]), new PointF(vv[2], vv[3]));
                    current.RawOps.Add(RawLine(operands, op));
                    break;

                case "y":
                    if (current is null || !TryNumbers(operands, 4, out double[] yv))
                    {
                        return null;
                    }

                    current.AddCurve(
                        new PointF(yv[0], yv[1]), new PointF(yv[2], yv[3]), new PointF(yv[2], yv[3]));
                    current.RawOps.Add(RawLine(operands, op));
                    break;

                case "re":
                    if (!TryNumbers(operands, 4, out double[] rv))
                    {
                        return null;
                    }

                    current = new Subpath();
                    current.Begin(new PointF(rv[0], rv[1]));
                    current.AddLine(new PointF(rv[0] + rv[2], rv[1]));
                    current.AddLine(new PointF(rv[0] + rv[2], rv[1] + rv[3]));
                    current.AddLine(new PointF(rv[0], rv[1] + rv[3]));
                    current.Close();
                    current.IsRect = true;
                    current.RectX = rv[0];
                    current.RectY = rv[1];
                    current.RectW = rv[2];
                    current.RectH = rv[3];
                    current.RawOps.Add(RawLine(operands, op));
                    subpaths.Add(current);
                    break;

                case "h":
                    if (current is null)
                    {
                        return null;
                    }

                    current.Close();
                    current.RawOps.Add(RawLine(operands, op));
                    break;

                default:
                    // Anything unexpected in the buffer (defensive — the caller
                    // only buffers construction operators here).
                    return null;
            }
        }

        return subpaths;
    }

    // Reads the LAST count operands (stack semantics — the ones a viewer
    // consumes). Crafted EXTRA operands make verbatim re-emission unsafe (the
    // extras would be re-parsed as geometry by the viewer), so any surplus
    // fails the parse and the caller falls back to the whole-path drop.
    private static bool TryNumbers(List<PdfToken> operands, int count, out double[] values)
    {
        values = new double[count];
        if (operands.Count != count)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (!double.TryParse(
                operands[i].RawText, NumberStyles.Float, CultureInfo.InvariantCulture,
                out values[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string RawLine(List<PdfToken> operands, string op)
    {
        StringBuilder sb = new StringBuilder();
        foreach (PdfToken t in operands)
        {
            sb.Append(t.RawText).Append(' ');
        }

        sb.Append(op);
        return sb.ToString();
    }

    private static string Num(double v)
    {
        return v.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
