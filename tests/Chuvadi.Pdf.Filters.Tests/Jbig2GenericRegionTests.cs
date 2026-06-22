// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.88 (JBIG2) §6.2 — generic region.
// PHASE: Phase 2 — items 22/23.
//
// Proves the generic-region encoder and decoder are exact inverses across all four
// templates, with and without TPGDON. This validates the template/context/AT/typical
// -prediction logic end to end; bit-exact conformance to an independent JBIG2 stream
// is checked separately against a reference fixture.

using System;
using Chuvadi.Pdf.Filters.Jbig2;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

public sealed class Jbig2GenericRegionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RoundTrip_RandomBitmap_AllTemplates(int template)
    {
        Jbig2Bitmap source = RandomBitmap(40, 30, seed: 100 + template, density: 0.30);
        Jbig2Bitmap decoded = RoundTrip(source, template, GenericRegion.DefaultAt(template), tpgdon: false);
        AssertSamePixels(source, decoded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RoundTrip_WithTpgdon_AllTemplates(int template)
    {
        // A bitmap with several runs of identical rows exercises the typical-prediction
        // (copy-row-above) path; the rest exercises normal pixel coding.
        Jbig2Bitmap source = BandedBitmap(48, 36, seed: 7 + template);
        Jbig2Bitmap decoded = RoundTrip(source, template, GenericRegion.DefaultAt(template), tpgdon: true);
        AssertSamePixels(source, decoded);
    }

    [Fact]
    public void RoundTrip_AllZeroAndAllOne_AreStable()
    {
        Jbig2Bitmap zeros = new Jbig2Bitmap(20, 20);
        AssertSamePixels(zeros, RoundTrip(zeros, 0, GenericRegion.DefaultAt(0), tpgdon: true));

        Jbig2Bitmap ones = new Jbig2Bitmap(20, 20);
        for (int i = 0; i < ones.Data.Length; i++) { ones.Data[i] = 1; }
        AssertSamePixels(ones, RoundTrip(ones, 0, GenericRegion.DefaultAt(0), tpgdon: true));
    }

    [Fact]
    public void RoundTrip_ExplicitPattern_Recovers()
    {
        // A framed box with an interior diagonal — deterministic structure.
        Jbig2Bitmap source = new Jbig2Bitmap(16, 16);
        for (int x = 0; x < 16; x++) { source.Set(x, 0, 1); source.Set(x, 15, 1); }
        for (int y = 0; y < 16; y++) { source.Set(0, y, 1); source.Set(15, y, 1); source.Set(y, y, 1); }

        Jbig2Bitmap decoded = RoundTrip(source, 2, GenericRegion.DefaultAt(2), tpgdon: false);
        AssertSamePixels(source, decoded);
    }

    private static Jbig2Bitmap RoundTrip(Jbig2Bitmap source, int template, TemplatePixel[] at, bool tpgdon)
    {
        byte[] coded = GenericRegion.Encode(source, template, at, tpgdon);
        MQDecoder decoder = new MQDecoder(coded, 0, coded.Length);
        byte[] cx = new byte[GenericRegion.ContextSize(template, at)];
        return GenericRegion.Decode(decoder, cx, source.Width, source.Height, template, at, tpgdon);
    }

    private static Jbig2Bitmap RandomBitmap(int width, int height, int seed, double density)
    {
        Random rng = new Random(seed);
        Jbig2Bitmap bitmap = new Jbig2Bitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.Set(x, y, rng.NextDouble() < density ? 1 : 0);
            }
        }

        return bitmap;
    }

    private static Jbig2Bitmap BandedBitmap(int width, int height, int seed)
    {
        Random rng = new Random(seed);
        Jbig2Bitmap bitmap = new Jbig2Bitmap(width, height);
        int y = 0;
        while (y < height)
        {
            int bandHeight = 1 + rng.Next(4);
            int fill = rng.Next(3) == 0 ? 1 : 0;
            bool textured = rng.Next(2) == 0;
            for (int b = 0; b < bandHeight && y < height; b++, y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int value = textured ? (rng.NextDouble() < 0.25 ? 1 : 0) : fill;
                    bitmap.Set(x, y, value);
                }

                // Repeat the row across the band so identical-row runs occur.
                if (b > 0 && !textured)
                {
                    for (int x = 0; x < width; x++) { bitmap.Set(x, y, bitmap.Get(x, y - 1)); }
                }
            }
        }

        return bitmap;
    }

    private static void AssertSamePixels(Jbig2Bitmap expected, Jbig2Bitmap actual)
    {
        actual.Width.Should().Be(expected.Width);
        actual.Height.Should().Be(expected.Height);
        actual.Data.Should().Equal(expected.Data);
    }
}
