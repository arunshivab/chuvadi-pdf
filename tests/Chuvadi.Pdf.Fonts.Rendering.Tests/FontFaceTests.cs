// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for FontFace (3.25.0) and the CFF fixes that came with it, on the
// synthetic fonts from tools/make_font_fixtures.py. Every fixture has the
// same ten glyphs: 0 .notdef, 1 space, 2 A, 3 B, 4 H, 5 x, 6 f, 7 i, 8 W,
// 9 f_i (a ligature no code point maps to), each made of rectangles.

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class FontFaceTests
{
    private const int GidA = 2;
    private const int GidH = 4;
    private const int GidX = 5;
    private const int GidW = 8;
    private const int GidFi = 9;

    // Expected glyph boxes (xMin, yMin, xMax, yMax) and advances, by glyph id.
    private static readonly FontBox[] Boxes =
    {
        new FontBox(50, 0, 450, 700),
        default,
        new FontBox(50, 0, 550, 700),
        new FontBox(60, 0, 500, 700),
        new FontBox(60, 0, 580, 700),
        new FontBox(40, 0, 460, 500),
        new FontBox(60, 0, 300, 700),
        new FontBox(70, 0, 160, 690),
        new FontBox(40, 0, 1260, 700),
        new FontBox(60, 0, 470, 700),
    };

    private static readonly int[] Advances = { 500, 250, 600, 580, 640, 500, 320, 240, 1300, 540 };

    public static TheoryData<string, int> AllFaces => new TheoryData<string, int>
    {
        { "ChuvadiTest-Glyf.ttf", 0 },
        { "ChuvadiTest-Cff.otf", 0 },
        { "ChuvadiTest-Cid.otf", 0 },
        { "ChuvadiTest.ttc", 0 },
        { "ChuvadiTest.ttc", 1 },
    };

    internal static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", name));

    [Theory]
    [MemberData(nameof(AllFaces))]
    public void Metrics_MatchTheFontTables(string file, int face)
    {
        FontFace f = FontFace.Load(Fixture(file), face);

        f.UnitsPerEm.Should().Be(1000);
        f.GlyphCount.Should().Be(10);
        f.FaceIndex.Should().Be(face);
        f.FamilyName.Should().Be("Chuvadi Test");
        f.SubfamilyName.Should().Be("Bold");
        f.PostScriptName.Should().Be("ChuvadiTest-Bold");
        f.WeightClass.Should().Be(700);
        f.IsBold.Should().BeTrue();
        f.IsItalic.Should().BeFalse();

        f.HheaAscender.Should().Be(850);
        f.HheaDescender.Should().Be(-250);
        f.HheaLineGap.Should().Be(30);
        f.HasOs2Table.Should().BeTrue();
        f.TypoAscender.Should().Be(800);
        f.TypoDescender.Should().Be(-200);
        f.TypoLineGap.Should().Be(90);
        f.WinAscent.Should().Be(900);
        f.WinDescent.Should().Be(250);
        f.UseTypoMetrics.Should().BeTrue();
        f.Ascender.Should().Be(800, "USE_TYPO_METRICS selects the typo values");
        f.Descender.Should().Be(-200);
        f.LineGap.Should().Be(90);

        f.CapHeight.Should().Be(700);
        f.XHeight.Should().Be(500);
        f.UnderlinePosition.Should().Be(-120);
        f.UnderlineThickness.Should().Be(60);
        f.StrikeoutPosition.Should().Be(260);
        f.StrikeoutSize.Should().Be(50);
        f.Scale(f.CapHeight, 12).Should().BeApproximately(8.4, 1e-9);
    }

    [Theory]
    [MemberData(nameof(AllFaces))]
    public void GlyphAdvancesAndBounds_MatchTheOutlines(string file, int face)
    {
        FontFace f = FontFace.Load(Fixture(file), face);

        for (int gid = 0; gid < Advances.Length; gid++)
        {
            f.GetAdvanceWidth(gid).Should().Be(Advances[gid], "glyph {0} advance", gid);
            f.GetGlyphBounds(gid).Should().Be(Boxes[gid], "glyph {0} box", gid);
        }

        f.GetLeftSideBearing(GidA).Should().Be(50);
        f.GetGlyphBounds(GidW).Width.Should().Be(1220);
        f.GetGlyphBounds(99).Should().Be(default(FontBox));
    }

    [Theory]
    [MemberData(nameof(AllFaces))]
    public void CharacterMap_AndMeasureText(string file, int face)
    {
        FontFace f = FontFace.Load(Fixture(file), face);

        f.GetGlyphIndex('A').Should().Be(GidA);
        f.GetGlyphIndex('W').Should().Be(GidW);
        f.HasGlyph('Z').Should().BeFalse();
        f.CharacterMap.Should().ContainKey('H').WhoseValue.Should().Be(GidH);
        f.CharacterMap.Values.Should().NotContain(GidFi, "the ligature is reached only through GSUB");
        f.MeasureText("AHx").Should().Be(600 + 640 + 500);
        f.MeasureText("A\U0001F600").Should().Be(600 + 500, "an unmapped code point measures as .notdef");
    }

    [Fact]
    public void Collection_FacesAreCountedAndSelected()
    {
        byte[] ttc = Fixture("ChuvadiTest.ttc");

        FontFace.GetFaceCount(ttc).Should().Be(2);
        FontFace.GetFaceCount(Fixture("ChuvadiTest-Cff.otf")).Should().Be(1);
        FontFace.Load(ttc, 0).IsCff.Should().BeFalse();
        FontFace.Load(ttc, 1).IsCff.Should().BeTrue();

        Action outOfRange = () => FontFace.Load(ttc, 2);
        outOfRange.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Load_RejectsDataThatIsNotAFont()
    {
        Action act = () => FontFace.Load(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });

        act.Should().Throw<Exception>().Which.Should().Match<Exception>(
            e => e is FontRenderingException || e is ArgumentException);
    }

    [Fact]
    public void Bounds_AreCachedAndSafeAcrossThreads()
    {
        FontFace f = FontFace.Load(Fixture("ChuvadiTest-Cff.otf"));
        List<FontBox> seen = new List<FontBox>();
        object gate = new object();

        System.Threading.Tasks.Parallel.For(0, 64, i =>
        {
            FontBox box = f.GetGlyphBounds(GidW);
            lock (gate)
            {
                seen.Add(box);
            }
        });

        seen.Should().AllBeEquivalentTo(Boxes[GidW]);
    }

    // ── CffLoader ────────────────────────────────────────────────────────

    [Fact]
    public void CffLoader_ReadsShortintOperands()
    {
        // 'W' draws a 1220-unit-wide bar, encoded with the 3-byte shortint
        // (byte 28) operand; that byte used to be taken for an operator.
        CffLoader cff = new CffLoader(CffTable("ChuvadiTest-Cff.otf"));

        GlyphOutline w = cff.GetGlyphOutline(GidW);

        w.Metrics.AdvanceWidth.Should().Be(1300);
        w.Metrics.Bounds.X.Should().Be(40);
        w.Metrics.Bounds.Width.Should().Be(1220);
        w.Metrics.Bounds.Height.Should().Be(700);
    }

    [Fact]
    public void CffLoader_GlyphBoxStartsAtTheOutline()
    {
        // The box used to include (0, 0) from unused control points, so no
        // CFF glyph had an xMin above 0.
        CffLoader cff = new CffLoader(CffTable("ChuvadiTest-Cff.otf"));

        cff.GetGlyphOutline(GidA).Metrics.Bounds.X.Should().Be(50);
    }

    [Fact]
    public void CffLoader_CidKeyed_UsesEachGlyphsFontDictionary()
    {
        // FD 1 has its local subroutines in reverse order and another
        // nominalWidthX; a glyph read with FD 0's subrs or widths would come
        // out wrong. H and x are in FD 1, A in FD 0.
        CffLoader cid = new CffLoader(CffTable("ChuvadiTest-Cid.otf"));
        CffLoader plain = new CffLoader(CffTable("ChuvadiTest-Cff.otf"));

        cid.IsCidFont.Should().BeTrue();
        cid.CidToGid.Should().ContainKey(GidH).WhoseValue.Should().Be(GidH);
        foreach (int gid in new[] { GidA, GidH, GidX, GidW, GidFi })
        {
            GlyphOutline a = cid.GetGlyphOutline(gid);
            GlyphOutline b = plain.GetGlyphOutline(gid);
            a.Metrics.AdvanceWidth.Should().Be(Advances[gid], "glyph {0} width", gid);
            a.Metrics.Bounds.Should().Be(b.Metrics.Bounds, "glyph {0} outline", gid);
        }
    }

    private static byte[] CffTable(string file)
    {
        byte[] font = Fixture(file);
        (int offset, int length) = SfntFile.Tables(font)["CFF "];
        return font.AsSpan(offset, length).ToArray();
    }
}
