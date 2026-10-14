// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for v3.20: the autohinter's X-axis vertical-stem fitting (full
// hinting only).

using System;
using Chuvadi.Pdf.Fonts.Rendering.Hinting;
using Chuvadi.Pdf.Fonts.Rendering.Hinting.Autohint;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class AutohinterXFitTests
{
    private const int UnitsPerEm = 2048;
    private static readonly byte[] NoInstructions = [];

    // The two stems of an "H" (no crossbar): stems 0–190 and 610–800.
    private static RawGlyph TwoStems()
    {
        int[] xs = [0, 190, 190, 0, 610, 800, 800, 610, 0, 0, 0, 0];
        int[] ys = [0, 0, 1400, 1400, 0, 0, 1400, 1400, 0, 0, 0, 0];
        bool[] onCurve = new bool[12];
        Array.Fill(onCurve, true);
        return new RawGlyph(xs, ys, onCurve, [3, 7], NoInstructions, 8);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(17)]
    [InlineData(24)]
    public void StemEdges_LandOnWholePixels_WithWholePixelWidths(int ppem)
    {
        double scale = (double)ppem / UnitsPerEm;

        double[] x = Autohinter.FitX(TwoStems(), scale);

        foreach (int i in new[] { 0, 1, 4, 5 })
        {
            x[i].Should().BeApproximately(Math.Round(x[i]), 1e-9, "stem edge {0} is grid-fitted", i);
        }

        (x[1] - x[0]).Should().BeGreaterThanOrEqualTo(1);
        (x[5] - x[4]).Should().Be(x[1] - x[0], "equal design stems fit to equal pixel widths");
        Math.Abs((x[1] - x[0]) - (190 * scale)).Should().BeLessThanOrEqualTo(0.5 + 1e-9);
    }

    [Fact]
    public void HairlineStem_IsWidenedToOnePixel()
    {
        double scale = 8.0 / UnitsPerEm; // 190 units ≈ 0.74 px

        double[] x = Autohinter.FitX(TwoStems(), scale);

        (x[1] - x[0]).Should().Be(1);
    }

    [Fact]
    public void Mapping_IsMonotone()
    {
        double scale = 9.0 / UnitsPerEm;
        RawGlyph glyph = TwoStems();

        double[] x = Autohinter.FitX(glyph, scale);

        x[0].Should().BeLessThanOrEqualTo(x[1]);
        x[1].Should().BeLessThanOrEqualTo(x[4]);
        x[4].Should().BeLessThanOrEqualTo(x[5]);
    }

    [Fact]
    public void GlyphWithoutStems_IsLeftNaturallyScaled()
    {
        // A wide flat bar: no vertical stem pairs.
        int[] xs = [0, 1800, 1800, 0, 0, 0, 0, 0];
        int[] ys = [0, 0, 150, 150, 0, 0, 0, 0];
        bool[] onCurve = [true, true, true, true, true, true, true, true];
        RawGlyph glyph = new(xs, ys, onCurve, [3], NoInstructions, 4);
        double scale = 12.0 / UnitsPerEm;

        double[] x = Autohinter.FitX(glyph, scale);

        x[1].Should().BeApproximately(1800 * scale, 1e-9);
    }
}
