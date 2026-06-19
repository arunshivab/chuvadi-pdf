// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Adobe AFM data for the Standard 14 fonts
// Locks the per-glyph accuracy of the shared width table: unlike a per-font
// average, individual glyphs differ (Helvetica 'i' != 'W').

using System;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class Standard14GlyphWidthsTests
{
    [Theory]
    [InlineData("Helvetica")]
    [InlineData("Helvetica-Bold")]
    [InlineData("Times-Roman")]
    [InlineData("Courier")]
    [InlineData("Symbol")]
    public void IsStandard14_RecognizesStandardFonts(string baseFont)
    {
        Standard14GlyphWidths.IsStandard14(baseFont).Should().BeTrue();
    }

    [Fact]
    public void IsStandard14_RejectsUnknownFont()
    {
        Standard14GlyphWidths.IsStandard14("CustomEmbeddedFont").Should().BeFalse();
    }

    [Fact]
    public void Width_IsPerGlyph_NotAverage()
    {
        // The whole point of this table: narrow and wide glyphs differ.
        Standard14GlyphWidths.Width("Helvetica", 'i').Should().Be(222);
        Standard14GlyphWidths.Width("Helvetica", 'W').Should().Be(944);
        Standard14GlyphWidths.Width("Helvetica", 'A').Should().Be(667);
        Standard14GlyphWidths.Width("Helvetica", ' ').Should().Be(278);
    }

    [Fact]
    public void Width_Times_HasOwnMetrics()
    {
        Standard14GlyphWidths.Width("Times-Roman", 'A').Should().Be(722);
        Standard14GlyphWidths.Width("Times-Roman", ' ').Should().Be(250);
    }

    [Fact]
    public void Width_Courier_IsMonospace()
    {
        Standard14GlyphWidths.Width("Courier", 'i').Should().Be(600);
        Standard14GlyphWidths.Width("Courier", 'W').Should().Be(600);
    }

    [Fact]
    public void Width_NullFont_Throws()
    {
        Action act = () => Standard14GlyphWidths.Width(null!, 'A');
        act.Should().Throw<ArgumentNullException>();
    }
}
