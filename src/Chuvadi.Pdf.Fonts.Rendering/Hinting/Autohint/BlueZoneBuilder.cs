// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FreeType autohinter — latin blue zone construction
// PHASE: Phase 2 — Autohinting (Component 2: alignment zones)
// Builds the per-font blue-zone table by clustering reference-glyph extremes.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting.Autohint;

/// <summary>
/// Builds a font-wide <see cref="BlueZoneTable"/> from a set of reference
/// glyphs by clustering their flat and rounded horizontal extremes into
/// baseline and top (x-height / cap-height) alignment zones.
/// </summary>
/// <remarks>
/// <para>
/// The builder is pure geometry: it consumes only <see cref="RawGlyph"/>
/// outlines (in font design units) and a units-per-em scale, and has no
/// dependency on the loader, the interpreter, or the render pipeline. The
/// caller chooses which glyphs to pass; conventionally a few flat-edged and
/// round letters (for example H, O, x, o, n) so the resulting table carries the
/// baseline plus the cap-height and x-height lines with their overshoot bands.
/// </para>
/// <para>
/// For each glyph the builder records the lowest and highest extreme of its
/// on-curve points. An extreme is a <em>flat</em> contribution when several
/// on-curve points share that Y (the flat foot of an H, the flat top of an x),
/// which defines a zone position; otherwise it is an <em>overshoot</em>
/// contribution (the single lowest point of a round O), which only widens a
/// zone's band. Contributions are then clustered by Y - separately for top and
/// bottom edges - so that, for example, lowercase tops gather into the x-height
/// zone and capital tops into the cap-height zone.
/// </para>
/// </remarks>
internal static class BlueZoneBuilder
{
    // Points within this fraction of em of an extreme count as lying on the
    // same flat edge.
    private const double FlatToleranceEmFraction = 0.012;

    // Extreme contributions within this fraction of em of each other belong to
    // the same zone. Chosen larger than overshoot (so a round overshoot groups
    // with its flat line) but far smaller than the x-height-to-cap-height gap
    // (so those remain distinct zones).
    private const double ClusterToleranceEmFraction = 0.06;

    /// <summary>
    /// Builds the blue-zone table for a font from reference glyphs.
    /// </summary>
    /// <param name="referenceGlyphs">Reference glyph outlines (font units); phantom points are ignored.</param>
    /// <param name="unitsPerEm">The font's units-per-em, used to scale clustering tolerances.</param>
    /// <returns>The constructed <see cref="BlueZoneTable"/>; empty when no usable extremes are found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="referenceGlyphs"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unitsPerEm"/> is not positive.</exception>
    internal static BlueZoneTable Build(IReadOnlyList<RawGlyph> referenceGlyphs, int unitsPerEm)
    {
        ArgumentNullException.ThrowIfNull(referenceGlyphs);
        if (unitsPerEm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitsPerEm), "Units-per-em must be positive.");
        }

        double flatTolerance = unitsPerEm * FlatToleranceEmFraction;
        double clusterTolerance = unitsPerEm * ClusterToleranceEmFraction;

        List<Contribution> bottoms = new List<Contribution>();
        List<Contribution> tops = new List<Contribution>();

        foreach (RawGlyph glyph in referenceGlyphs)
        {
            CollectExtremes(glyph, flatTolerance, bottoms, tops);
        }

        List<BlueZone> zones = new List<BlueZone>();
        ClusterIntoZones(bottoms, clusterTolerance, isTop: false, zones);
        ClusterIntoZones(tops, clusterTolerance, isTop: true, zones);

        return new BlueZoneTable(zones);
    }

    // Records one bottom and one top contribution for a glyph, classifying each
    // as flat (several on-curve points share the extreme Y) or overshoot.
    private static void CollectExtremes(
        RawGlyph glyph, double flatTolerance, List<Contribution> bottoms, List<Contribution> tops)
    {
        if (glyph is null || glyph.RealPointCount == 0)
        {
            return;
        }

        double minY = double.MaxValue;
        double maxY = double.MinValue;
        bool any = false;

        for (int i = 0; i < glyph.RealPointCount; i++)
        {
            if (!glyph.OnCurve[i])
            {
                continue;
            }

            double y = glyph.Y[i];
            if (y < minY) { minY = y; }
            if (y > maxY) { maxY = y; }
            any = true;
        }

        if (!any)
        {
            return;
        }

        int atMin = 0;
        int atMax = 0;
        for (int i = 0; i < glyph.RealPointCount; i++)
        {
            if (!glyph.OnCurve[i])
            {
                continue;
            }

            double y = glyph.Y[i];
            if (Math.Abs(y - minY) <= flatTolerance) { atMin++; }
            if (Math.Abs(y - maxY) <= flatTolerance) { atMax++; }
        }

        bottoms.Add(new Contribution(minY, isFlat: atMin >= 2));
        tops.Add(new Contribution(maxY, isFlat: atMax >= 2));
    }

    // Groups contributions whose Y values fall within the cluster tolerance into
    // zones. A zone's position is the median of its flat contributions (or the
    // mean of all contributions when it has no flat edge); its band spans all
    // contributions in the cluster.
    private static void ClusterIntoZones(
        List<Contribution> contributions, double clusterTolerance, bool isTop, List<BlueZone> zones)
    {
        if (contributions.Count == 0)
        {
            return;
        }

        contributions.Sort((a, b) => a.Y.CompareTo(b.Y));

        int start = 0;
        while (start < contributions.Count)
        {
            int end = start;
            while (end + 1 < contributions.Count
                && contributions[end + 1].Y - contributions[end].Y <= clusterTolerance)
            {
                end++;
            }

            zones.Add(BuildZone(contributions, start, end, isTop));
            start = end + 1;
        }
    }

    private static BlueZone BuildZone(List<Contribution> contributions, int start, int end, bool isTop)
    {
        double min = double.MaxValue;
        double max = double.MinValue;

        List<double> flats = new List<double>();
        for (int i = start; i <= end; i++)
        {
            double y = contributions[i].Y;
            if (y < min) { min = y; }
            if (y > max) { max = y; }
            if (contributions[i].IsFlat)
            {
                flats.Add(y);
            }
        }

        double position = flats.Count > 0 ? Median(flats) : (min + max) / 2.0;
        return new BlueZone(position, min, max, isTop);
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        int n = values.Count;
        if ((n & 1) == 1)
        {
            return values[n / 2];
        }

        return (values[(n / 2) - 1] + values[n / 2]) / 2.0;
    }

    // One glyph's extreme Y on a given side, and whether it is a flat edge.
    private readonly struct Contribution
    {
        internal Contribution(double y, bool isFlat)
        {
            Y = y;
            IsFlat = isFlat;
        }

        internal double Y { get; }

        internal bool IsFlat { get; }
    }
}
