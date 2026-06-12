// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.6 — CCITTFaxDecode; ITU-T T.4 / T.6
// PHASE: Phase 2.9 — Reader feature batch (scanned-document support) tests
//
// Reference data: the Fixtures/Ccitt strips were produced by an independent
// implementation (Pillow/libtiff) encoding known pixel patterns as Group 3
// and Group 4 TIFF strips. Pillow's fax writer emits its samples with
// inverted run polarity (compensated by PhotometricInterpretation inside
// TIFF), so the .expected files pack the inverse image under the PDF default
// (BlackIs1 = false: black = 0 bits). Absolute polarity is pinned separately
// by HandBuiltVector_PinsStandardPolarity.

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

public sealed class CcittFaxFilterTests
{
    private static readonly string FixtureRoot =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ccitt");

    private static byte[] DecodeStrip(string file, int columns, int rows, int k, bool blackIs1 = false)
    {
        byte[] encoded = File.ReadAllBytes(Path.Combine(FixtureRoot, file));
        CcittFaxFilter filter = new();
        FilterParameters parms = new()
        {
            Columns = columns,
            ColumnsSpecified = true,
            Rows = rows,
            CcittK = k,
            BlackIs1 = blackIs1,
        };

        using MemoryStream input = new(encoded);
        using MemoryStream output = new();
        filter.Decode(input, output, parms);
        return output.ToArray();
    }

    private static byte[] Expected(string pattern)
        => File.ReadAllBytes(Path.Combine(FixtureRoot, pattern + ".expected"));

    [Theory]
    [InlineData("bar_64x16", 64, 16)]
    [InlineData("checker_80x40", 80, 40)]
    [InlineData("diag_100x100", 100, 100)]
    [InlineData("noise_200x50", 200, 50)]
    [InlineData("lines_45x10", 45, 10)]
    public void Group4_MatchesIndependentReference(string pattern, int columns, int rows)
    {
        byte[] decoded = DecodeStrip($"{pattern}_group4.bin", columns, rows, k: -1);
        decoded.Should().Equal(Expected(pattern));
    }

    [Theory]
    [InlineData("bar_64x16", 64, 16)]
    [InlineData("checker_80x40", 80, 40)]
    [InlineData("diag_100x100", 100, 100)]
    [InlineData("noise_200x50", 200, 50)]
    [InlineData("lines_45x10", 45, 10)]
    public void Group3OneDimensional_MatchesIndependentReference(string pattern, int columns, int rows)
    {
        byte[] decoded = DecodeStrip($"{pattern}_group3.bin", columns, rows, k: 0);
        decoded.Should().Equal(Expected(pattern));
    }

    [Fact]
    public void HandBuiltVector_PinsStandardPolarity()
    {
        // One 1-D row, 64 columns: white 20, black 24, white 20, encoded by
        // hand from the published T.4 tables (white 20 = 0001000,
        // black 24 = 00000010111). This pins absolute polarity to the fax
        // standard independently of the Pillow fixtures, whose writer
        // inverts (see the fixture generator note).
        byte[] encoded = [0x10, 0x05, 0xC4, 0x00];
        CcittFaxFilter filter = new();
        FilterParameters parms = new()
        {
            Columns = 64,
            ColumnsSpecified = true,
            Rows = 1,
            CcittK = 0,
        };

        using MemoryStream input = new(encoded);
        using MemoryStream output = new();
        filter.Decode(input, output, parms);

        // PDF default BlackIs1 = false: white = 1 bits, black = 0 bits.
        output.ToArray().Should().Equal(
            0xFF, 0xFF, 0xF0, 0x00, 0x00, 0x0F, 0xFF, 0xFF);
    }

    [Fact]
    public void BlackIs1_InvertsThePackedBits()
    {
        byte[] normal = DecodeStrip("bar_64x16_group4.bin", 64, 16, k: -1, blackIs1: false);
        byte[] inverted = DecodeStrip("bar_64x16_group4.bin", 64, 16, k: -1, blackIs1: true);

        inverted.Length.Should().Be(normal.Length);
        for (int i = 0; i < normal.Length; i++)
        {
            inverted[i].Should().Be((byte)~normal[i]);
        }
    }

    [Fact]
    public void RowsZero_DecodesUntilDataEnds()
    {
        // Same strip without a Rows hint: the decoder reads rows until the
        // coding data is exhausted.
        byte[] decoded = DecodeStrip("bar_64x16_group4.bin", 64, rows: 0, k: -1);
        decoded.Should().Equal(Expected("bar_64x16"));
    }

    [Fact]
    public void GarbageData_Throws()
    {
        CcittFaxFilter filter = new();
        byte[] junk = [0xAB, 0xCD, 0xEF, 0x01, 0x23];
        using MemoryStream input = new(junk);
        using MemoryStream output = new();
        FilterParameters parms = new()
        {
            Columns = 64,
            ColumnsSpecified = true,
            CcittK = -1,
        };

        Action act = () => filter.Decode(input, output, parms);
        act.Should().Throw<FilterException>();
    }

    [Fact]
    public void Encode_IsNotSupported()
    {
        CcittFaxFilter filter = new();
        using MemoryStream input = new();
        using MemoryStream output = new();
        Action act = () => filter.Encode(input, output);
        act.Should().Throw<FilterException>();
    }

    [Fact]
    public void Registry_ResolvesFilterAndAlias()
    {
        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        byte[] encoded = File.ReadAllBytes(Path.Combine(FixtureRoot, "bar_64x16_group4.bin"));
        FilterParameters parms = new()
        {
            Columns = 64,
            ColumnsSpecified = true,
            Rows = 16,
            CcittK = -1,
        };

        byte[] decoded = pipeline.Decode("CCITTFaxDecode", encoded, parms);
        decoded.Should().Equal(Expected("bar_64x16"));

        FilterRegistry.ResolveAlias("CCF").Should().Be("CCITTFaxDecode");
    }
}
