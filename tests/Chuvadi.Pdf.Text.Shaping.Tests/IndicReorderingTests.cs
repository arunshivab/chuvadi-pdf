// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
//
// Indic reordering tests pinned against HarfBuzz oracle (ref-Tamil.ttf,
// LiPi Sans family). Glyph ids sourced from LiPi Sans cmap tables.
//
// Tamil HarfBuzz oracle (upem=1000, ref-Tamil.ttf):
//   கெ  U+0B95 + U+0BC6  -> gids [46, 18]   (Left: e-sign before ka)
//   கா  U+0B95 + U+0BBE  -> gids [18, 41]   (Right: aa-sign after ka)
//   கோ  U+0B95 + U+0BCB  -> gids [47, 18, 41] (L_And_R: ee-sign, ka, aa-sign)
//   கொ  U+0B95 + U+0BCA  -> gids [46, 18, 41] (L_And_R: e-sign, ka, aa-sign)
//   கௌ  U+0B95 + U+0BCC  -> gids [46, 18, 54] (L_And_R: e-sign, ka, au-mark)
//   கி  U+0B95 + U+0BBF  -> gids [18, ...]  (Right/Top: no pre-base reorder)

using System.IO;
using System.Collections.Generic;
using FluentAssertions;
using Chuvadi.Pdf.Text.Shaping.Indic;
using Chuvadi.Pdf.Text.Shaping.Unicode;
using Xunit;

namespace Chuvadi.Pdf.Text.Shaping.Tests;

public sealed class IndicReorderingTests
{
    private static readonly string FixturesDir =
        Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

    // ref-Tamil.ttf is the known-good TTF that HarfBuzz reads correctly.
    // It lives next to Carlito-Regular.ttf in the Fixtures directory.
    private static byte[] TamilFont() =>
        File.ReadAllBytes(Path.Combine(FixturesDir, "LiPi-Sans-Tamil.ttf"));

    // ── Left pre-base reorder ─────────────────────────────────────────────────

    [Fact]
    public void Shape_Tamil_LeftVowel_MovedPreBase()
    {
        // கெ: ka(0B95) + e-sign(0BC6 Left) -> e-sign(46) before ka(18)
        IReadOnlyList<ShapedGlyph> g = TextShaper.Shape(TamilFont(), "\u0B95\u0BC6", LipiScript.Tamil);

        g.Should().HaveCount(2);
        g[0].GlyphId.Should().Be(46, "e-sign should come before ka");
        g[1].GlyphId.Should().Be(18, "ka should follow the pre-base e-sign");
    }

    [Fact]
    public void Shape_Tamil_RightVowel_StaysPostBase()
    {
        // கா: ka(0B95) + aa-sign(0BBE Right) -> ka(18) then aa-sign(41)
        IReadOnlyList<ShapedGlyph> g = TextShaper.Shape(TamilFont(), "\u0B95\u0BBE", LipiScript.Tamil);

        g.Should().HaveCount(2);
        g[0].GlyphId.Should().Be(18, "ka should be first");
        g[1].GlyphId.Should().Be(41, "aa-sign should remain after ka");
    }

    // ── Left_And_Right synthesis ──────────────────────────────────────────────

    [Fact]
    public void Shape_Tamil_OVowel_SynthesisedAsLeftBaseRight()
    {
        // கோ: ka + o-sign(0BCB Left_And_Right) -> ee-sign(47) ka(18) aa-sign(41)
        IReadOnlyList<ShapedGlyph> g = TextShaper.Shape(TamilFont(), "\u0B95\u0BCB", LipiScript.Tamil);

        g.Should().HaveCount(3, "Left_And_Right should expand to three glyphs");
        g[0].GlyphId.Should().Be(47, "left part (ee-sign) should be first");
        g[1].GlyphId.Should().Be(18, "ka should be in the middle");
        g[2].GlyphId.Should().Be(41, "right part (aa-sign) should be last");
    }

    [Fact]
    public void Shape_Tamil_OShortVowel_SynthesisedCorrectly()
    {
        // கொ: ka + o-short-sign(0BCA) -> e-sign(46) ka(18) aa-sign(41)
        IReadOnlyList<ShapedGlyph> g = TextShaper.Shape(TamilFont(), "\u0B95\u0BCA", LipiScript.Tamil);

        g.Should().HaveCount(3);
        g[0].GlyphId.Should().Be(46);
        g[1].GlyphId.Should().Be(18);
        g[2].GlyphId.Should().Be(41);
    }

    [Fact]
    public void Shape_Tamil_AuVowel_SynthesisedCorrectly()
    {
        // கௌ: ka + au-sign(0BCC) -> e-sign(46) ka(18) au-length-mark(54)
        IReadOnlyList<ShapedGlyph> g = TextShaper.Shape(TamilFont(), "\u0B95\u0BCC", LipiScript.Tamil);

        g.Should().HaveCount(3);
        g[0].GlyphId.Should().Be(46);
        g[1].GlyphId.Should().Be(18);
        g[2].GlyphId.Should().Be(54);
    }

    // ── Indic categories (HarfBuzz's table: category | position << 8) ─────────

    [Theory]
    [InlineData(0x0BC6, IndicShaper.CatM, IndicShaper.PosPreM)]
    [InlineData(0x0BBE, IndicShaper.CatM, IndicShaper.PosAfterPost)]
    [InlineData(0x0BC0, IndicShaper.CatM, IndicShaper.PosAfterSub)]
    [InlineData(0x0B95, IndicShaper.CatC, IndicShaper.PosBaseC)]
    [InlineData(0x0BCD, IndicShaper.CatH, IndicShaper.PosAboveC)]
    [InlineData(0x0B85, IndicShaper.CatV, IndicShaper.PosBaseC)]
    [InlineData(0x0B82, IndicShaper.CatSM, IndicShaper.PosSmvd)]
    [InlineData(0x0041, IndicShaper.CatX, IndicShaper.PosEnd)]
    [InlineData(0x093F, IndicShaper.CatM, IndicShaper.PosPreM)]
    [InlineData(0x0930, IndicShaper.CatRa, IndicShaper.PosBaseC)]
    [InlineData(0x0D4E, IndicShaper.CatRepha, IndicShaper.PosEnd)]
    [InlineData(0x25CC, IndicShaper.CatDottedCircle, IndicShaper.PosBaseC)]
    [InlineData(0x200D, IndicShaper.CatZwj, IndicShaper.PosEnd)]
    public void IndicCategories_MatchHarfBuzz(int codepoint, int category, int position)
    {
        int value = UnicodeData.IndicCategories(codepoint);

        (value & 0xFF).Should().Be(category);
        (value >> 8).Should().Be(position);
    }
}
