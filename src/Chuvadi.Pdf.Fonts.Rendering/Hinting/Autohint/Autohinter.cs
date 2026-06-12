// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FreeType autohinter — latin Y-direction grid fitting (blue zone
//        anchoring, stem fitting, untouched-point interpolation)
// PHASE: Phase 2.7 — Autohinting (Components 3–5: Y fitting fallback)
// Grid-fits the Y axis of glyphs that carry no TrueType instructions.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting.Autohint;

/// <summary>
/// The geometric autohinter's Y-fitting pass for unhinted glyphs: anchors
/// horizontal edges to the font's blue zones, fits horizontal stroke weights
/// to whole pixels, rounds remaining edges to the pixel grid, and
/// interpolates every other point between the fitted edges.
/// </summary>
/// <remarks>
/// <para>
/// The X axis is never touched — this matches the library's Light hinting
/// philosophy (Y-only grid fitting under grayscale anti-aliasing). The fitted
/// result feeds the same fractional-pixel path builder the bytecode
/// interpreter uses.
/// </para>
/// <para>
/// Overshoot suppression follows the classic rule: when a blue zone's
/// design-unit height scales below ¾ of a pixel, overshoot edges collapse
/// onto the zone's flat reference line; at larger sizes overshoots survive,
/// rounded to whole pixels relative to the reference.
/// </para>
/// </remarks>
internal static class Autohinter
{
    // A blue zone collapses overshoots when its scaled height drops below
    // this many pixels (FreeType's classic threshold).
    private const double ZoneCollapsePixels = 0.75;

    // An edge anchors to a blue zone when its design Y lies within the zone
    // band extended by this fraction of em.
    private const double ZoneToleranceEmFraction = 0.02;

    // Two opposing edges pair into a horizontal stroke only when their gap is
    // at most this fraction of em (no crossbar is half an em thick).
    private const double MaxStrokeEmFraction = 0.25;

