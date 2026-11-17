// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  FreeType interpreter v40 backward compatibility (ttinterp.h)
// PHASE: 3.35.0 (D2, backlog 65b) — light hinting runs glyph programs with
//        no X moves, so their Y fitting matches the unhinted X positions.

using Chuvadi.Pdf.Fonts.Rendering.Hinting;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class BackwardCompatibility3350Tests
{
    private const int UnitsPerEm = 2048;

    // SVTCA[x]; PUSHB 0; MDAP[round]; SVTCA[y]; PUSHB 0; MDAP[round].
    private static readonly byte[] RoundBothAxes = [0x01, 0xB0, 0x00, 0x2F, 0x00, 0xB0, 0x00, 0x2F];

    // SVTCA[y]; PUSHB 0; MDAP[round]; IUP[y]; IUP[x]; SVTCA[y]; PUSHB 32,0; SHPIX.
    private static readonly byte[] ShiftAfterIup = [0x00, 0xB0, 0x00, 0x2F, 0x31, 0x30, 0x00, 0xB1, 0x00, 0x20, 0x38];

    // At 16 ppem a coordinate of 50 font units is 25 in 26.6; MDAP[round]
    // takes it to 0.
    private static readonly int[] Fifty = [50];

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 25, 0)]
    public void XMoves_AreSkipped_InBackwardCompatibility(bool backward, int expectedX, int expectedY)
    {
        HintingInterpreter interp = new HintingInterpreter(HintingLimits.Default);
        interp.PrepareSize(16, UnitsPerEm, null, null);
        interp.BackwardCompatibility = backward;

        Zone zone = interp.HintGlyph(new RawGlyph(Fifty, Fifty, [true], [0], RoundBothAxes, 1));

        zone.CurrentX[0].Should().Be(expectedX);
        zone.CurrentY[0].Should().Be(expectedY, "Y is fitted either way");
    }

    [Theory]
    [InlineData(false, 32)]
    [InlineData(true, 0)]
    public void NothingMovesInY_AfterBothIups_InBackwardCompatibility(bool backward, int expectedY)
    {
        HintingInterpreter interp = new HintingInterpreter(HintingLimits.Default);
        interp.PrepareSize(16, UnitsPerEm, null, null);
        interp.BackwardCompatibility = backward;

        Zone zone = interp.HintGlyph(new RawGlyph(Fifty, Fifty, [true], [0], ShiftAfterIup, 1));

        zone.CurrentY[0].Should().Be(expectedY);
    }
}
