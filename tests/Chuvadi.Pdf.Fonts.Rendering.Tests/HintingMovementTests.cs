// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Apple TrueType Reference Manual — measurement and point-movement instructions
// PHASE: Phase 2 — TrueType bytecode hinting (Stage 4) tests

using Chuvadi.Pdf.Fonts.Rendering.Hinting;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class HintingMovementTests
{
    private const int UnitsPerEm = 2048;

    // Programs, hoisted to fields (CA1861).
    private static readonly byte[] Mppem = [0x4B];
    private static readonly byte[] ReadCvt0 = [0xB0, 0x00, 0x45];                          // PUSHB 0; RCVT
    private static readonly byte[] WriteThenReadCvt0 = [0xB1, 0x00, 0x46, 0x44, 0xB0, 0x00, 0x45]; // PUSHB 0,70; WCVTP; PUSHB 0; RCVT
    private static readonly byte[] WriteFunitsThenReadCvt1 = [0xB1, 0x01, 0x64, 0x70, 0xB0, 0x01, 0x45]; // PUSHB 1,100; WCVTF; PUSHB 1; RCVT
    private static readonly byte[] GetCoordPoint0 = [0xB0, 0x00, 0x46];                    // PUSHB 0; GC[0]
    private static readonly byte[] SetCoordPoint0To128 = [0xB1, 0x00, 0x80, 0x48];         // PUSHB 0,128; SCFS
    private static readonly byte[] MdapRoundPoint0 = [0xB0, 0x00, 0x2F];                   // PUSHB 0; MDAP[1]
    private static readonly byte[] Srp0ThenMdrpPoint1 = [0xB0, 0x00, 0x10, 0xB0, 0x01, 0xC4]; // PUSHB 0; SRP0; PUSHB 1; MDRP[round]
    private static readonly byte[] Srp0ThenMirpPoint1 = [0xB0, 0x00, 0x10, 0xB1, 0x01, 0x00, 0xE4]; // PUSHB 0; SRP0; PUSHB 1,0; MIRP[round]
    private static readonly byte[] SpvtlParallel = [0xB1, 0x00, 0x01, 0x06];               // PUSHB 0,1; SPVTL[0]
    private static readonly byte[] SpvtlPerpendicular = [0xB1, 0x00, 0x01, 0x07];          // PUSHB 0,1; SPVTL[1]
    private static readonly byte[] WriteCvt0To70 = [0xB1, 0x00, 0x46, 0x44];               // PUSHB 0,70; WCVTP

    // Control Value Tables (big-endian int16 font units).
    private static readonly byte[] Cvt100And200 = [0x00, 0x64, 0x00, 0xC8];
    private static readonly byte[] Cvt55 = [0x00, 0x37];
    private static readonly byte[] CvtSingleZero = [0x00, 0x00];

    private static readonly int[] NoContours = [];
    private static readonly byte[] NoInstructions = [];

    private static HintingInterpreter NewInterpreter()
    {
        return new HintingInterpreter(HintingLimits.Default);
    }

    // Builds a single-contour glyph with all-on-curve points and no phantom
    // points (the interpreter treats every point uniformly at this stage).
    private static RawGlyph Glyph(int[] xs, int[] ys, byte[] instructions)
    {
        bool[] onCurve = new bool[xs.Length];
        for (int i = 0; i < onCurve.Length; i++)
        {
            onCurve[i] = true;
        }

        int[] ends = xs.Length > 0 ? [xs.Length - 1] : NoContours;
        return new RawGlyph(xs, ys, onCurve, ends, instructions, xs.Length);
    }

    [Fact]
    public void MulFix_ScalesFontUnitsToTwentySixDotSix()
    {
        // ppem 12, unitsPerEm 2048 -> scale 24576 (16.16): one em scales to 12px (768 in 26.6).
        const int scale = 24576;
        F26Dot6.MulFix(2048, scale).Should().Be(768);
        F26Dot6.MulFix(1024, scale).Should().Be(384);
        F26Dot6.MulFix(-1024, scale).Should().Be(-384);
    }

    [Fact]
    public void HintGlyph_ScalesOutlineToTwentySixDotSix()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(12, UnitsPerEm, null, null);
        Zone zone = interp.HintGlyph(Glyph([2048, 1024, 0], [1024, 0, 512], NoInstructions));

        zone.CurrentX[0].Should().Be(768);
        zone.CurrentX[1].Should().Be(384);
        zone.CurrentX[2].Should().Be(0);
        zone.CurrentY[0].Should().Be(384);
        zone.CurrentY[2].Should().Be(192);

        // Original equals current before any hinting moves a point.
        zone.OriginalX[0].Should().Be(768);
    }

    [Fact]
    public void Mppem_PushesPixelsPerEm()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(18, UnitsPerEm, null, null);
        interp.HintGlyph(Glyph([0], [0], Mppem));

        interp.StackDepth.Should().Be(1);
        interp.StackSnapshot()[0].Should().Be(18);
    }

    [Fact]
    public void Rcvt_ReadsScaledControlValue()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, Cvt100And200, null);   // scale 32768 (x0.5)
        interp.HintGlyph(Glyph([0], [0], ReadCvt0));

        interp.StackSnapshot()[^1].Should().Be(50);               // 100 funits -> 50 (26.6)
    }

    [Fact]
    public void Wcvtp_WritesControlValueInPixels()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, Cvt100And200, null);
        interp.HintGlyph(Glyph([0], [0], WriteThenReadCvt0));

        interp.StackSnapshot()[^1].Should().Be(70);
    }

    [Fact]
    public void Wcvtf_WritesScaledFontUnits()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, Cvt100And200, null);   // scale x0.5
        interp.HintGlyph(Glyph([0], [0], WriteFunitsThenReadCvt1));

        interp.StackSnapshot()[^1].Should().Be(50);               // 100 funits -> 50
    }

    [Fact]
    public void Gc_ProjectsCurrentCoordinateOntoProjectionVector()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, null, null);           // scale x0.5: 2048 -> 1024
        interp.HintGlyph(Glyph([2048], [0], GetCoordPoint0));

        interp.StackSnapshot()[^1].Should().Be(1024);
    }

    [Fact]
    public void Scfs_MovesPointToTheGivenProjectedCoordinate()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, null, null);
        Zone zone = interp.HintGlyph(Glyph([2048], [0], SetCoordPoint0To128));

        zone.CurrentX[0].Should().Be(128);
        zone.TouchedX[0].Should().BeTrue();
    }

    [Fact]
    public void MdapRound_SnapsPointProjectionToTheGrid()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(64, UnitsPerEm, null, null);           // scale x2: 35 -> 70
        Zone zone = interp.HintGlyph(Glyph([35], [0], MdapRoundPoint0));

        zone.CurrentX[0].Should().Be(64);                         // 70 rounds to grid 64
    }

    [Fact]
    public void MdrpRound_MovesPointToRoundedOriginalDistanceFromRp0()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(64, UnitsPerEm, null, null);           // 35 -> 70
        Zone zone = interp.HintGlyph(Glyph([0, 35], [0, 0], Srp0ThenMdrpPoint1));

        zone.CurrentX[1].Should().Be(64);                         // original distance 70 -> rounded 64
    }

    [Fact]
    public void MirpRound_MovesPointToRoundedControlValueDistance()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(64, UnitsPerEm, Cvt55, null);          // CVT 55 funits -> 110 (26.6); original distance 70
        Zone zone = interp.HintGlyph(Glyph([0, 35], [0, 0], Srp0ThenMirpPoint1));

        // The CVT distance (110), not the original (70), is used and rounded to 128.
        zone.CurrentX[1].Should().Be(128);
    }

    [Fact]
    public void SpvtlParallel_SetsProjectionVectorAlongTheLine()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(64, UnitsPerEm, null, null);           // 32 -> 64
        interp.HintGlyph(Glyph([0, 32], [0, 0], SpvtlParallel));

        interp.State.ProjectionVectorX.Should().Be(16384);
        interp.State.ProjectionVectorY.Should().Be(0);
    }

    [Fact]
    public void SpvtlPerpendicular_SetsProjectionVectorPerpendicularToTheLine()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(64, UnitsPerEm, null, null);
        interp.HintGlyph(Glyph([0, 32], [0, 0], SpvtlPerpendicular));

        interp.State.ProjectionVectorX.Should().Be(0);
        interp.State.ProjectionVectorY.Should().Be(16384);
    }

    [Fact]
    public void PrepareSize_RunsPrepAndItsControlValueWritesPersist()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, CvtSingleZero, WriteCvt0To70);
        interp.HintGlyph(Glyph([0], [0], ReadCvt0));

        interp.StackSnapshot()[^1].Should().Be(70);
    }
}
