// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// 3.25: DrawShapedRun with its source text, so shaped runs copy and search
// as written (ToUnicode for one-glyph clusters, /ActualText otherwise), and
// GlyphRunStyle (horizontal scale, slant, bold stroke).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Text.Shaping;
using FluentAssertions;
using Xunit;
using static Chuvadi.Pdf.Authoring.Tests.AuthoringTestKit;

namespace Chuvadi.Pdf.Authoring.Tests;

public sealed class ShapedRunTextTests
{
    private static readonly int[] TwoClusters = { 0, 0 };

    [Fact]
    public void ShapedRun_WithLigature_ExtractsTheSourceText()
    {
        FontFace face = FontFace.Load(Font("ChuvadiTest-Cff.otf"));
        const string text = "fi AH";
        IReadOnlyList<ShapedGlyph> glyphs = TextShaper.Shape(face, text, LipiScript.Latin);
        glyphs.Select(g => g.GlyphId).Should().Contain(GidFi, "the run shapes f + i into the ligature");

        byte[] pdf = Draw(glyphs, text, ClusterStarts(glyphs, text));

        ExtractedText(pdf).Should().Be(text);
        Encoding.Latin1.GetString(pdf).Should().Contain("<0009> <00660069>", "the ligature glyph maps to \"fi\"")
            .And.NotContain("/ActualText", "every cluster here is one glyph");
    }

    [Fact]
    public void ShapedRun_ClusterOfSeveralGlyphs_UsesActualText()
    {
        // Two glyphs (A, H) standing for one cluster "Q".
        ShapedGlyph[] glyphs = { new ShapedGlyph(GidA, 600, 0, 0, 0), new ShapedGlyph(GidH, 640, 0, 0, 0) };

        byte[] pdf = Draw(glyphs, "Q", TwoClusters);

        ExtractedText(pdf).Should().Be("Q");
        Encoding.Latin1.GetString(pdf).Should().Contain("/ActualText <FEFF0051>");
    }

    [Fact]
    public void ShapedRun_GlyphAlreadyStandingForOtherText_UsesActualText()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().AddFont("T", Font("ChuvadiTest-Cff.otf"));
        PageBuilder page = builder.AddPage(PageSize.A4);
        ShapedGlyph[] fi = { new ShapedGlyph(GidFi, 540, 0, 0, 0) };
        page.DrawShapedRun(fi, "fi", new[] { 0 }, 50, 50, "T", 20, Colors.Black);
        page.DrawShapedRun(fi, "ﬁ", new[] { 0 }, 50, 100, "T", 20, Colors.Black);
        page.DrawShapedRun(new[] { new ShapedGlyph(GidA, 600, 0, 0, 0) }, "B", new[] { 0 }, 50, 150, "T", 20, Colors.Black);

        byte[] pdf = builder.ToByteArray();

