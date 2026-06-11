// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FreeType autohinter — latin Y-direction grid fitting
// PHASE: Phase 2.7 — Autohinting (Components 3–5: Y fitting fallback) tests

using System.Collections.Generic;
using Chuvadi.Pdf.Fonts.Rendering.Hinting;
using Chuvadi.Pdf.Fonts.Rendering.Hinting.Autohint;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class AutohinterTests
{
    private const int UnitsPerEm = 2048;
    private static readonly byte[] NoInstructions = [];

    // A flat-edged rectangle glyph: 4 contour points + 4 phantoms.
    private static RawGlyph Rectangle(int xMin, int xMax, int yMin, int yMax)
    {
        int[] xs = [xMin, xMax, xMax, xMin, 0, 0, 0, 0];
        int[] ys = [yMin, yMin, yMax, yMax, 0, 0, 0, 0];
        bool[] onCurve = [true, true, true, true, true, true, true, true];
        int[] ends = [3];
        return new RawGlyph(xs, ys, onCurve, ends, NoInstructions, 4);
    }

    private static BlueZoneTable EmptyZones()
        => BlueZoneBuilder.Build(new List<RawGlyph>(), UnitsPerEm);

    // ── HorizontalEdgeDetector ────────────────────────────────────────────

    [Fact]
    public void Detect_Rectangle_FindsFloorAndCeiling()
    {
        RawGlyph glyph = Rectangle(0, 800, 0, 1400);

        List<HorizontalEdge> edges = HorizontalEdgeDetector.Detect(glyph, UnitsPerEm);

        edges.Should().HaveCount(2);
        edges[0].Y.Should().BeApproximately(0, 0.001);
        edges[0].IsFloor.Should().BeTrue("the bottom run goes left-to-right with ink above");
        edges[1].Y.Should().BeApproximately(1400, 0.001);
        edges[1].IsFloor.Should().BeFalse("the top run goes right-to-left with ink below");
    }

    [Fact]
    public void Detect_TinyRuns_AreRejected()
    {
        // 40 font units wide at 2048 upem is below the minimum run extent.
        RawGlyph glyph = Rectangle(0, 40, 0, 1400);

        List<HorizontalEdge> edges = HorizontalEdgeDetector.Detect(glyph, UnitsPerEm);

        edges.Should().BeEmpty();
    }

    [Fact]
    public void Detect_TwoContoursAtSameY_MergeIntoOneEdge()
    {
        // Two stems of an "H": both feet on the baseline, both tops at cap height.
        int[] xs = [0, 200, 200, 0, 600, 800, 800, 600, 0, 0, 0, 0];
        int[] ys = [0, 0, 1400, 1400, 0, 0, 1400, 1400, 0, 0, 0, 0];
        bool[] onCurve = new bool[12];
        for (int i = 0; i < 12; i++)
        {
            onCurve[i] = true;
        }
        int[] ends = [3, 7];
        RawGlyph glyph = new(xs, ys, onCurve, ends, NoInstructions, 8);

        List<HorizontalEdge> edges = HorizontalEdgeDetector.Detect(glyph, UnitsPerEm);

        edges.Should().HaveCount(2, "the two feet merge and the two tops merge");
        edges[0].PointIndices.Should().Contain(new[] { 0, 1, 4, 5 });
        edges[1].PointIndices.Should().Contain(new[] { 2, 3, 6, 7 });
    }

    // ── Autohinter.FitY ───────────────────────────────────────────────────

    [Fact]
    public void FitY_NoEdges_ReturnsNaturalScale()
    {
        RawGlyph glyph = Rectangle(0, 40, 0, 1400);   // too narrow for edges
        double scale = 17.0 / UnitsPerEm;

        double[] fitted = Autohinter.FitY(glyph, EmptyZones(), scale, UnitsPerEm);

        fitted[0].Should().BeApproximately(0, 1e-9);
        fitted[2].Should().BeApproximately(1400 * scale, 1e-9);
    }

    [Fact]
    public void FitY_UnpairedEdges_RoundToGrid()
    {
        // Tall rectangle: gap 1400 units exceeds the stroke cap, so the two
        // edges fit independently to the grid.
        RawGlyph glyph = Rectangle(0, 800, 100, 1500);
        double scale = 17.0 / UnitsPerEm;

        double[] fitted = Autohinter.FitY(glyph, EmptyZones(), scale, UnitsPerEm);

        // 100 × scale = 0.830 → 1.0; 1500 × scale = 12.451 → 12.0.
        fitted[0].Should().BeApproximately(1.0, 1e-9);
        fitted[1].Should().BeApproximately(1.0, 1e-9);
        fitted[2].Should().BeApproximately(12.0, 1e-9);
        fitted[3].Should().BeApproximately(12.0, 1e-9);
    }

    [Fact]
    public void FitY_StrokePair_FitsWidthToWholePixels()
    {
        // Crossbar: 300 units thick — within the stroke cap, so the pair
        // fits as floor + whole-pixel width.
        RawGlyph glyph = Rectangle(0, 800, 700, 1000);
        double scale = 17.0 / UnitsPerEm;

        double[] fitted = Autohinter.FitY(glyph, EmptyZones(), scale, UnitsPerEm);

        // floor: 700 × scale = 5.811 → 6; width: 300 × scale = 2.490 → 2.
        fitted[0].Should().BeApproximately(6.0, 1e-9);
        fitted[2].Should().BeApproximately(8.0, 1e-9);
        (fitted[2] - fitted[0]).Should().BeApproximately(2.0, 1e-9, "stroke weight fits to whole pixels");
    }

    [Fact]
    public void FitY_BlueZone_CollapsesOvershootAtSmallSizes()
    {
        // Zones built from a flat cap-height reference and a round overshoot.
        List<RawGlyph> refs = new()
        {
            Rectangle(100, 900, 0, 1400),
            RoundGlyph(500, -20, 1430),
        };
        BlueZoneTable zones = BlueZoneBuilder.Build(refs, UnitsPerEm);
        zones.Count.Should().BeGreaterThan(0);

        // Glyph whose top overshoots cap height; at 12 ppem the zone height
        // scales below ¾ px, so the overshoot collapses to the flat line.
        RawGlyph glyph = Rectangle(0, 800, 0, 1430);
        double scale = 12.0 / UnitsPerEm;

        double[] fitted = Autohinter.FitY(glyph, zones, scale, UnitsPerEm);

        double reference = System.Math.Round(1400 * scale);
        fitted[2].Should().BeApproximately(reference, 1e-9);
        fitted[3].Should().BeApproximately(reference, 1e-9);
        fitted[0].Should().BeApproximately(0, 1e-9, "the baseline anchors at zero");
    }

    [Fact]
    public void FitY_UntouchedPoints_InterpolateBetweenEdges()
    {
        // Rectangle with an extra on-curve midpoint on the right side.
        int[] xs = [0, 800, 800, 800, 0, 0, 0, 0, 0];
        int[] ys = [0, 0, 350, 700, 700, 0, 0, 0, 0];
        bool[] onCurve = [true, true, true, true, true, true, true, true, true];
        int[] ends = [4];
        RawGlyph glyph = new(xs, ys, onCurve, ends, NoInstructions, 5);

        double scale = 17.0 / UnitsPerEm;
        double[] fitted = Autohinter.FitY(glyph, EmptyZones(), scale, UnitsPerEm);

        // Bottom stays 0; top 700 × scale = 5.811 → 6. The midpoint at 350
        // sits halfway in design space, so it lands halfway between the
        // fitted edges: 3.0.
        fitted[0].Should().BeApproximately(0, 1e-9);
        fitted[3].Should().BeApproximately(6.0, 1e-9);
        fitted[2].Should().BeApproximately(3.0, 1e-9);
    }

    private static RawGlyph RoundGlyph(int centerX, int yBottom, int yTop)
    {
        int mid = (yBottom + yTop) / 2;
        int[] xs = [centerX, centerX + 100, centerX, centerX - 100, 0, 0, 0, 0];
        int[] ys = [yBottom, mid, yTop, mid, 0, 0, 0, 0];
        bool[] onCurve = [true, true, true, true, true, true, true, true];
        int[] ends = [3];
        return new RawGlyph(xs, ys, onCurve, ends, NoInstructions, 4);
    }
}
