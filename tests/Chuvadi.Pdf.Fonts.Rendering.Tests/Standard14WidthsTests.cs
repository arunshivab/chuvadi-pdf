// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R1 D3b — Standard14Widths tests

using System;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class Standard14WidthsTests
{
    [Fact]
    public void UnitsPerEm_Is1000()
    {
        Standard14Widths.UnitsPerEm.Should().Be(1000);
    }

    // ── Courier family: monospace, every glyph is 600 em ──────────────────

    [Theory]
    [InlineData("Courier", 'A')]
    [InlineData("Courier", 'i')]
    [InlineData("Courier", ' ')]
    [InlineData("Courier-Bold", 'X')]
    [InlineData("Courier-Oblique", 'M')]
    [InlineData("Courier-BoldOblique", 'w')]
    public void GetWidth_CourierFamily_IsAlwaysMonospace(string font, char ch)
    {
        Standard14Widths.GetWidth(font, ch).Should().Be(600);
    }

    // ── Space character: well-known per-family AFM constants ──────────────

    [Theory]
    [InlineData("Helvetica", 278)]
    [InlineData("Helvetica-Bold", 278)]
    [InlineData("Helvetica-Oblique", 278)]
    [InlineData("Times-Roman", 250)]
    [InlineData("Times-Bold", 250)]
    [InlineData("Symbol", 250)]
    [InlineData("ZapfDingbats", 278)]
    public void GetWidth_Space_MatchesPerFamilyExactValues(string font, int expected)
    {
        Standard14Widths.GetWidth(font, 0x20).Should().Be(expected);
    }

    // ── Non-space characters in variable-width families ───────────────────

    [Fact]
    public void GetWidth_Helvetica_ReturnsExactAfmWidths()
    {
        Standard14Widths.GetWidth("Helvetica", 'A').Should().Be(667);
        Standard14Widths.GetWidth("Helvetica", 'i').Should().Be(222);
        Standard14Widths.GetWidth("Helvetica", 'W').Should().Be(944);
        Standard14Widths.GetWidth("Helvetica-Bold", 'i').Should().Be(278);
    }

    [Fact]
    public void GetWidth_TimesRoman_ReturnsExactAfmWidths()
    {
        // Exact Adobe Core 14 AFM values, not approximations.
        Standard14Widths.GetWidth("Times-Roman", 'A').Should().Be(722);
        Standard14Widths.GetWidth("Times-Roman", 'I').Should().Be(333);
        Standard14Widths.GetWidth("Times-Roman", 'M').Should().Be(889);
        Standard14Widths.GetWidth("Times-Roman", 'i').Should().Be(278);
        Standard14Widths.GetWidth("Times-Roman", '0').Should().Be(500);
        Standard14Widths.GetWidth("Times-Bold", 'I').Should().Be(389);
        Standard14Widths.GetWidth("Times-Bold", 'M').Should().Be(944);
    }

    [Fact]
    public void GetWidth_ZapfDingbats_ReturnsExactAfmWidth()
    {
        // Built-in encoding: code 0x21 is the a1 dingbat, 974/1000 em.
        Standard14Widths.GetWidth("ZapfDingbats", 0x21).Should().Be(974);
    }

    // ── Non-Standard 14 fallback ──────────────────────────────────────────

    [Fact]
    public void GetWidth_UnknownFontName_ReturnsFallback()
    {
        Standard14Widths.GetWidth("NotAFont", 'A').Should().Be(500);
    }

    [Fact]
    public void GetWidth_UnknownFontName_Space_ReturnsFallback()
    {
        // Unknown fonts return the flat em-half default for every code.
        Standard14Widths.GetWidth("NotAFont", 0x20).Should().Be(500);
    }

    // ── Null guards ───────────────────────────────────────────────────────

    [Fact]
    public void GetWidth_NullFontName_Throws()
    {
        Action act = () => Standard14Widths.GetWidth(null!, 'A');
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsStandard14_NullFontName_Throws()
    {
        Action act = () => Standard14Widths.IsStandard14(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── IsStandard14 recognition ──────────────────────────────────────────

    [Theory]
    [InlineData("Helvetica")]
    [InlineData("Helvetica-Bold")]
    [InlineData("Helvetica-Oblique")]
    [InlineData("Helvetica-BoldOblique")]
    [InlineData("Times-Roman")]
    [InlineData("Times-Bold")]
    [InlineData("Times-Italic")]
    [InlineData("Times-BoldItalic")]
    [InlineData("Courier")]
    [InlineData("Courier-Bold")]
    [InlineData("Courier-Oblique")]
    [InlineData("Courier-BoldOblique")]
    [InlineData("Symbol")]
    [InlineData("ZapfDingbats")]
    public void IsStandard14_RecognizesAllFourteen(string fontName)
    {
        Standard14Widths.IsStandard14(fontName).Should().BeTrue();
    }

    [Theory]
    [InlineData("Arial")]
    [InlineData("MyCustomFont")]
    [InlineData("")]
    [InlineData("helvetica")] // Case-sensitive
    [InlineData("Helvetica-Italic")] // Not a real Standard 14 name; correct is "Oblique"
    public void IsStandard14_RejectsNonStandard14Names(string fontName)
    {
        Standard14Widths.IsStandard14(fontName).Should().BeFalse();
    }
}