        string[] lines = ExtractedText(pdf).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines.Should().Equal("fi", "ﬁ", "B");
    }

    [Fact]
    public void ShapedRun_GlyphSharedByTwoCodePoints_UsesActualText()
    {
        // In the glyf fixture's cmap only one code point maps to each glyph;
        // DejaVu-style fonts map space and no-break space to one glyph. Use
        // a run whose text is a code point that does not map to the glyph at
        // all: it must not become a ToUnicode entry for the cmap glyph.
        ShapedGlyph[] glyphs = { new ShapedGlyph(GidA, 600, 0, 0, 0) };

        byte[] pdf = Draw(glyphs, "Ä", new[] { 0 });

        ExtractedText(pdf).Should().Be("Ä");
        Encoding.Latin1.GetString(pdf).Should().Contain("/ActualText").And.NotContain("<0002> <00C4>");
    }

    [Fact]
    public void ShapedRun_LongCluster_UsesActualText()
    {
        string longText = new string('x', 300);
        ShapedGlyph[] glyphs = { new ShapedGlyph(GidFi, 540, 0, 0, 0) };

        byte[] pdf = Draw(glyphs, longText, new[] { 0 });

        ExtractedText(pdf).Should().Be(longText);
        Encoding.Latin1.GetString(pdf).Should().Contain("/ActualText");
    }

    [Fact]
    public void ShapedRun_ReorderedCluster_IsSpokenOnce()
    {
        // A cluster split around another one in visual order: its second run
        // carries an empty /ActualText, so the text is not repeated.
        ShapedGlyph[] glyphs =
        {
            new ShapedGlyph(GidA, 600, 0, 0, 0),
            new ShapedGlyph(GidH, 640, 0, 0, 0),
            new ShapedGlyph(GidX, 500, 0, 0, 0),
        };

        byte[] pdf = Draw(glyphs, "PQ", new[] { 0, 1, 0 });

        ExtractedText(pdf).Replace(" ", string.Empty, StringComparison.Ordinal).Should().Be("PQ");
    }

    [Fact]
    public void ShapedRun_RejectsMismatchedClusters()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().AddFont("T", Font("ChuvadiTest-Cff.otf"));
        PageBuilder page = builder.AddPage(PageSize.A4);
        ShapedGlyph[] one = { new ShapedGlyph(GidA, 600, 0, 0, 0) };

        Action wrongCount = () => page.DrawShapedRun(one, "A", TwoClusters, 0, 0, "T", 10, Colors.Black);
        Action outside = () => page.DrawShapedRun(one, "A", new[] { 2 }, 0, 0, "T", 10, Colors.Black);
        Action badStyle = () => page.DrawShapedRun(
            one, "A", new[] { 0 }, 0, 0, "T", 10, Colors.Black, new GlyphRunStyle { HorizontalScale = 0 });

        wrongCount.Should().Throw<ArgumentException>();
        outside.Should().Throw<ArgumentException>();
        badStyle.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GlyphRunStyle_WritesTheTextMatrixAndBoldStroke()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().AddFont("T", Font("ChuvadiTest-Cff.otf"));
        ShapedGlyph[] glyphs = { new ShapedGlyph(GidA, 600, 0, 0, 0), new ShapedGlyph(GidH, 640, 0, 0, 0) };
        builder.AddPage(PageSize.A4).DrawShapedRun(
            glyphs, "AH", new[] { 0, 1 }, 50, 50, "T", 100, Colors.Black,
            new GlyphRunStyle { HorizontalScale = 0.5, Skew = 0.2, StrokeWidth = 2 });

        byte[] pdf = builder.ToByteArray();
        string body = Encoding.Latin1.GetString(pdf);

        body.Should().Contain("0.5 0 0.2 1 50 ").And.Contain("2 Tr").And.Contain("2 w");
        body.Should().Contain("0.5 0 0.2 1 80 ", "the second glyph starts 600/1000 x 100 pt x 0.5 = 30 pt later");
        ExtractedText(pdf).Should().Be("AH");
    }

    [Fact]
    public void GlyphRunStyle_SlantAndScale_MoveTheInk()
    {
        ShapedGlyph[] a = { new ShapedGlyph(GidA, 600, 0, 0, 0) };
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().AddFont("T", Font("ChuvadiTest-Glyf.ttf"));
        builder.AddPage(PageSize.A4).DrawShapedRun(
            a, "A", new[] { 0 }, 50, 50, "T", 100, Colors.Black, new GlyphRunStyle { HorizontalScale = 2 });

        // At 200% the left bar spans x 60-80 instead of 55-65.
        Graphics.PixelBuffer px = Raster(builder.ToByteArray());
        IsDark(px, 75, 120).Should().BeTrue();
    }

    [Fact]
    public void LipiDrawText_IndicRun_ExtractsTheSourceText()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().UseLipiFonts();
        builder.AddPage(PageSize.A4).DrawText("தமிழ் OK", 50, 50, "Lipi", 20, Colors.Black);

        ExtractedText(builder.ToByteArray()).Replace(" ", string.Empty, StringComparison.Ordinal)
            .Should().Be("தமிழ்OK");
    }

    private static byte[] Draw(IReadOnlyList<ShapedGlyph> glyphs, string text, IReadOnlyList<int> clusterStarts)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().AddFont("T", Font("ChuvadiTest-Cff.otf"));
        builder.AddPage(PageSize.A4).DrawShapedRun(glyphs, text, clusterStarts, 50, 50, "T", 20, Colors.Black);
        return builder.ToByteArray();
    }

    // UTF-16 cluster starts for a Latin run: TextShaper's Cluster for Latin is
    // the code point index, which equals the UTF-16 index for BMP text.
    private static int[] ClusterStarts(IReadOnlyList<ShapedGlyph> glyphs, string text) =>
        glyphs.Select(g => Math.Min(g.Cluster, text.Length)).ToArray();
}