    /// <summary>
    /// Computes grid-fitted device-pixel Y coordinates for the glyph's
    /// contour points. X positions are unaffected by design.
    /// </summary>
    /// <param name="glyph">The raw glyph (font units); phantom points are ignored.</param>
    /// <param name="zones">The font's blue zones (may be empty).</param>
    /// <param name="scale">Device pixels per font unit (ppem ÷ unitsPerEm).</param>
    /// <param name="unitsPerEm">The font's units-per-em, used to scale tolerances.</param>
    /// <returns>
    /// Per-point device Y values (fractional pixels, Y up), parallel to the
    /// glyph's contour points; the natural scaled Y when no edges were found.
    /// </returns>
    internal static double[] FitY(RawGlyph glyph, BlueZoneTable zones, double scale, int unitsPerEm)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        ArgumentNullException.ThrowIfNull(zones);
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be positive.");
        }
        if (unitsPerEm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitsPerEm), "Units-per-em must be positive.");
        }

        int realPoints = glyph.RealPointCount;
        double[] fitted = new double[realPoints];
        for (int i = 0; i < realPoints; i++)
        {
            fitted[i] = glyph.Y[i] * scale;
        }
        if (realPoints == 0)
        {
            return fitted;
        }

        List<HorizontalEdge> edges = HorizontalEdgeDetector.Detect(glyph, unitsPerEm);
        if (edges.Count == 0)
        {
            return fitted;
        }

        AnchorToBlueZones(edges, zones, scale, unitsPerEm);
        FitStrokePairs(edges, scale, unitsPerEm);
        RoundRemainingEdges(edges, scale);

        // Touched points: every edge point moves by its edge's delta.
        bool[] touched = new bool[realPoints];
        double[] delta = new double[realPoints];
        foreach (HorizontalEdge edge in edges)
        {
            double edgeDelta = edge.FittedY - (edge.Y * scale);
            foreach (int p in edge.PointIndices)
            {
                if (p < realPoints)
                {
                    touched[p] = true;
                    delta[p] = edgeDelta;
                    fitted[p] = (glyph.Y[p] * scale) + edgeDelta;
                }
            }
        }

        InterpolateUntouched(glyph, fitted, touched, delta, scale);
        return fitted;
    }

    // ── Fitting stages ────────────────────────────────────────────────────

    private static void AnchorToBlueZones(
        List<HorizontalEdge> edges, BlueZoneTable zones, double scale, int unitsPerEm)
    {
        if (zones.Count == 0)
        {
            return;
        }

        double tolerance = unitsPerEm * ZoneToleranceEmFraction;

        foreach (HorizontalEdge edge in edges)
        {
            BlueZone? zone = zones.FindZoneFor(edge.Y, tolerance);
            if (zone is null)
            {
                continue;
            }

            double reference = Math.Round(zone.Position * scale);
            bool collapse = zone.Height * scale < ZoneCollapsePixels;

            if (collapse)
            {
                edge.FittedY = reference;
            }
            else
            {
                // Overshoot survives, in whole pixels off the reference line.
                edge.FittedY = reference + Math.Round((edge.Y - zone.Position) * scale);
            }

            edge.IsFitted = true;
            edge.IsBlueAnchored = true;
        }
    }

    private static void FitStrokePairs(List<HorizontalEdge> edges, double scale, int unitsPerEm)
    {
        double maxStroke = unitsPerEm * MaxStrokeEmFraction;

        // Candidate pairs: a floor below a ceiling — the two boundaries of a
        // horizontal stroke. Nearest-gap-first, each edge used once.
        List<(double Gap, HorizontalEdge Floor, HorizontalEdge Ceiling)> candidates = new();
        foreach (HorizontalEdge floor in edges)
        {
            if (!floor.IsFloor)
            {
                continue;
            }
            foreach (HorizontalEdge ceiling in edges)
            {
                if (ceiling.IsFloor)
                {
                    continue;
                }
                double gap = ceiling.Y - floor.Y;
                if (gap > 0 && gap <= maxStroke)
                {
                    candidates.Add((gap, floor, ceiling));
                }
            }
        }
        candidates.Sort((a, b) => a.Gap.CompareTo(b.Gap));

        HashSet<HorizontalEdge> paired = new();
        foreach ((double gap, HorizontalEdge floor, HorizontalEdge ceiling) in candidates)
        {
            if (paired.Contains(floor) || paired.Contains(ceiling))
            {
                continue;
            }
            paired.Add(floor);
            paired.Add(ceiling);

            double widthDevice = gap * scale;
            double fittedWidth = Math.Max(1, Math.Round(widthDevice));

            if (floor.IsFitted && ceiling.IsFitted)
            {
                continue;
            }
            if (floor.IsFitted)
            {
                ceiling.FittedY = floor.FittedY + fittedWidth;
                ceiling.IsFitted = true;
            }
            else if (ceiling.IsFitted)
            {
                floor.FittedY = ceiling.FittedY - fittedWidth;
                floor.IsFitted = true;
            }
            else
            {
                floor.FittedY = Math.Round(floor.Y * scale);
                floor.IsFitted = true;
                ceiling.FittedY = floor.FittedY + fittedWidth;
                ceiling.IsFitted = true;
            }
        }
    }

    private static void RoundRemainingEdges(List<HorizontalEdge> edges, double scale)
    {
        foreach (HorizontalEdge edge in edges)
        {
            if (!edge.IsFitted)
            {
                edge.FittedY = Math.Round(edge.Y * scale);
                edge.IsFitted = true;
            }
        }
    }

    // ── Interpolation (IUP-Y analogue) ────────────────────────────────────

    private static void InterpolateUntouched(
        RawGlyph glyph, double[] fitted, bool[] touched, double[] delta, double scale)
    {
        int contourStart = 0;
        foreach (int contourEnd in glyph.ContourEnds)
        {
            InterpolateContour(glyph, fitted, touched, delta, scale, contourStart, contourEnd);
            contourStart = contourEnd + 1;
        }
    }

    private static void InterpolateContour(
        RawGlyph glyph, double[] fitted, bool[] touched, double[] delta, double scale,
        int start, int end)
    {
        int count = end - start + 1;
        if (count <= 0)
        {
            return;
        }

        // Collect touched indices in contour order.
        List<int> anchors = new();
        for (int i = start; i <= end; i++)
        {
            if (touched[i])
            {
                anchors.Add(i);
            }
        }

        if (anchors.Count == 0)
        {
            return;
        }

        if (anchors.Count == 1)
        {
            // Whole contour shifts rigidly with its single anchor.
            double d = delta[anchors[0]];
            for (int i = start; i <= end; i++)
            {
                if (!touched[i])
                {
                    fitted[i] = (glyph.Y[i] * scale) + d;
                }
            }
            return;
        }

        // Between each cyclic pair of anchors, interpolate the untouched points.
        for (int a = 0; a < anchors.Count; a++)
        {
            int t1 = anchors[a];
            int t2 = anchors[(a + 1) % anchors.Count];

            int i = Next(t1, start, end);
            while (i != t2)
            {
                InterpolatePoint(glyph, fitted, delta, scale, i, t1, t2);
                i = Next(i, start, end);
            }
        }
    }

    private static int Next(int i, int start, int end) => i == end ? start : i + 1;

    private static void InterpolatePoint(
        RawGlyph glyph, double[] fitted, double[] delta, double scale,
        int point, int t1, int t2)
    {
        double orig = glyph.Y[point];
        double orig1 = glyph.Y[t1];
        double orig2 = glyph.Y[t2];

        double lo = Math.Min(orig1, orig2);
        double hi = Math.Max(orig1, orig2);

        if (orig <= lo)
        {
            // Outside on the low side: rigid shift with the lower anchor.
            fitted[point] = (orig * scale) + (orig1 <= orig2 ? delta[t1] : delta[t2]);
            return;
        }
        if (orig >= hi)
        {
            fitted[point] = (orig * scale) + (orig1 >= orig2 ? delta[t1] : delta[t2]);
            return;
        }

        // Strictly between: linear interpolation in fitted space.
        double f1 = (orig1 * scale) + delta[t1];
        double f2 = (orig2 * scale) + delta[t2];
        double ratio = (orig - orig1) / (orig2 - orig1);
        fitted[point] = f1 + ((f2 - f1) * ratio);
    }
}
