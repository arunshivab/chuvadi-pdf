// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.10 — Extraction of text content
// PHASE: v2.0.0 R3 — Chuvadi.Pdf.Text tests for TextRun infrastructure

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Text.Tests;

// ── GlyphPosition ─────────────────────────────────────────────────────────

public sealed class GlyphPositionTests
{
    [Fact]
    public void Constructor_StoresAllFields()
    {
        GlyphPosition g = new GlyphPosition(x: 12.5, y: 100, advance: 7.2, unicode: 'A');
        g.X.Should().Be(12.5);
        g.Y.Should().Be(100);
        g.Advance.Should().Be(7.2);
        g.Unicode.Should().Be('A');
    }

    [Fact]
    public void Constructor_AcceptsAstralCodePoint()
    {
        const int Bee = 0x1F41D; // 🐝
        GlyphPosition g = new GlyphPosition(0, 0, 10, Bee);
        g.Unicode.Should().Be(Bee);
    }
}

// ── TextRun ───────────────────────────────────────────────────────────────

public sealed class TextRunTests
{
    [Fact]
    public void Constructor_NullText_Throws()
    {
        Action act = () => new TextRun(
            unicode: null!,
            boundingBox: new RectangleF(0, 0, 1, 1),
            fontSize: 12,
            direction: TextDirection.LeftToRight,
            glyphs: Array.Empty<GlyphPosition>(),
            readingOrderIndex: 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullGlyphs_Throws()
    {
        Action act = () => new TextRun(
            unicode: "x",
            boundingBox: new RectangleF(0, 0, 1, 1),
            fontSize: 12,
            direction: TextDirection.LeftToRight,
            glyphs: null!,
            readingOrderIndex: 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_StoresAllFields()
    {
        IReadOnlyList<GlyphPosition> g = new[] { new GlyphPosition(0, 0, 7, 'A') };
        TextRun r = new TextRun(
            "A",
            new RectangleF(1, 2, 3, 4),
            fontSize: 18,
            direction: TextDirection.RightToLeft,
            glyphs: g,
            readingOrderIndex: 7);

        r.Unicode.Should().Be("A");
        r.BoundingBox.Should().Be(new RectangleF(1, 2, 3, 4));
        r.FontSize.Should().Be(18);
        r.Direction.Should().Be(TextDirection.RightToLeft);
        r.Glyphs.Should().BeSameAs(g);
        r.ReadingOrderIndex.Should().Be(7);
    }
}

// ── TextRunBuilder ────────────────────────────────────────────────────────

public sealed class TextRunBuilderTests
{
    [Fact]
    public void BuildFromFragments_Null_Throws()
    {
        Action act = () => TextRunBuilder.BuildFromFragments(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildFromFragments_Empty_ReturnsEmpty()
    {
        IReadOnlyList<TextRun> runs = TextRunBuilder.BuildFromFragments(
            new List<TextFragment>());
        runs.Should().BeEmpty();
    }

    [Fact]
    public void BuildFromFragments_SingleFragment_ProducesOneRun()
    {
        List<TextFragment> fragments =
        [
            new TextFragment("Hello", 50, 100, 12),
        ];

        IReadOnlyList<TextRun> runs = TextRunBuilder.BuildFromFragments(fragments);

        runs.Should().HaveCount(1);
        runs[0].Unicode.Should().Be("Hello");
        runs[0].FontSize.Should().Be(12);
        runs[0].ReadingOrderIndex.Should().Be(0);
    }

    [Fact]
    public void BuildFromFragments_MultipleFragments_PreservesOrder()
    {
        List<TextFragment> fragments =
        [
            new TextFragment("First", 0, 100, 12),
            new TextFragment("Second", 50, 100, 12),
            new TextFragment("Third", 0, 80, 12),
        ];

        IReadOnlyList<TextRun> runs = TextRunBuilder.BuildFromFragments(fragments);

        runs.Should().HaveCount(3);
        runs[0].Unicode.Should().Be("First");
        runs[0].ReadingOrderIndex.Should().Be(0);
        runs[1].Unicode.Should().Be("Second");
        runs[1].ReadingOrderIndex.Should().Be(1);
        runs[2].Unicode.Should().Be("Third");
        runs[2].ReadingOrderIndex.Should().Be(2);
    }

    [Fact]
    public void BuildFromFragments_BoundingBox_TracksFontSizeAndLength()
    {
        // 5 glyphs at 12pt with 0.6 average-advance fraction → width 36
        List<TextFragment> fragments =
        [
            new TextFragment("Hello", 50, 100, 12),
        ];

        IReadOnlyList<TextRun> runs = TextRunBuilder.BuildFromFragments(fragments);

        runs[0].BoundingBox.Width.Should().BeApproximately(36f, precision: 0.01f);
        runs[0].BoundingBox.Height.Should().BeApproximately(12f, precision: 0.01f);
        runs[0].BoundingBox.X.Should().BeApproximately(50f, precision: 0.01f);
    }

    [Fact]
    public void BuildFromFragments_LatinText_InfersLeftToRight()
    {
        List<TextFragment> fragments =
        [
            new TextFragment("Hello World", 0, 0, 12),
        ];

        IReadOnlyList<TextRun> runs = TextRunBuilder.BuildFromFragments(fragments);
        runs[0].Direction.Should().Be(TextDirection.LeftToRight);
    }

    [Fact]
    public void BuildFromFragments_HebrewText_InfersRightToLeft()
    {
        // שלום
        List<TextFragment> fragments =
        [
            new TextFragment("\u05E9\u05DC\u05D5\u05DD", 0, 0, 12),
        ];

        IReadOnlyList<TextRun> runs = TextRunBuilder.BuildFromFragments(fragments);
        runs[0].Direction.Should().Be(TextDirection.RightToLeft);
    }

    [Fact]
    public void BuildFromFragments_ArabicText_InfersRightToLeft()
    {
        // مرحبا
        List<TextFragment> fragments =
        [
            new TextFragment("\u0645\u0631\u062D\u0628\u0627", 0, 0, 12),
        ];

        IReadOnlyList<TextRun> runs = TextRunBuilder.BuildFromFragments(fragments);
        runs[0].Direction.Should().Be(TextDirection.RightToLeft);
    }

    [Fact]
    public void BuildFromFragments_MixedScript_PicksDominantDirection()
    {
        // Mostly Latin with one Arabic letter: should stay LTR.
        List<TextFragment> fragments =
        [
            new TextFragment("Hello\u0645", 0, 0, 12),
        ];

        IReadOnlyList<TextRun> runs = TextRunBuilder.BuildFromFragments(fragments);
        runs[0].Direction.Should().Be(TextDirection.LeftToRight);
    }

    [Fact]
    public void BuildFromFragments_GlyphCount_MatchesCodePointCount()
    {
        List<TextFragment> fragments =
        [
            new TextFragment("ABC", 0, 0, 10),
        ];

        IReadOnlyList<TextRun> runs = TextRunBuilder.BuildFromFragments(fragments);
        runs[0].Glyphs.Should().HaveCount(3);
        runs[0].Glyphs[0].Unicode.Should().Be('A');
        runs[0].Glyphs[1].Unicode.Should().Be('B');
        runs[0].Glyphs[2].Unicode.Should().Be('C');
    }

    [Fact]
    public void BuildFromFragments_GlyphX_IsMonotonicallyIncreasing()
    {
        List<TextFragment> fragments =
        [
            new TextFragment("XYZ", 100, 50, 12),
        ];

        IReadOnlyList<TextRun> runs = TextRunBuilder.BuildFromFragments(fragments);
        IReadOnlyList<GlyphPosition> glyphs = runs[0].Glyphs;

        glyphs[0].X.Should().Be(100);
        glyphs[1].X.Should().BeGreaterThan(glyphs[0].X);
        glyphs[2].X.Should().BeGreaterThan(glyphs[1].X);
    }

    [Fact]
    public void BuildFromFragments_AstralCharacter_OccupiesOneGlyph()
    {
        // 🐝 is a single code point but takes a surrogate pair in UTF-16.
        const string Bee = "\uD83D\uDC1D";
        List<TextFragment> fragments = [new TextFragment(Bee, 0, 0, 12)];

        IReadOnlyList<TextRun> runs = TextRunBuilder.BuildFromFragments(fragments);
        runs[0].Glyphs.Should().HaveCount(1);
        runs[0].Glyphs[0].Unicode.Should().Be(0x1F41D);
    }
}
