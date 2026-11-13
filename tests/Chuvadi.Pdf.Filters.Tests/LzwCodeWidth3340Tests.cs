// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.4 (LZWDecode, EarlyChange)
// PHASE: 3.34.0 (Phase B test bench N2) — LZW code width grows at the
//        standard point. The fixtures were written by an independent LZW
//        encoder and checked by decoding them with qpdf (test tool only).

using System.Globalization;
using System.IO;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

public sealed class LzwCodeWidth3340Tests
{
    private static readonly FilterPipeline Pipeline = FilterRegistry.CreateDefaultPipeline();

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void Decode_ReadsAStandardStreamPastNineBitCodes(int earlyChange)
    {
        byte[] encoded = File.ReadAllBytes(Fixture(earlyChange));

        byte[] decoded = Pipeline.Decode("LZWDecode", encoded, Parms(earlyChange));

        decoded.Should().Equal(Text());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void Encode_WritesTheStandardStream(int earlyChange)
    {
        byte[] encoded = Pipeline.Encode("LZWDecode", Text(), Parms(earlyChange));

        encoded.Should().Equal(File.ReadAllBytes(Fixture(earlyChange)));
    }

    private static string Fixture(int earlyChange) =>
        Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "Lzw", "early-change-" + earlyChange.ToString(CultureInfo.InvariantCulture) + ".lzw");

    private static FilterParameters Parms(int earlyChange) => new FilterParameters { EarlyChange = earlyChange };

    // 400 lines, about 24 KB: enough codes to pass 9, 10 and 11 bits.
    private static byte[] Text()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 400; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"Line {i}: the quick brown fox {i * 7} jumps over {i * 13} lazy dogs.\n");
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
