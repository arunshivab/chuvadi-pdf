// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// 3.26: Indic shaping behaviours, with expected values from HarfBuzz 15.0.0
// on the shipped LiPi Sans fonts (glyph ids, UTF-16 clusters, font units).

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Shaping.Tests;

public sealed class IndicShapingTests
{
    [Fact]
    public void Tamil_SplitVowel_IsOneClusterAtTheConsonant()
    {
        // கொடு: KO (ka + o-sign, drawn e-sign, ka, aa-sign) then DU.
        IReadOnlyList<ShapedGlyph> g = TextShaper.Shape(TestFonts.Lipi(LipiScript.Tamil), "\u0B95\u0BCA\u0B9F\u0BC1", LipiScript.Tamil);

        g.Select(x => x.GlyphId).Should().Equal(46, 18, 41, 116);
        g.Select(x => x.Cluster).Should().Equal(0, 0, 0, 2);
    }

    [Fact]
    public void Tamil_LoneVowelSign_GetsADottedCircle()
    {
        IReadOnlyList<ShapedGlyph> g = TextShaper.Shape(TestFonts.Lipi(LipiScript.Tamil), "\u0BC6", LipiScript.Tamil);

        g.Select(x => x.GlyphId).Should().Equal(46, 243);
        g.Select(x => x.Cluster).Should().Equal(0, 0);
    }

    [Fact]
    public void Devanagari_Reph_MovesAfterTheBase()
    {
        // र्क: Ra + virama + Ka forms the reph, drawn after ka.
        IReadOnlyList<ShapedGlyph> g = TextShaper.Shape(TestFonts.Lipi(LipiScript.Devanagari), "\u0930\u094D\u0915", LipiScript.Devanagari);

        g.Select(x => x.GlyphId).Should().Equal(56, 506);
        g[1].XAdvance.Should().Be(0);
        g[1].XOffset.Should().BeApproximately(-221, 0.001);
    }

    [Fact]
    public void Devanagari_IMatra_IsReorderedBeforeTheBase()
    {
        IReadOnlyList<ShapedGlyph> g = TextShaper.Shape(TestFonts.Lipi(LipiScript.Devanagari), "\u0915\u093F", LipiScript.Devanagari);

        g.Select(x => x.GlyphId).Should().Equal(545, 56);
        g.Select(x => x.Cluster).Should().Equal(0, 0);
    }

    [Fact]
    public void Devanagari_Zwnj_BlocksTheConjunct_AndIsHidden()
    {
        // क् ZWNJ ष: explicit virama; the ZWNJ becomes the space glyph with no advance.
        IReadOnlyList<ShapedGlyph> g = TextShaper.Shape(TestFonts.Lipi(LipiScript.Devanagari), "\u0915\u094D\u200C\u0937", LipiScript.Devanagari);

        g.Select(x => x.GlyphId).Should().Equal(56, 103, 3, 86);
        g.Select(x => x.Cluster).Should().Equal(0, 0, 2, 3);
        g[2].XAdvance.Should().Be(0);
    }

    [Fact]
    public void Odia_FeatureVariations_AtTheDefaultInstance_Apply()
    {
        // The variable Odia face substitutes 'rclt' through FeatureVariations.
        IReadOnlyList<ShapedGlyph> g = TextShaper.Shape(TestFonts.Lipi(LipiScript.Odia), "\u0B15", LipiScript.Odia);

        g.Select(x => x.GlyphId).Should().Equal(871);
    }

    [Fact]
    public void Malayalam_Chillu_FormsFromViramaZwj()
    {
        IReadOnlyList<ShapedGlyph> g = TextShaper.Shape(TestFonts.Lipi(LipiScript.Malayalam), "\u0D28\u0D4D\u200D", LipiScript.Malayalam);

        g.Select(x => x.GlyphId).Should().Equal(117);
        g[0].Cluster.Should().Be(0);
    }

    [Fact]
    public void FaceAndBytes_GiveTheSameResult_ForIndic()
    {
        byte[] bytes = TestFonts.Lipi(LipiScript.Tamil).FontProgram;
        const string text = "\u0B95\u0BCA\u0B9F\u0BC1 \u0BA4\u0BAE\u0BBF\u0BB4\u0BCD";

        TextShaper.Shape(bytes, text, LipiScript.Tamil).Should().Equal(TextShaper.Shape(TestFonts.Lipi(LipiScript.Tamil), text, LipiScript.Tamil));
    }
}
