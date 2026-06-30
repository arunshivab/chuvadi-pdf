// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using Chuvadi.Pdf.Xfa.Model;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Xfa.Tests;

public sealed class XfaMeasurementTests
{
    [Theory]
    [InlineData("72pt", 72.0)]
    [InlineData("1in", 72.0)]
    [InlineData("0.5in", 36.0)]
    [InlineData("25.4mm", 72.0)]
    [InlineData("2.54cm", 72.0)]
    [InlineData("1pc", 12.0)]
    [InlineData("36px", 36.0)]
    [InlineData("100", 100.0)]
    public void Parse_ConvertsUnitsToPoints(string input, double expectedPoints)
    {
        XfaMeasurement m = XfaMeasurement.Parse(input);
        m.Points.Should().BeApproximately(expectedPoints, 0.001);
    }

    [Fact]
    public void Parse_Millimetres_MatchesManualConversion()
    {
        // 202.146mm at 72/25.4 pt per mm
        XfaMeasurement m = XfaMeasurement.Parse("202.146mm");
        m.Points.Should().BeApproximately(202.146 / 25.4 * 72.0, 0.001);
    }

    [Fact]
    public void Parse_EmptyOrNull_ReturnsZero()
    {
        XfaMeasurement.Parse(null).Should().Be(XfaMeasurement.Zero);
        XfaMeasurement.Parse("").Should().Be(XfaMeasurement.Zero);
        XfaMeasurement.Parse("   ").Should().Be(XfaMeasurement.Zero);
    }

    [Fact]
    public void Parse_EmUnit_UsesEmPoints()
    {
        XfaMeasurement m = XfaMeasurement.Parse("2em", emPoints: 12.0);
        m.Points.Should().BeApproximately(24.0, 0.001);
    }

    [Fact]
    public void Parse_PercentUnit_UsesBase()
    {
        XfaMeasurement m = XfaMeasurement.Parse("50%", percentBasePoints: 200.0);
        m.Points.Should().BeApproximately(100.0, 0.001);
    }

    [Fact]
    public void Parse_NegativeValue_IsSupported()
    {
        XfaMeasurement.Parse("-12pt").Points.Should().BeApproximately(-12.0, 0.001);
    }

    [Fact]
    public void Parse_UnknownUnit_Throws()
    {
        Action act = () => XfaMeasurement.Parse("10furlongs");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_Malformed_ReturnsFalseAndZero()
    {
        bool ok = XfaMeasurement.TryParse("notanumber", out XfaMeasurement result);
        ok.Should().BeFalse();
        result.Should().Be(XfaMeasurement.Zero);
    }

    [Fact]
    public void Equality_WorksByPoints()
    {
        XfaMeasurement a = XfaMeasurement.Parse("1in");
        XfaMeasurement b = new XfaMeasurement(72.0);
        a.Should().Be(b);
        (a == b).Should().BeTrue();
        (a != b).Should().BeFalse();
    }
}
