// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FreeType autohinter — latin blue zones (alignment zones)
// PHASE: Phase 2 — Autohinting (Component 2: alignment zones)
// The set of alignment zones built once per font from reference glyphs.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting.Autohint;

/// <summary>
/// The collection of horizontal alignment zones derived once per font from a
/// set of reference glyphs. Holds the baseline and the x-height / cap-height
/// lines (and any further zones the builder produces), each as a
/// <see cref="BlueZone"/> in font design units.
/// </summary>
/// <remarks>
/// The table is the font-wide reference the autohinter consults when fitting a
/// glyph: a glyph coordinate near a zone is aligned to that zone's grid-fitted
/// position rather than being rounded independently, which is what keeps
/// baselines and x-heights consistent across every glyph in the font.
/// </remarks>
internal sealed class BlueZoneTable
{
    private readonly IReadOnlyList<BlueZone> _zones;

    /// <summary>
    /// Initialises a <see cref="BlueZoneTable"/> from its zones.
    /// </summary>
    /// <param name="zones">The alignment zones (any order).</param>
    /// <exception cref="ArgumentNullException"><paramref name="zones"/> is null.</exception>
    internal BlueZoneTable(IReadOnlyList<BlueZone> zones)
    {
        _zones = zones ?? throw new ArgumentNullException(nameof(zones));
    }

    /// <summary>Gets the alignment zones in this table.</summary>
    internal IReadOnlyList<BlueZone> Zones => _zones;

    /// <summary>Gets the number of zones in this table.</summary>
    internal int Count => _zones.Count;

    /// <summary>
    /// Finds the zone whose overshoot band contains the supplied Y coordinate,
    /// or the nearest zone within <paramref name="tolerance"/> if none contains
    /// it. Returns null when no zone is close enough.
    /// </summary>
    /// <param name="y">The Y coordinate to align (font units).</param>
    /// <param name="tolerance">
    /// The maximum distance (font units) from a zone's position for that zone to
    /// be considered a match when no band contains <paramref name="y"/>.
    /// </param>
    /// <returns>The matching <see cref="BlueZone"/>, or null.</returns>
    internal BlueZone? FindZoneFor(double y, double tolerance)
    {
        BlueZone? containing = null;
        BlueZone? nearest = null;
        double nearestDistance = double.MaxValue;

        foreach (BlueZone zone in _zones)
        {
            if (zone.Contains(y))
            {
                // Prefer the band whose position is closest to y when several
                // bands overlap the coordinate.
                if (containing is null || Math.Abs(zone.Position - y) < Math.Abs(containing.Position - y))
                {
                    containing = zone;
                }
            }

            double distance = Math.Abs(zone.Position - y);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = zone;
            }
        }

        if (containing is not null)
        {
            return containing;
        }

        return nearestDistance <= tolerance ? nearest : null;
    }
}
