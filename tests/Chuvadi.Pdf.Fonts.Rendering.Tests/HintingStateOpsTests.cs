// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Apple TrueType Reference Manual — rounding and vector instructions
// PHASE: Phase 2 — TrueType bytecode hinting (Stage 3) tests

using Chuvadi.Pdf.Fonts.Rendering.Hinting;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class HintingRoundingTests
{
    private static readonly byte[] Rtg = [0x18];
    private static readonly byte[] Rthg = [0x19];
    private static readonly byte[] Rtdg = [0x3D];
    private static readonly byte[] Rdtg = [0x7D];
    private static readonly byte[] Rutg = [0x7C];
    private static readonly byte[] Roff = [0x7A];
    private static readonly byte[] Sround48 = [0xB0, 0x48, 0x76];   // PUSHB 0x48; SROUND
    private static readonly byte[] S45Round48 = [0xB0, 0x48, 0x77]; // PUSHB 0x48; S45ROUND

    private static HintingInterpreter NewInterpreter()
    {
        return new HintingInterpreter(HintingLimits.Default);
    }

    [Fact]
    public void DefaultState_IsRoundToGrid()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.Round(70, 0).Should().Be(64);
        interp.Round(96, 0).Should().Be(128);
        interp.Round(-70, 0).Should().Be(-64);
    }

    [Fact]
    public void Rtg_RoundsToNearestPixel()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(Rtg);
        interp.Round(70, 0).Should().Be(64);
        interp.Round(96, 0).Should().Be(128);
    }

    [Fact]
    public void Rthg_RoundsToNearestHalfPixel()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(Rthg);
        interp.Round(10, 0).Should().Be(32);
        interp.Round(70, 0).Should().Be(96);
    }

    [Fact]
    public void Rtdg_RoundsToNearestHalfGrid()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(Rtdg);
        interp.Round(10, 0).Should().Be(0);
        interp.Round(20, 0).Should().Be(32);
    }

    [Fact]
    public void Rdtg_RoundsDown()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(Rdtg);
        interp.Round(70, 0).Should().Be(64);
        interp.Round(63, 0).Should().Be(0);
    }

    [Fact]
    public void Rutg_RoundsUp()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(Rutg);
        interp.Round(1, 0).Should().Be(64);
        interp.Round(64, 0).Should().Be(64);
        interp.Round(65, 0).Should().Be(128);
    }

    [Fact]
    public void Roff_DoesNotRoundButAppliesCompensation()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(Roff);
        interp.Round(70, 0).Should().Be(70);
        interp.Round(70, 5).Should().Be(75);
        interp.Round(-70, 5).Should().Be(-75);
    }

    [Fact]
    public void Sround_DecodesSelectorToRoundToGrid()
    {
        // Selector 0x48 -> period grid (64), phase 0, threshold 32 == RTG.
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(Sround48);
        interp.Round(70, 0).Should().Be(64);
    }

    [Fact]
    public void S45Round_DecodesSelectorWithDiagonalPeriod()
    {
        // Selector 0x48 -> period 45, phase 0, threshold 22.
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(S45Round48);
        interp.Round(50, 0).Should().Be(45);
        interp.Round(70, 0).Should().Be(90);
    }
}

public sealed class HintingVectorTests
{
    private static readonly byte[] SvtcaX = [0x01];
    private static readonly byte[] SvtcaY = [0x00];
    private static readonly byte[] SpvtcaY = [0x02];
    private static readonly byte[] SfvtcaY = [0x04];

    private static HintingInterpreter NewInterpreter()
    {
        return new HintingInterpreter(HintingLimits.Default);
    }

    [Fact]
    public void SvtcaX_SetsBothVectorsToXAxis()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(SvtcaX);
        interp.State.ProjectionVectorX.Should().Be(0x4000);
        interp.State.ProjectionVectorY.Should().Be(0);
        interp.State.FreedomVectorX.Should().Be(0x4000);
        interp.State.FreedomVectorY.Should().Be(0);
        interp.State.DualProjectionVectorX.Should().Be(0x4000);
    }

    [Fact]
    public void SvtcaY_SetsBothVectorsToYAxis()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(SvtcaY);
        interp.State.ProjectionVectorX.Should().Be(0);
        interp.State.ProjectionVectorY.Should().Be(0x4000);
        interp.State.FreedomVectorX.Should().Be(0);
        interp.State.FreedomVectorY.Should().Be(0x4000);
    }

    [Fact]
    public void Spvtca_SetsProjectionOnly_LeavingFreedom()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(SpvtcaY);
        interp.State.ProjectionVectorX.Should().Be(0);
        interp.State.ProjectionVectorY.Should().Be(0x4000);
        // Freedom vector keeps its default x-axis value.
        interp.State.FreedomVectorX.Should().Be(0x4000);
        interp.State.FreedomVectorY.Should().Be(0);
    }

    [Fact]
    public void Sfvtca_SetsFreedomOnly_LeavingProjection()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(SfvtcaY);
        interp.State.FreedomVectorX.Should().Be(0);
        interp.State.FreedomVectorY.Should().Be(0x4000);
        // Projection vector keeps its default x-axis value.
        interp.State.ProjectionVectorX.Should().Be(0x4000);
        interp.State.ProjectionVectorY.Should().Be(0);
    }
}
