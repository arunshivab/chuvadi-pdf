// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Apple TrueType Reference Manual — SSW, ROUND/NROUND, MPS, MIRP;
//        conformance reference: FreeType interpreter v35 (ttinterp.c)
// PHASE: Phase 2.7 — Interpreter spec fixes tests

using Chuvadi.Pdf.Fonts.Rendering.Hinting;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class HintingSpecFixTests
{
    private const int UnitsPerEm = 2048;

    // Programs, hoisted to fields (CA1861). At ppem 16 / upem 2048 the scale
    // is 16 ÷ 2048: 100 font units → 50 in 26.6 pixels.
    private static readonly byte[] SswHundredFUnits = [0xB0, 0x64, 0x1F];                     // PUSHB 100; SSW
    private static readonly byte[] NroundBlack64 = [0xB0, 0x40, 0x6D];                        // PUSHB 64; NROUND[black]
    private static readonly byte[] NroundGray32 = [0xB0, 0x20, 0x6C];                         // PUSHB 32; NROUND[gray]
    private static readonly byte[] RoundWhite30 = [0xB0, 0x1E, 0x6A];                         // PUSHB 30; ROUND[white]
    private static readonly byte[] Mps = [0x4C];                                              // MPS

    // PUSHB 0; SZP1 — zp1 = twilight; PUSHB 0; SRP0; PUSHB 0,0; MIRP[round];
    // PUSHB 0; SZP2 — zp2 = twilight; PUSHB 0; GC[original].
    private static readonly byte[] MirpTwilightThenReadOriginal =
        [0xB0, 0x00, 0x14, 0xB0, 0x00, 0x10, 0xB1, 0x00, 0x00, 0xE4,
         0xB0, 0x00, 0x15, 0xB0, 0x00, 0x47];

    // PUSHB 0; SRP0; PUSHB 1,0; MIRP[round] — both zone pointers in the glyph zone.
    private static readonly byte[] MirpSameZoneRound = [0xB0, 0x00, 0x10, 0xB1, 0x01, 0x00, 0xE4];

    // PUSHB 0; SZP0 — zp0 = twilight; PUSHB 0; SRP0; PUSHB 1,0; MIRP[round].
    private static readonly byte[] MirpCrossZoneRound =
        [0xB0, 0x00, 0x13, 0xB0, 0x00, 0x10, 0xB1, 0x01, 0x00, 0xE4];

    // PUSHB 100; SSW; PUSHB 60; SSWCI; PUSHB 0; SRP0; PUSHB 1; MDRP[no flags].
    private static readonly byte[] MdrpWithSingleWidth =
        [0xB0, 0x64, 0x1F, 0xB0, 0x3C, 0x1E, 0xB0, 0x00, 0x10, 0xB0, 0x01, 0xC0];

    // PUSHB 120; SSW; PUSHB 30; SSWCI; PUSHB 0; SRP0; PUSHB 1,0; MIRP[no round].
    private static readonly byte[] MirpWithSingleWidthNoRound =
        [0xB0, 0x78, 0x1F, 0xB0, 0x1E, 0x1E, 0xB0, 0x00, 0x10, 0xB1, 0x01, 0x00, 0xE0];

    // Control Value Tables (big-endian int16 font units).
    private static readonly byte[] Cvt100 = [0x00, 0x64];

    private static readonly int[] NoContours = [];

    private static HintingInterpreter NewInterpreter()
        => new(HintingLimits.Default);

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

    // ── SSW ───────────────────────────────────────────────────────────────

    [Fact]
    public void Ssw_ScalesFontUnitsToPixels()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, null, null);
        interp.HintGlyph(Glyph([0], [0], SswHundredFUnits));

        // 100 font units at ppem 16 / upem 2048 → 50 in 26.6 — the same
        // FUnits-to-pixels conversion WCVTF performs (FreeType Ins_SSW).
        interp.State.SingleWidthValue.Should().Be(50);
    }

    // ── ROUND / NROUND engine compensation ────────────────────────────────

    [Fact]
    public void Nround_DefaultCompensation_IsIdentity()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, null, null);
        interp.HintGlyph(Glyph([0], [0], NroundBlack64));

        interp.StackSnapshot()[^1].Should().Be(64);
    }

    [Fact]
    public void Nround_AppliesEngineCompensationByDistanceType()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.SetEngineCompensation(1, 32);            // black: +half a pixel
        interp.PrepareSize(16, UnitsPerEm, null, null);
        interp.HintGlyph(Glyph([0], [0], NroundBlack64));

        interp.StackSnapshot()[^1].Should().Be(96);
    }

    [Fact]
    public void Nround_NeverCrossesZero()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.SetEngineCompensation(0, -64);           // gray: -1 pixel
        interp.PrepareSize(16, UnitsPerEm, null, null);
        interp.HintGlyph(Glyph([0], [0], NroundGray32));

        // 32 + (-64) = -32 would cross zero; Round_None clamps to 0.
        interp.StackSnapshot()[^1].Should().Be(0);
    }

    [Fact]
    public void Round_AppliesEngineCompensationBeforeRounding()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, null, null);
        interp.HintGlyph(Glyph([0], [0], RoundWhite30));
        interp.StackSnapshot()[^1].Should().Be(0, "30 rounds down to grid without compensation");

        HintingInterpreter compensated = NewInterpreter();
        compensated.SetEngineCompensation(2, 32);       // white: +half a pixel
        compensated.PrepareSize(16, UnitsPerEm, null, null);
        compensated.HintGlyph(Glyph([0], [0], RoundWhite30));
        compensated.StackSnapshot()[^1].Should().Be(64, "30 + 32 rounds up to the grid");
    }

    [Fact]
    public void SetEngineCompensation_RejectsBadDistanceType()
    {
        HintingInterpreter interp = NewInterpreter();
        System.Action act = () => interp.SetEngineCompensation(4, 0);
        act.Should().Throw<System.ArgumentOutOfRangeException>();
    }

    // ── MPS ───────────────────────────────────────────────────────────────

    [Fact]
    public void Mps_Default_PushesPpem()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, null, null);
        interp.HintGlyph(Glyph([0], [0], Mps));

        // The classic interpreter pushes the ppem (FreeType v35 behaviour).
        interp.StackSnapshot()[^1].Should().Be(16);
    }

    [Fact]
    public void Mps_WithMeasuredPointSize_PushesPointSize()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.MeasuredPointSize = 768;                 // 12 pt in 26.6
        interp.PrepareSize(16, UnitsPerEm, null, null);
        interp.HintGlyph(Glyph([0], [0], Mps));

        interp.StackSnapshot()[^1].Should().Be(768);
    }

    // ── MIRP hardening ────────────────────────────────────────────────────

    [Fact]
    public void Mirp_TwilightPoint_SeedsOriginalFromControlValue()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, Cvt100, null);
        interp.HintGlyph(Glyph([0], [0], MirpTwilightThenReadOriginal));

        // cvt 0 = 100 font units → 50 px. The twilight point's ORIGINAL is
        // seeded from rp0 (glyph point 0 at x = 0) plus the CVT distance
        // along the freedom vector: GC[original] reads 50.
        interp.StackSnapshot()[^1].Should().Be(50);
    }

    [Fact]
    public void Mirp_CutIn_AppliesWithinOneZone()
    {
        // Point 1 sits 400 font units (200 px) from rp0; the CVT says 50 px.
        // |50 − 200| = 150 exceeds the default cut-in (68), so the original
        // distance wins and rounds: 200 → 192.
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, Cvt100, null);
        Zone zone = interp.HintGlyph(Glyph([0, 400], [0, 0], MirpSameZoneRound));

        zone.CurrentX[1].Should().Be(192);
    }

    [Fact]
    public void Mirp_CutIn_SkippedAcrossZones()
    {
        // Same geometry, but zp0 is the twilight zone while zp1 is the glyph
        // zone — the cut-in test does not apply across zones (FreeType
        // Ins_MIRP), so the CVT distance is used directly: 50 → rounds to 64.
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, Cvt100, null);
        Zone zone = interp.HintGlyph(Glyph([0, 400], [0, 0], MirpCrossZoneRound));

        zone.CurrentX[1].Should().Be(64);
    }

    // ── Per-instruction single-width forms ────────────────────────────────

    [Fact]
    public void Mdrp_SingleWidth_PositiveDistanceInsideWindow_Snaps()
    {
        // Single width 100 fu → 50 px; cut-in 60. Original distance 128 fu →
        // 64 px lies inside (50 − 60, 50 + 60), so it snaps to +50.
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, null, null);
        Zone zone = interp.HintGlyph(Glyph([0, 128], [0, 0], MdrpWithSingleWidth));

        zone.CurrentX[1].Should().Be(50);
    }

    [Fact]
    public void Mdrp_SingleWidth_NegativeDistanceOutsideWindow_DoesNotSnap()
    {
        // FreeType's MDRP window sits around +single-width only: an original
        // distance of −64 px falls outside (−10, 110) and stays −64.
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, null, null);
        Zone zone = interp.HintGlyph(Glyph([0, -128], [0, 0], MdrpWithSingleWidth));

        zone.CurrentX[1].Should().Be(-64);
    }

    [Fact]
    public void Mirp_SingleWidth_SnapsCvtDistance()
    {
        // Single width 120 fu → 60 px; cut-in 30. The CVT distance 50 px lies
        // within 30 of 60, so MIRP uses 60; no-round keeps it exact.
        HintingInterpreter interp = NewInterpreter();
        interp.PrepareSize(16, UnitsPerEm, Cvt100, null);
        Zone zone = interp.HintGlyph(Glyph([0, 128], [0, 0], MirpWithSingleWidthNoRound));

        zone.CurrentX[1].Should().Be(60);
    }
}
