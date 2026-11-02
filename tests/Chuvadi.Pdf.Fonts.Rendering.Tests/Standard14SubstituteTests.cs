// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.30 — substitutes for fonts a PDF does not embed (D-105).

using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class Standard14SubstituteTests
{
    [Theory]
    [InlineData("Arial", 32, 0, 0, "Helvetica")]
    [InlineData("ArialMT", 32, 0, 0, "Helvetica")]
    [InlineData("ABCDEF+Arial-BoldMT", 32, 0, 0, "Helvetica-Bold")]
    [InlineData("Arial,BoldItalic", 32, 0, 0, "Helvetica-BoldOblique")]
    [InlineData("Calibri", 32, 0, 0, "Helvetica")]
    [InlineData("Calibri", 34, 0, 0, "Helvetica")]
    [InlineData("TimesNewRomanPSMT", 34, 0, 0, "Times-Roman")]
    [InlineData("Times New Roman,Italic", 34, 0, 0, "Times-Italic")]
    [InlineData("Georgia-Bold", 34, 0, 0, "Times-Bold")]
    [InlineData("SomeBookFace", 2, 0, 0, "Times-Roman")]
    [InlineData("CourierNewPSMT", 33, 0, 0, "Courier")]
    [InlineData("Consolas", 32, 0, 700, "Courier-Bold")]
    [InlineData("AnyFixedFace", 1, -12, 0, "Courier-Oblique")]
    [InlineData("Unknown", 32, -11, 0, "Helvetica-Oblique")]
    [InlineData("Unknown", 32 | (1 << 18), 0, 0, "Helvetica-Bold")]
    [InlineData("Symbol", 4, 0, 0, "Symbol")]
    [InlineData("ZapfDingbatsITC", 4, 0, 0, "ZapfDingbats")]
    [InlineData("OpenSans-SemiBold", 32, 0, 600, "Helvetica-Bold")]
    public void Chooses_TheClosestStandardFont(string baseFont, int flags, double italicAngle, double weight, string expected)
    {
        Standard14Substitute.Choose(baseFont, flags, italicAngle, weight).Should().Be(expected);
    }

    [Theory]
    [InlineData("Wingdings-Regular")]
    [InlineData("Webdings")]
    public void PictureFonts_HaveNoSubstitute(string baseFont)
    {
        Standard14Substitute.Choose(baseFont, 4, 0, 0).Should().BeNull();
    }

    [Theory]
    [InlineData("Helvetica-Bold", "Liberation Sans")]
    [InlineData("Times-Italic", "Liberation Serif")]
    [InlineData("Courier", "Liberation Mono")]
    public void OutlineSource_NamesTheLiberationFace(string standard14, string expected)
    {
        Standard14Substitute.OutlineSource(standard14).Should().Be(expected);
    }
}
