// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Apple TrueType Reference Manual — arithmetic, logical, flow control,
//        DELTA, and the shift/interpolation instructions
// PHASE: Phase 2 — TrueType bytecode hinting (Stages 5 and 6) tests

using Chuvadi.Pdf.Fonts.Rendering.Hinting;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class HintingArithmeticAndInterpolationTests
{
    private const int UnitsPerEm = 2048;

    // ── Arithmetic / logical / storage programs (PUSHB[1]=0xB1, PUSHB[0]=0xB0) ──
    private static readonly byte[] AddProg = [0xB1, 30, 12, 0x60];
    private static readonly byte[] SubProg = [0xB1, 50, 12, 0x61];
    private static readonly byte[] MulProg = [0xB1, 128, 192, 0x63];   // 2.0 * 3.0 = 6.0 (26.6)
    private static readonly byte[] DivProg = [0xB1, 192, 128, 0x62];   // 3.0 / 2.0 = 1.5 (26.6)
    private static readonly byte[] AbsProg = [0xB8, 0xFF, 0xCE, 0x64]; // PUSHW -50; ABS
    private static readonly byte[] NegProg = [0xB0, 50, 0x65];
    private static readonly byte[] MaxProg = [0xB1, 30, 80, 0x8B];
    private static readonly byte[] MinProg = [0xB1, 30, 80, 0x8C];
    private static readonly byte[] AndProg = [0xB1, 5, 3, 0x5A];
    private static readonly byte[] OrProg = [0xB1, 0, 0, 0x5B];
    private static readonly byte[] NotProg = [0xB0, 0, 0x5C];
    private static readonly byte[] GtProg = [0xB1, 80, 30, 0x52];
    private static readonly byte[] LtProg = [0xB1, 80, 30, 0x50];
    private static readonly byte[] EqProg = [0xB1, 5, 5, 0x54];
    private static readonly byte[] FloorProg = [0xB0, 100, 0x66];
    private static readonly byte[] CeilingProg = [0xB0, 100, 0x67];
    private static readonly byte[] OddProg = [0xB0, 64, 0x56];
    private static readonly byte[] EvenProg = [0xB0, 128, 0x57];
    private static readonly byte[] RollProg = [0xB2, 1, 2, 3, 0x8A];   // PUSHB[2] 1,2,3; ROLL
    private static readonly byte[] StorageProg = [0xB1, 0, 99, 0x42, 0xB0, 0, 0x43]; // WS s[0]=99; RS s[0]

    // ── Flow-control programs (IF=0x58, ELSE=0x1B, EIF=0x59, JMPR=0x1C, JROT=0x78) ──
    private static readonly byte[] IfTrueProg = [0xB0, 1, 0x58, 0xB0, 42, 0x59];
    private static readonly byte[] IfFalseProg = [0xB0, 0, 0x58, 0xB0, 42, 0x59, 0xB0, 7];
    private static readonly byte[] IfElseTrueProg = [0xB0, 1, 0x58, 0xB0, 10, 0x1B, 0xB0, 20, 0x59];
    private static readonly byte[] IfElseFalseProg = [0xB0, 0, 0x58, 0xB0, 10, 0x1B, 0xB0, 20, 0x59];
    private static readonly byte[] NestedIfProg = [0xB0, 1, 0x58, 0xB0, 0, 0x58, 0xB0, 1, 0x1B, 0xB0, 2, 0x59, 0x59];
    private static readonly byte[] JmprProg = [0xB0, 3, 0x1C, 0xB0, 99, 0xB0, 7];
    private static readonly byte[] JrotTakenProg = [0xB1, 3, 1, 0x78, 0xB0, 99, 0xB0, 7];

    // ── DELTA programs (arg 0x0F = relative ppem 0, magnitude selector 15 → +8 steps) ──
    // At DeltaShift 3 the step is 64>>3 = 8 (26.6); 8 steps = +64 (one pixel).
    private static readonly byte[] DeltaP1Prog = [0xB2, 0, 0x0F, 1, 0x5D];          // pair (point 0, arg 0x0F), count 1
    private static readonly byte[] DeltaC1Prog = [0xB2, 0, 0x0F, 1, 0x73, 0xB0, 0, 0x45]; // DELTAC1 then RCVT[0]

    // ── Geometry programs ──
    private static readonly byte[] ShpixProg = [0xB1, 0, 64, 0x38];                 // shift point 0 by +64
    private static readonly byte[] IpProg =
    [
        0xB1, 2, 100, 0x38,   // SHPIX point 2 by +100 (200 -> 300)
        0xB0, 0, 0x11,        // SRP1 = 0
        0xB0, 2, 0x12,        // SRP2 = 2
        0xB0, 1, 0x39,        // IP point 1
    ];
    private static readonly byte[] IupInterpProg =
    [
        0xB1, 2, 100, 0x38,   // SHPIX point 2 by +100 (touch + move: 200 -> 300)
        0xB1, 0, 0, 0x38,     // SHPIX point 0 by 0 (touch only)
        0x30,                 // IUP[x]
    ];
    private static readonly byte[] IupShiftProg =
    [
        0xB1, 0, 50, 0x38,    // SHPIX point 0 by +50 (single touched anchor)
        0x30,                 // IUP[x]
    ];
    private static readonly byte[] AlignRpProg = [0xB0, 0, 0x10, 0xB0, 1, 0x3C];    // SRP0 0; ALIGNRP point 1
    private static readonly byte[] AlignPtsProg = [0xB1, 0, 1, 0x27];               // ALIGNPTS p1=0, p2=1
    private static readonly byte[] UtpProg = [0xB1, 0, 0, 0x38, 0xB0, 0, 0x29];     // touch p0 then UTP p0
    private static readonly byte[] ShcProg =
    [
        0xB1, 0, 64, 0x38,    // SHPIX point 0 by +64
        0xB0, 0, 0x12,        // SRP2 = 0
        0xB0, 1, 0x34,        // SHC[0] contour 1
    ];
    private static readonly byte[] IsectProg = [0xB4, 4, 0, 1, 2, 3, 0x0F];         // point 4, lineA(0,1), lineB(2,3)
    private static readonly byte[] GetInfoProg = [0xB0, 1, 0x88];                   // GETINFO selector 1 (version)


    private static HintingInterpreter NewInterpreter()
    {
        return new HintingInterpreter(HintingLimits.Default);
    }

    private static RawGlyph Glyph(int[] xs, int[] ys, int[] ends, byte[] instructions)
    {
        bool[] onCurve = new bool[xs.Length];
        for (int i = 0; i < onCurve.Length; i++)
        {
            onCurve[i] = true;
        }

        return new RawGlyph(xs, ys, onCurve, ends, instructions, xs.Length);
    }

    // Runs a pure-stack program (no zones needed) and returns the interpreter.
    private static HintingInterpreter Run(byte[] program)
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunProgram(program);
        return interp;
    }

    // Builds the glyph zone at 1:1 scale (ppem 32, unitsPerEm 2048) and runs the
    // program as the glyph's instruction stream; returns the fitted zone.
    private static Zone HintAtUnitScale(int[] xs, int[] ys, int[] ends, byte[] program, byte[]? cvt = null)
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(32, UnitsPerEm, cvt, null);
        return interp.HintGlyph(Glyph(xs, ys, ends, program));
    }

    [Fact]
    public void Arithmetic_AddSubMulDiv()
    {
        Run(AddProg).StackSnapshot()[^1].Should().Be(42);
        Run(SubProg).StackSnapshot()[^1].Should().Be(38);
        Run(MulProg).StackSnapshot()[^1].Should().Be(384);   // 6.0 in 26.6
        Run(DivProg).StackSnapshot()[^1].Should().Be(96);    // 1.5 in 26.6
    }

    [Fact]
    public void Arithmetic_AbsNegMaxMin()
    {
        Run(AbsProg).StackSnapshot()[^1].Should().Be(50);
        Run(NegProg).StackSnapshot()[^1].Should().Be(-50);
        Run(MaxProg).StackSnapshot()[^1].Should().Be(80);
        Run(MinProg).StackSnapshot()[^1].Should().Be(30);
    }

    [Fact]
    public void Logical_AndOrNotComparisons()
    {
        Run(AndProg).StackSnapshot()[^1].Should().Be(1);
        Run(OrProg).StackSnapshot()[^1].Should().Be(0);
        Run(NotProg).StackSnapshot()[^1].Should().Be(1);
        Run(GtProg).StackSnapshot()[^1].Should().Be(1);
        Run(LtProg).StackSnapshot()[^1].Should().Be(0);
        Run(EqProg).StackSnapshot()[^1].Should().Be(1);
    }

    [Fact]
    public void Rounding_FloorCeilingOddEven()
    {
        Run(FloorProg).StackSnapshot()[^1].Should().Be(64);
        Run(CeilingProg).StackSnapshot()[^1].Should().Be(128);
        Run(OddProg).StackSnapshot()[^1].Should().Be(1);
        Run(EvenProg).StackSnapshot()[^1].Should().Be(1);
    }

    [Fact]
    public void Roll_RotatesTopThreeElements()
    {
        int[] stack = Run(RollProg).StackSnapshot();
        stack.Should().Equal(2, 3, 1);
    }

    [Fact]
    public void Storage_WriteThenRead()
    {
        Run(StorageProg).StackSnapshot()[^1].Should().Be(99);
    }

    [Fact]
    public void Flow_IfTrueExecutesThenBranch()
    {
        Run(IfTrueProg).StackSnapshot()[^1].Should().Be(42);
    }

    [Fact]
    public void Flow_IfFalseSkipsThenBranch()
    {
        Run(IfFalseProg).StackSnapshot()[^1].Should().Be(7);
    }

    [Fact]
    public void Flow_IfElseTakesCorrectBranch()
    {
        Run(IfElseTrueProg).StackSnapshot()[^1].Should().Be(10);
        Run(IfElseFalseProg).StackSnapshot()[^1].Should().Be(20);
    }

    [Fact]
    public void Flow_NestedIfElse()
    {
        Run(NestedIfProg).StackSnapshot()[^1].Should().Be(2);
    }

    [Fact]
    public void Flow_JmprSkipsForward()
    {
        Run(JmprProg).StackSnapshot()[^1].Should().Be(7);
    }

    [Fact]
    public void Flow_JrotJumpsWhenTrue()
    {
        Run(JrotTakenProg).StackSnapshot()[^1].Should().Be(7);
    }

    [Fact]
    public void DeltaP_MovesPointAtMatchingPpem()
    {
        // ppem 9 = DeltaBase(9) + 0; arg 0x0F => +64 (one pixel) along x.
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(9, UnitsPerEm, null, null);
        Zone zone = interp.HintGlyph(Glyph([0], [0], [0], DeltaP1Prog));
        zone.CurrentX[0].Should().Be(64);
    }

    [Fact]
    public void DeltaP_NoEffectAtNonMatchingPpem()
    {
        // ppem 20 != target 9, so the exception does not apply.
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(20, UnitsPerEm, null, null);
        Zone zone = interp.HintGlyph(Glyph([0], [0], [0], DeltaP1Prog));
        zone.CurrentX[0].Should().Be(0);
    }

    [Fact]
    public void DeltaC_AdjustsControlValueAtMatchingPpem()
    {
        // CVT[0] starts at 0; +64 at ppem 9; RCVT reads it back.
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(9, UnitsPerEm, [0x00, 0x00], null);
        interp.HintGlyph(Glyph([0], [0], [0], DeltaC1Prog));
        interp.StackSnapshot()[^1].Should().Be(64);
    }

    [Fact]
    public void Shpix_ShiftsPointByPixelAmount()
    {
        Zone zone = HintAtUnitScale([0], [0], [0], ShpixProg);
        zone.CurrentX[0].Should().Be(64);
    }

    [Fact]
    public void Ip_InterpolatesPointWithinExpandedRange()
    {
        // p0=0 (rp1), p2=200 (rp2) -> p2 moved to 300; p1=100 interpolates to 150.
        Zone zone = HintAtUnitScale([0, 100, 200], [0, 0, 0], [2], IpProg);
        zone.CurrentX[1].Should().Be(150);
    }

    [Fact]
    public void Iup_InterpolatesUntouchedPointBetweenAnchors()
    {
        Zone zone = HintAtUnitScale([0, 100, 200], [0, 0, 0], [2], IupInterpProg);
        zone.CurrentX[0].Should().Be(0);
        zone.CurrentX[1].Should().Be(150);   // interpolated
        zone.CurrentX[2].Should().Be(300);
    }

    [Fact]
    public void Iup_ShiftsRigidlyWithSingleAnchor()
    {
        Zone zone = HintAtUnitScale([0, 100, 200], [0, 0, 0], [2], IupShiftProg);
        zone.CurrentX[0].Should().Be(50);
        zone.CurrentX[1].Should().Be(150);   // shifted by the single anchor's delta
        zone.CurrentX[2].Should().Be(250);
    }

    [Fact]
    public void AlignRp_MovesPointOntoReference()
    {
        Zone zone = HintAtUnitScale([0, 128], [0, 0], [1], AlignRpProg);
        zone.CurrentX[1].Should().Be(0);
    }

    [Fact]
    public void AlignPts_MovesBothPointsToMidpoint()
    {
        Zone zone = HintAtUnitScale([0, 128], [0, 0], [1], AlignPtsProg);
        zone.CurrentX[0].Should().Be(64);
        zone.CurrentX[1].Should().Be(64);
    }

    [Fact]
    public void Utp_ClearsTouchFlag()
    {
        Zone zone = HintAtUnitScale([0], [0], [0], UtpProg);
        zone.TouchedX[0].Should().BeFalse();
    }

    [Fact]
    public void Shc_ShiftsContourByReferenceMovement()
    {
        // Two contours: pts 0-1 and pts 2-3. Move p0 (rp2) by +64, shift contour 1.
        Zone zone = HintAtUnitScale([0, 50, 100, 150], [0, 0, 0, 0], [1, 3], ShcProg);
        zone.CurrentX[1].Should().Be(50);    // contour 0 (other point) unchanged
        zone.CurrentX[2].Should().Be(164);   // contour 1 shifted by 64
        zone.CurrentX[3].Should().Be(214);
    }

    [Fact]
    public void Isect_MovesPointToLineIntersection()
    {
        // Line A horizontal through (0,0)-(200,0); line B vertical through
        // (100,-100)-(100,100); intersection (100,0); point 4 moves there.
        Zone zone = HintAtUnitScale(
            [0, 200, 100, 100, 0],
            [0, 0, -100, 100, 0],
            [4],
            IsectProg);
        zone.CurrentX[4].Should().Be(100);
        zone.CurrentY[4].Should().Be(0);
    }

    [Fact]
    public void GetInfo_ReportsScalerVersion()
    {
        Run(GetInfoProg).StackSnapshot()[^1].Should().Be(42);
    }
}
