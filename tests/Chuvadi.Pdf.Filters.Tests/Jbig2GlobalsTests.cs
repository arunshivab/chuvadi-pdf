// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.7 — JBIG2Decode; ITU-T T.88 (JBIG2).
// PHASE: Phase 2 — item 22, /JBIG2Globals plumbing.

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

public sealed class Jbig2GlobalsTests
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Jbig2");

    [Fact]
    public void Decode_WithGlobalsParameter_DecodesTextRegionImage()
    {
        byte[] globals = File.ReadAllBytes(Path.Combine(FixtureDir, "norconex_globals.bin"));
        byte[] image = File.ReadAllBytes(Path.Combine(FixtureDir, "norconex_image.bin"));
        byte[] expected = File.ReadAllBytes(Path.Combine(FixtureDir, "norconex.expected"));

        FilterParameters parms = new FilterParameters { Jbig2Globals = globals };
        Jbig2Filter filter = new Jbig2Filter();

        using MemoryStream input = new MemoryStream(image);
        using MemoryStream output = new MemoryStream();
        filter.Decode(input, output, parms);

        byte[] result = output.ToArray();

        int width = (expected[0] << 8) | expected[1];
        int rowBytes = (width + 7) / 8;
        int height = (expected.Length - 2) / rowBytes;
        result.Length.Should().Be(rowBytes * height);

        // The golden stores 1 = black; the filter emits PDF-polarity 1-bpp data
        // (0 = black), so each output byte is the complement of the golden body.
        byte[] expectedInverted = new byte[expected.Length - 2];
        for (int i = 0; i < expectedInverted.Length; i++)
        {
            expectedInverted[i] = (byte)~expected[i + 2];
        }

        result.Should().Equal(expectedInverted);
    }

    [Fact]
    public void Decode_WithoutGlobals_FailsClearlyForTextRegionImage()
    {
        byte[] image = File.ReadAllBytes(Path.Combine(FixtureDir, "norconex_image.bin"));
        Jbig2Filter filter = new Jbig2Filter();

        using MemoryStream input = new MemoryStream(image);
        using MemoryStream output = new MemoryStream();

        // With no globals the text region's referred symbol dictionary is empty, so
        // no symbol id can resolve and the filter fails clearly rather than silently.
        Action act = () => filter.Decode(input, output, null);
        act.Should().Throw<FilterException>();
    }
}
