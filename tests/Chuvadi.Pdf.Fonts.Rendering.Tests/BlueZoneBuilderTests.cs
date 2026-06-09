// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FreeType autohinter — latin blue zone construction
// PHASE: Phase 2 — Autohinting (Component 2: alignment zones) tests

using System.Collections.Generic;
using Chuvadi.Pdf.Fonts.Rendering.Hinting;
using Chuvadi.Pdf.Fonts.Rendering.Hinting.Autohint;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class BlueZoneBuilderTests
{
    private const int UnitsPerEm = 2048;
    private static readonly byte[] NoInstructions = [];

    // A flat-edged rectangle glyph: flat bottom at yMin, flat top at yMax.
    private static RawGlyph FlatGlyph(int xMin, int xMax, int yMin, int yMax)
    {
        int[] xs = [xMin, xMax, xMax, xMin, 0, 0, 0, 0];
        int[] ys = [yMin, yMin, yMax, yMax, 0, 0, 0, 0];
        bool[] onCurve = [true, true, true, true, true, true, true, true];
        int[] ends = [3];
        return new RawGlyph(xs, ys, onCurve, ends, NoInstructions, 4);
    }

    // A round glyph (diamond) whose single top/bottom extremes overshoot the
    // flat lines: bottom at yBottom, top at yTop, sides at the mid height.
    private static RawGlyph RoundGlyph(int centerX, int yBottom, int yTop)
    {
        int mid = (yBottom + yTop) / 2;
        int[] xs = [centerX, centerX + 100, centerX, centerX - 100, 0, 0, 0, 0];
        int[] ys = [yBottom, mid, yTop, mid, 0, 0, 0, 0];
        bool[] onCurve = [true, true, true, true, true, true, true, true];
        int[] ends = [3];
        return new RawGlyph(xs, ys, onCurve, ends, NoInstructions, 4);
    }

    private static BlueZone? ZoneNear(BlueZoneTable table, double position, bool isTop)
    {
        BlueZone? best = null;
        double bestDistance = double.MaxValue;
        foreach (BlueZone zone in table.Zones)
        {
            if (zone.IsTop != isTop)
            {
                continue;
            }

            double distance = System.Math.Abs(zone.Position - position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = zone;
            }
        }

        return best;
    }

    [Fact]
    public void FlatGlyphs_ProduceBaselineAndCapZones()
    {
        // H-like cap rectangle: baseline at 0, cap-height at 1400.
        List<RawGlyph> refs = new List<RawGlyph> { FlatGlyph(100, 200, 0, 1400) };

        BlueZoneTable table = BlueZoneBuilder.Build(refs, UnitsPerEm);

        BlueZone? baseline = ZoneNear(table, 0, isTop: false);
        BlueZone? cap = ZoneNear(table, 1400, isTop: true);

        baseline.Should().NotBeNull();
        baseline!.Position.Should().Be(0);
        cap.Should().NotBeNull();
        cap!.Position.Should().Be(1400);
    }

    [Fact]
    public void MixedReferences_SeparateXHeightFromCapHeight()
    {
        // x at 1000, H at 1400, o overshooting both baseline and x-height.
        List<RawGlyph> refs = new List<RawGlyph>
        {
            FlatGlyph(100, 200, 0, 1400),   // cap
            FlatGlyph(100, 200, 0, 1000),   // x-height
            RoundGlyph(150, -10, 1010),     // round, overshoots
        };

        BlueZoneTable table = BlueZoneBuilder.Build(refs, UnitsPerEm);

        BlueZone? baseline = ZoneNear(table, 0, isTop: false);
        BlueZone? xHeight = ZoneNear(table, 1000, isTop: true);
        BlueZone? cap = ZoneNear(table, 1400, isTop: true);

        baseline.Should().NotBeNull();
        baseline!.Position.Should().Be(0);

        xHeight.Should().NotBeNull();
        xHeight!.Position.Should().Be(1000);

        cap.Should().NotBeNull();
        cap!.Position.Should().Be(1400);

        // x-height and cap-height are distinct zones.
        xHeight.Position.Should().NotBe(cap.Position);
    }

    [Fact]
    public void RoundOvershoot_WidensBaselineBandBelowZero()
    {
        // The round glyph dips to -10 at the bottom; the baseline band should
        // extend to that overshoot while the flat glyphs pin the position at 0.
        List<RawGlyph> refs = new List<RawGlyph>
        {
            FlatGlyph(100, 200, 0, 1000),
            RoundGlyph(150, -10, 1010),
        };

        BlueZoneTable table = BlueZoneBuilder.Build(refs, UnitsPerEm);

        BlueZone? baseline = ZoneNear(table, 0, isTop: false);

        baseline.Should().NotBeNull();
        baseline!.Position.Should().Be(0);     // pinned by the flat glyph
        baseline.Min.Should().Be(-10);         // widened by the round overshoot
        baseline.Max.Should().Be(0);
    }

    [Fact]
    public void FindZoneFor_ReturnsContainingBand()
    {
        List<RawGlyph> refs = new List<RawGlyph>
        {
            FlatGlyph(100, 200, 0, 1000),
            RoundGlyph(150, -10, 1010),
        };
        BlueZoneTable table = BlueZoneBuilder.Build(refs, UnitsPerEm);

        // y = 1005 lies inside the x-height band [1000, 1010].
        BlueZone? zone = table.FindZoneFor(1005, tolerance: 24);

        zone.Should().NotBeNull();
        zone!.IsTop.Should().BeTrue();
        zone.Position.Should().Be(1000);
    }

    [Fact]
    public void FindZoneFor_ReturnsNullWhenFarFromEveryZone()
    {
        List<RawGlyph> refs = new List<RawGlyph>
        {
            FlatGlyph(100, 200, 0, 1000),
        };
        BlueZoneTable table = BlueZoneBuilder.Build(refs, UnitsPerEm);

        // Mid-glyph y = 500 is far from both baseline (0) and x-height (1000).
        BlueZone? zone = table.FindZoneFor(500, tolerance: 24);

        zone.Should().BeNull();
    }

    [Fact]
    public void EmptyReferenceSet_ProducesNoZones()
    {
        BlueZoneTable table = BlueZoneBuilder.Build(new List<RawGlyph>(), UnitsPerEm);

        table.Count.Should().Be(0);
    }
}
