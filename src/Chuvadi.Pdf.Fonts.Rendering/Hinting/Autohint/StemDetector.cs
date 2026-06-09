// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FreeType autohinter — latin segment and stem detection
// PHASE: Phase 2 — Autohinting (Component 1: stem detection)
// Finds vertical stems (opposing near-vertical edges) in a raw glyph outline.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting.Autohint;

/// <summary>
/// Detects vertical stems in a glyph outline: pairs of opposing near-vertical
/// edges that form the left and right sides of a vertical stroke (for example
/// the two sides of the left stem of an "n", or both stems of an "H").
/// </summary>
/// <remarks>
/// <para>
/// The detector is the first stage of the geometric autohinter. It operates on
/// the raw, un-cubicized <see cref="RawGlyph"/> outline in font design units
/// and produces stems that later stages snap to the pixel grid. It is
/// deliberately self-contained: it reads only the glyph geometry and has no
/// dependency on the bytecode interpreter or the render pipeline.
/// </para>
/// <para>
/// The approach follows the classic FreeType latin-autohinter shape: treat each
/// contour as a closed polyline over its on-curve points, find segments that
/// run mostly vertically, then pair segments that face in opposite directions
/// (one rising, one falling in contour order) and lie at a plausible stroke
/// width apart while overlapping in Y. Off-curve (control) points are skipped:
/// stems are defined by the straight near-vertical flanks of a stroke, and
/// using on-curve points keeps the edges well-defined without curve flattening.
/// </para>
/// </remarks>
internal static class StemDetector
{
    // A segment is "vertical" when its vertical extent dominates its horizontal
    // extent by at least this ratio. Stroke flanks are near-vertical; this
    // tolerance admits slightly italic or rounded flanks without admitting the
    // near-horizontal arches and serifs.
    private const double VerticalDominanceRatio = 2.0;

    // A segment must span at least this fraction of the glyph's overall height
    // to count as a stem flank, which rejects tiny nicks and the short vertical
    // pieces of serifs.
    private const double MinHeightFraction = 0.25;

    // Two opposing flanks pair into a stem only when their horizontal gap (the
    // candidate stroke width) is at most this fraction of the glyph's overall
    // height. A stem is tall and thin, so its thickness is naturally bounded by
    // height rather than width; bounding by width fails for narrow glyphs whose
    // single stem spans the whole advance. Pairing the wrong flanks across an
    // open counter is prevented separately by the nearest-gap-first, each-edge-
    // used-once pairing below.
    private const double MaxStemWidthHeightFraction = 0.5;

    /// <summary>
    /// Detects the vertical stems of the supplied glyph.
    /// </summary>
    /// <param name="glyph">The raw glyph outline (font units); phantom points are ignored.</param>
    /// <returns>
    /// The detected stems, ordered left to right by centre X. Empty when the
    /// glyph has no contours or no qualifying stems.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="glyph"/> is null.</exception>
    internal static IReadOnlyList<Stem> DetectVerticalStems(RawGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);

        List<Stem> stems = new List<Stem>();

        if (glyph.ContourCount == 0 || glyph.RealPointCount == 0)
        {
            return stems;
        }

        double glyphHeight = OutlineHeight(glyph);
        double glyphWidth = OutlineWidth(glyph);

        if (glyphHeight <= 0.0 || glyphWidth <= 0.0)
        {
            return stems;
        }

        double minHeight = glyphHeight * MinHeightFraction;
        double maxWidth = glyphHeight * MaxStemWidthHeightFraction;

        List<VerticalEdge> edges = CollectVerticalEdges(glyph, minHeight);
        PairEdgesIntoStems(edges, maxWidth, stems);

