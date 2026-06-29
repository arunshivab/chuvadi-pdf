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

using System;
using System.IO;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Shaping.Tests;

public sealed class IndicReorderingTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

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

    // ── IndicData category lookups ────────────────────────────────────────────

    [Fact]
    public void IndicData_Tamil_CorrectPositionalCategories()
    {
        Indic.IndicData.GetPositionalCategory(0x0BC6).Should().Be(
            Indic.IndicPositionalCategory.Left, "e-sign is Left");
        Indic.IndicData.GetPositionalCategory(0x0BBE).Should().Be(
            Indic.IndicPositionalCategory.Right, "aa-sign is Right");
        Indic.IndicData.GetPositionalCategory(0x0BCB).Should().Be(
            Indic.IndicPositionalCategory.LeftAndRight, "o-sign is LeftAndRight");
        Indic.IndicData.GetPositionalCategory(0x0BC0).Should().Be(
            Indic.IndicPositionalCategory.Top, "ii-sign is Top");
    }

    [Fact]
    public void IndicData_Tamil_CorrectSyllabicCategories()
    {
        Indic.IndicData.GetSyllabicCategory(0x0B95).Should().Be(
            Indic.IndicSyllabicCategory.Consonant, "ka is Consonant");
        Indic.IndicData.GetSyllabicCategory(0x0BCD).Should().Be(
            Indic.IndicSyllabicCategory.Virama, "virama is Virama");
        Indic.IndicData.GetSyllabicCategory(0x0B85).Should().Be(
            Indic.IndicSyllabicCategory.VowelIndependent, "a is VowelIndependent");
        Indic.IndicData.GetSyllabicCategory(0x0B82).Should().Be(
            Indic.IndicSyllabicCategory.Bindu, "anusvara is Bindu");
        Indic.IndicData.GetSyllabicCategory(0x0041).Should().Be(
            Indic.IndicSyllabicCategory.Other, "Latin A is Other");
    }

    [Fact]
    public void IndicData_Devanagari_LeftVowelIdentified()
    {
        // Devanagari i-sign (U+093F) is Left
        Indic.IndicData.GetPositionalCategory(0x093F).Should().Be(
            Indic.IndicPositionalCategory.Left);
    }

    [Fact]
    public void IndicData_Bengali_LeftAndRight_Identified()
    {
        // Bengali o-sign (U+09CB) is LeftAndRight
        Indic.IndicData.GetPositionalCategory(0x09CB).Should().Be(
            Indic.IndicPositionalCategory.LeftAndRight);
    }
}
