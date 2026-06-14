// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.DisplayList.Tests;

public sealed class FontStyleClassifierTests
{
    [Theory]
    [InlineData("Helvetica", "Helvetica", 400, FontSlant.Normal)]
    [InlineData("Times-Bold", "Times", 700, FontSlant.Normal)]
    [InlineData("Times-BoldItalic", "Times", 700, FontSlant.Italic)]
    [InlineData("Arial,Bold", "Arial", 700, FontSlant.Normal)]
    [InlineData("Helvetica-Oblique", "Helvetica", 400, FontSlant.Oblique)]
    [InlineData("ABCDEF+Calibri", "Calibri", 400, FontSlant.Normal)]
    public void Classify_FromName(string baseFont, string family, int weight, FontSlant slant)
    {
        FontStyle style = FontStyleClassifier.Classify(baseFont, null, null, null);

        style.FontFamily.Should().Be(family);
        style.Weight.Should().Be(weight);
        style.Slant.Should().Be(slant);
    }

    [Fact]
    public void Classify_ItalicFlag_MarksItalic()
    {
        FontStyle style = FontStyleClassifier.Classify("Plain", 1 << 6, null, null);

        style.Slant.Should().Be(FontSlant.Italic);
        style.IsItalic.Should().BeTrue();
    }

    [Fact]
    public void Classify_ForceBoldFlag_MarksBold()
    {
        FontStyle style = FontStyleClassifier.Classify("Plain", 1 << 18, null, null);

        style.Weight.Should().Be(700);
        style.IsBold.Should().BeTrue();
    }

    [Fact]
    public void Classify_ItalicAngle_MarksItalic()
    {
        FontStyle style = FontStyleClassifier.Classify("Plain", null, -12.0, null);

        style.Slant.Should().Be(FontSlant.Italic);
        style.ItalicAngle.Should().Be(-12.0);
    }

    [Fact]
    public void Classify_HighStemV_MarksBold()
    {
        FontStyle style = FontStyleClassifier.Classify("Plain", null, null, 160);

        style.Weight.Should().Be(700);
    }

    [Fact]
    public void Classify_NullBaseFont_Throws()
    {
        Action act = () => FontStyleClassifier.Classify(null!, null, null, null);

        act.Should().Throw<ArgumentNullException>();
    }
}
