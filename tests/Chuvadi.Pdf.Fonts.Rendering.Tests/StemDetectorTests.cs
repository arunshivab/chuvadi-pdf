// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FreeType autohinter — latin segment and stem detection
// PHASE: Phase 2 — Autohinting (Component 1: stem detection) tests

using System.Collections.Generic;
using Chuvadi.Pdf.Fonts.Rendering.Hinting;
using Chuvadi.Pdf.Fonts.Rendering.Hinting.Autohint;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class StemDetectorTests
{
    private static readonly byte[] NoInstructions = [];

    // Builds a glyph from contour rectangles, appending four dummy phantom
    // points exactly as the loader does (RealPointCount excludes them).
    private static RawGlyph Glyph(IReadOnlyList<int[]> contours)
    {
        List<int> xs = new List<int>();
        List<int> ys = new List<int>();
        List<bool> onCurve = new List<bool>();
        List<int> ends = new List<int>();

        foreach (int[] rect in contours)
        {
            // rect = [xMin, xMax, yMin, yMax]; emit a 4-corner closed rectangle
            // wound counter-clockwise (bottom, right, top, left).
            int xMin = rect[0];
            int xMax = rect[1];
            int yMin = rect[2];
            int yMax = rect[3];

            AddPoint(xs, ys, onCurve, xMin, yMin);
            AddPoint(xs, ys, onCurve, xMax, yMin);
            AddPoint(xs, ys, onCurve, xMax, yMax);
            AddPoint(xs, ys, onCurve, xMin, yMax);
            ends.Add(xs.Count - 1);
        }

        int realCount = xs.Count;

        // Four phantom points (values irrelevant to the detector).
        for (int i = 0; i < 4; i++)
        {
            AddPoint(xs, ys, onCurve, 0, 0);
        }

        return new RawGlyph(
            xs.ToArray(),
            ys.ToArray(),
            onCurve.ToArray(),
            ends.ToArray(),
            NoInstructions,
            realCount);
    }

    private static void AddPoint(List<int> xs, List<int> ys, List<bool> onCurve, int x, int y)
    {
        xs.Add(x);
        ys.Add(y);
        onCurve.Add(true);
    }

    [Fact]
    public void SingleBar_DetectsOneStem()
    {
        // A vertical bar x in [100, 200], full height.
        RawGlyph glyph = Glyph(new int[][] { [100, 200, 0, 700] });

        IReadOnlyList<Stem> stems = StemDetector.DetectVerticalStems(glyph);

        stems.Should().HaveCount(1);
        stems[0].MinX.Should().Be(100);
        stems[0].MaxX.Should().Be(200);
        stems[0].Width.Should().Be(100);
    }

    [Fact]
    public void TwoSeparateBars_DetectTwoStems()
    {
        // Two vertical bars, like the stems of an "H": x in [100,200] and [400,500].
        RawGlyph glyph = Glyph(new int[][] { [100, 200, 0, 700], [400, 500, 0, 700] });

        IReadOnlyList<Stem> stems = StemDetector.DetectVerticalStems(glyph);

        stems.Should().HaveCount(2);
        stems[0].CenterX.Should().Be(150);    // left stem first (sorted by centre)
        stems[1].CenterX.Should().Be(450);
    }

    [Fact]
    public void AllHorizontalEdges_DetectNoStems()
    {
        // A short, wide bar: every long edge is horizontal, no vertical flank
        // clears the height threshold.
        RawGlyph glyph = Glyph(new int[][] { [0, 700, 100, 160] });

        IReadOnlyList<Stem> stems = StemDetector.DetectVerticalStems(glyph);

        stems.Should().BeEmpty();
    }

    [Fact]
    public void EmptyGlyph_DetectsNoStems()
    {
        RawGlyph glyph = new RawGlyph(
            [0, 0, 0, 0],
            [0, 0, 0, 0],
            [true, true, true, true],
            [],
            NoInstructions,
            0);

        IReadOnlyList<Stem> stems = StemDetector.DetectVerticalStems(glyph);

        stems.Should().BeEmpty();
    }

    [Fact]
    public void WideBar_StemWidthMatchesEdges()
    {
        // Centre and width are reported in font units from the two flanks.
        RawGlyph glyph = Glyph(new int[][] { [220, 320, 0, 1400] });

        IReadOnlyList<Stem> stems = StemDetector.DetectVerticalStems(glyph);

        stems.Should().HaveCount(1);
        stems[0].CenterX.Should().Be(270);
        stems[0].Width.Should().Be(100);
        stems[0].Height.Should().Be(1400);
    }
}