        stems.Sort((a, b) => a.CenterX.CompareTo(b.CenterX));
        return stems;
    }

    // Walks every contour and collects its near-vertical segments as edges,
    // tagging each with the direction it runs (up or down in contour order) so
    // opposing flanks can later be paired.
    private static List<VerticalEdge> CollectVerticalEdges(RawGlyph glyph, double minHeight)
    {
        List<VerticalEdge> edges = new List<VerticalEdge>();

        int start = 0;
        for (int contour = 0; contour < glyph.ContourCount; contour++)
        {
            int end = glyph.ContourEnds[contour];
            if (end < start)
            {
                start = end + 1;
                continue;
            }

            CollectContourEdges(glyph, start, end, minHeight, edges);
            start = end + 1;
        }

        return edges;
    }

    // Collects near-vertical edges from a single contour, walking only its
    // on-curve points in order and closing the loop from last back to first.
    private static void CollectContourEdges(
        RawGlyph glyph, int start, int end, double minHeight, List<VerticalEdge> edges)
    {
        List<int> onCurve = new List<int>();
        for (int i = start; i <= end; i++)
        {
            if (glyph.OnCurve[i])
            {
                onCurve.Add(i);
            }
        }

        if (onCurve.Count < 2)
        {
            return;
        }

        for (int k = 0; k < onCurve.Count; k++)
        {
            int a = onCurve[k];
            int b = onCurve[(k + 1) % onCurve.Count];

            double ax = glyph.X[a];
            double ay = glyph.Y[a];
            double bx = glyph.X[b];
            double by = glyph.Y[b];

            double dx = Math.Abs(bx - ax);
            double dy = Math.Abs(by - ay);

            if (dy < minHeight)
            {
                continue;
            }

            if (dy < dx * VerticalDominanceRatio)
            {
                continue;
            }

            // The flank's X is taken at its midpoint, tolerating a slight slant.
            double edgeX = (ax + bx) / 2.0;
            double lowY = Math.Min(ay, by);
            double highY = Math.Max(ay, by);

            // Contour winding gives each flank a direction; opposing flanks of a
            // stroke run opposite ways, which is how the two sides are paired.
            bool risesUp = by > ay;

            edges.Add(new VerticalEdge(edgeX, lowY, highY, risesUp));
        }
    }

    // Pairs opposing flanks that sit within a stroke width of each other and
    // overlap vertically, emitting one stem per pair. Each edge is used at most
    // once, nearest qualifying partner first.
    private static void PairEdgesIntoStems(
        List<VerticalEdge> edges, double maxWidth, List<Stem> stems)
    {
        bool[] used = new bool[edges.Count];

        for (int i = 0; i < edges.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            int bestJ = -1;
            double bestGap = double.MaxValue;

            for (int j = 0; j < edges.Count; j++)
            {
                if (j == i || used[j])
                {
                    continue;
                }

                VerticalEdge ei = edges[i];
                VerticalEdge ej = edges[j];

                // Opposing direction: the two sides of a stroke wind opposite ways.
                if (ei.RisesUp == ej.RisesUp)
                {
                    continue;
                }

                double gap = Math.Abs(ej.X - ei.X);
                if (gap <= 0.0 || gap > maxWidth)
                {
                    continue;
                }

                // The flanks must overlap in Y to be the two sides of one stroke.
                double overlap = Math.Min(ei.HighY, ej.HighY) - Math.Max(ei.LowY, ej.LowY);
                if (overlap <= 0.0)
                {
                    continue;
                }

                if (gap < bestGap)
                {
                    bestGap = gap;
                    bestJ = j;
                }
            }

            if (bestJ < 0)
            {
                continue;
            }

            used[i] = true;
            used[bestJ] = true;

            VerticalEdge left = edges[i];
            VerticalEdge right = edges[bestJ];
            double minX = Math.Min(left.X, right.X);
            double maxX = Math.Max(left.X, right.X);
            double minY = Math.Max(left.LowY, right.LowY);
            double maxY = Math.Min(left.HighY, right.HighY);

            stems.Add(new Stem(minX, maxX, minY, maxY));
        }
    }

    private static double OutlineHeight(RawGlyph glyph)
    {
        double min = double.MaxValue;
        double max = double.MinValue;
        for (int i = 0; i < glyph.RealPointCount; i++)
        {
            double y = glyph.Y[i];
            if (y < min) { min = y; }
            if (y > max) { max = y; }
        }

        return max - min;
    }

    private static double OutlineWidth(RawGlyph glyph)
    {
        double min = double.MaxValue;
        double max = double.MinValue;
        for (int i = 0; i < glyph.RealPointCount; i++)
        {
            double x = glyph.X[i];
            if (x < min) { min = x; }
            if (x > max) { max = x; }
        }

        return max - min;
    }

    // A single near-vertical flank: its X position, vertical extent, and the
    // direction it runs in contour order (used to pair opposing sides).
    private sealed class VerticalEdge
    {
        internal VerticalEdge(double x, double lowY, double highY, bool risesUp)
        {
            X = x;
            LowY = lowY;
            HighY = highY;
            RisesUp = risesUp;
        }

        internal double X { get; }

        internal double LowY { get; }

        internal double HighY { get; }

        internal bool RisesUp { get; }
    }
}
