// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Apple TrueType Reference Manual — fixed-point arithmetic
// PHASE: Phase 2 — TrueType bytecode hinting (Stage 3) tests

using Chuvadi.Pdf.Fonts.Rendering.Hinting;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class F26Dot6Tests
{
    [Fact]
    public void FromPixels_AndToPixels_RoundTrip()
    {
        F26Dot6.FromPixels(3).Should().Be(192);
        F26Dot6.ToPixels(192).Should().Be(3);
        F26Dot6.ToPixels(200).Should().Be(3); // truncates toward zero
    }

    [Fact]
    public void ToDouble_ConvertsToPixels()
    {
        F26Dot6.ToDouble(96).Should().BeApproximately(1.5, 1e-9);
    }

    [Fact]
    public void Floor_RoundsTowardNegativeInfinity()
    {
        F26Dot6.Floor(70).Should().Be(64);
        F26Dot6.Floor(-70).Should().Be(-128);
    }

    [Fact]
    public void Ceiling_RoundsTowardPositiveInfinity()
    {
        F26Dot6.Ceiling(70).Should().Be(128);
        F26Dot6.Ceiling(64).Should().Be(64);
    }

    [Fact]
    public void Round_RoundsToNearestPixel()
    {
        F26Dot6.Round(31).Should().Be(0);
        F26Dot6.Round(32).Should().Be(64); // tie rounds toward positive infinity
        F26Dot6.Round(70).Should().Be(64);
        F26Dot6.Round(96).Should().Be(128);
    }

    [Fact]
    public void Mul_MultipliesInFixedPoint()
    {
        F26Dot6.Mul(64, 64).Should().Be(64);   // 1.0 * 1.0
        F26Dot6.Mul(32, 64).Should().Be(32);   // 0.5 * 1.0
        F26Dot6.Mul(96, 96).Should().Be(144);  // 1.5 * 1.5 = 2.25
    }

    [Fact]
    public void Div_DividesInFixedPoint()
    {
        F26Dot6.Div(64, 64).Should().Be(64);    // 1.0 / 1.0
        F26Dot6.Div(64, 128).Should().Be(32);   // 1.0 / 2.0 = 0.5
        F26Dot6.Div(-64, 128).Should().Be(-32); // -0.5
        F26Dot6.Div(10, 0).Should().Be(0);      // divide by zero guarded
    }
}

public sealed class F2Dot14Tests
{
    [Fact]
    public void ToDouble_ConvertsToUnitScale()
    {
        F2Dot14.ToDouble(0x4000).Should().BeApproximately(1.0, 1e-9);
        F2Dot14.ToDouble(0x2000).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Mul_MultipliesInFixedPoint()
    {
        F2Dot14.Mul(0x4000, 0x4000).Should().Be(0x4000); // 1.0 * 1.0
        F2Dot14.Mul(0x2000, 0x4000).Should().Be(0x2000); // 0.5 * 1.0
    }

    [Fact]
    public void Dot_ComputesVectorDotProduct()
    {
        // Parallel unit vectors on the x axis: dot == 1.0.
        F2Dot14.Dot(0x4000, 0, 0x4000, 0).Should().Be(0x4000);
        // Orthogonal axes: dot == 0.
        F2Dot14.Dot(0x4000, 0, 0, 0x4000).Should().Be(0);
        // Parallel unit vectors on the y axis: dot == 1.0.
        F2Dot14.Dot(0, 0x4000, 0, 0x4000).Should().Be(0x4000);
    }
}
