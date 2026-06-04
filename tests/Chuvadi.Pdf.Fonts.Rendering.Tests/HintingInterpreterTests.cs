// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Apple TrueType Reference Manual — the instruction set
// PHASE: Phase 2 — TrueType bytecode hinting (Stage 2) tests

using System;
using Chuvadi.Pdf.Fonts.Rendering.Hinting;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

// ── HintingInterpreter ─────────────────────────────────────────────────────

public sealed class HintingInterpreterTests
{
    // Push family.
    private static readonly byte[] PushBProgram = [0xB2, 10, 20, 30];          // PUSHB[2] 10 20 30
    private static readonly byte[] PushWSignedProgram = [0xB8, 0xFF, 0xFF];     // PUSHW[0] -1
    private static readonly byte[] NPushBProgram = [0x40, 0x03, 1, 2, 3];       // NPUSHB 3
    private static readonly byte[] NPushWProgram = [0x41, 0x02, 0x00, 0x05, 0xFF, 0xFF]; // NPUSHW 2: 5, -1

    // Stack manipulation.
    private static readonly byte[] DupProgram = [0xB0, 7, 0x20];                // PUSHB[0] 7; DUP
    private static readonly byte[] PopProgram = [0xB1, 1, 2, 0x21];             // PUSHB[1] 1 2; POP
    private static readonly byte[] SwapProgram = [0xB1, 1, 2, 0x23];            // PUSHB[1] 1 2; SWAP
    private static readonly byte[] DepthProgram = [0xB1, 5, 6, 0x24];           // PUSHB[1] 5 6; DEPTH
    private static readonly byte[] CIndexProgram = [0xB2, 10, 20, 30, 0xB0, 3, 0x25]; // CINDEX 3
    private static readonly byte[] MIndexProgram = [0xB2, 10, 20, 30, 0xB0, 3, 0x26]; // MINDEX 3

    // Functions.
    private static readonly byte[] FDefProgram = [0xB0, 5, 0x2C, 0xB0, 99, 0x2D]; // FDEF 5 { PUSHB 99 }
    private static readonly byte[] CallProgram = [0xB0, 5, 0x2C, 0xB0, 99, 0x2D, 0xB0, 5, 0x2B]; // ...; CALL 5
    private static readonly byte[] LoopCallProgram = [0xB0, 5, 0x2C, 0xB0, 7, 0x2D, 0xB1, 3, 5, 0x2A]; // count 3, f 5, LOOPCALL

    // FDEF body whose PUSHW data bytes are 0x2D (the ENDF opcode value); the
    // body scanner must skip them rather than treat them as ENDF.
    private static readonly byte[] SkipInlineDataProgram =
        [0xB0, 0x01, 0x2C, 0xB8, 0x2D, 0x2D, 0x2D, 0xB0, 0x01, 0x2B];

    // Function 0 calls itself: must trip the call-depth guard, not stack-overflow.
    private static readonly byte[] RecursionProgram =
        [0xB0, 0, 0x2C, 0xB0, 0, 0x2B, 0x2D, 0xB0, 0, 0x2B];

    // IDEF for opcode 0x91 (unused) that pushes 42, then invokes 0x91.
    private static readonly byte[] IDefProgram = [0xB0, 0x91, 0x89, 0xB0, 42, 0x2D, 0x91];

    private static readonly int[] ExpectedPushB = [10, 20, 30];
    private static readonly int[] ExpectedPushWSigned = [-1];
    private static readonly int[] ExpectedNPushB = [1, 2, 3];
    private static readonly int[] ExpectedNPushW = [5, -1];
    private static readonly int[] ExpectedDup = [7, 7];
    private static readonly int[] ExpectedPop = [1];
    private static readonly int[] ExpectedSwap = [2, 1];
    private static readonly int[] ExpectedDepth = [5, 6, 2];
    private static readonly int[] ExpectedCIndex = [10, 20, 30, 10];
    private static readonly int[] ExpectedMIndex = [20, 30, 10];
    private static readonly int[] ExpectedCall = [99];
    private static readonly int[] ExpectedLoopCall = [7, 7, 7];
    private static readonly int[] ExpectedSkipInline = [0x2D2D];
    private static readonly int[] ExpectedIDef = [42];

    private static HintingInterpreter NewInterpreter()
    {
        return new HintingInterpreter(HintingLimits.Default);
    }

    [Fact]
    public void PushB_PushesUnsignedBytes()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(PushBProgram);
        interp.StackSnapshot().Should().Equal(ExpectedPushB);
    }

    [Fact]
    public void PushW_PushesSignedWords()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(PushWSignedProgram);
        interp.StackSnapshot().Should().Equal(ExpectedPushWSigned);
    }

    [Fact]
    public void NPushB_PushesCountedBytes()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(NPushBProgram);
        interp.StackSnapshot().Should().Equal(ExpectedNPushB);
    }

    [Fact]
    public void NPushW_PushesCountedSignedWords()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(NPushWProgram);
        interp.StackSnapshot().Should().Equal(ExpectedNPushW);
    }

    [Fact]
    public void Dup_DuplicatesTop()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(DupProgram);
        interp.StackSnapshot().Should().Equal(ExpectedDup);
    }

    [Fact]
    public void Pop_DiscardsTop()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(PopProgram);
        interp.StackSnapshot().Should().Equal(ExpectedPop);
    }

    [Fact]
    public void Swap_SwapsTopTwo()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(SwapProgram);
        interp.StackSnapshot().Should().Equal(ExpectedSwap);
    }

    [Fact]
    public void Depth_PushesStackDepth()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(DepthProgram);
        interp.StackSnapshot().Should().Equal(ExpectedDepth);
    }

    [Fact]
    public void CIndex_CopiesIndexedElementToTop()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(CIndexProgram);
        interp.StackSnapshot().Should().Equal(ExpectedCIndex);
    }

    [Fact]
    public void MIndex_MovesIndexedElementToTop()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(MIndexProgram);
        interp.StackSnapshot().Should().Equal(ExpectedMIndex);
    }

    [Fact]
    public void FDef_RegistersFunctionWithoutExecutingBody()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(FDefProgram);
        interp.IsFunctionDefined(5).Should().BeTrue();
        interp.IsFunctionDefined(4).Should().BeFalse();
        interp.StackDepth.Should().Be(0);
    }

    [Fact]
    public void Call_ExecutesFunctionBody()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(CallProgram);
        interp.StackSnapshot().Should().Equal(ExpectedCall);
    }

    [Fact]
    public void LoopCall_ExecutesFunctionRepeatedly()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(LoopCallProgram);
        interp.StackSnapshot().Should().Equal(ExpectedLoopCall);
    }

    [Fact]
    public void FDef_SkipsInlinePushDataWhenFindingEndf()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(SkipInlineDataProgram);
        interp.StackSnapshot().Should().Equal(ExpectedSkipInline);
    }

    [Fact]
    public void Call_SelfRecursion_TripsDepthGuard()
    {
        HintingInterpreter interp = NewInterpreter();
        Action act = () => interp.RunFontProgram(RecursionProgram);
        act.Should().Throw<FontRenderingException>();
    }

    [Fact]
    public void IDef_DefinesAndDispatchesCustomOpcode()
    {
        HintingInterpreter interp = NewInterpreter();
        interp.RunFontProgram(IDefProgram);
        interp.IsInstructionDefined(0x91).Should().BeTrue();
        interp.StackSnapshot().Should().Equal(ExpectedIDef);
    }

    [Fact]
    public void Constructor_SizesTablesFromLimits()
    {
        HintingLimits limits = new HintingLimits(
            maxFunctionDefs: 10,
            maxInstructionDefs: 2,
            maxStorage: 32,
            maxStackElements: 100,
            maxTwilightPoints: 8);
        HintingInterpreter interp = new HintingInterpreter(limits);
        interp.StorageSize.Should().Be(32);
        interp.Limits.MaxFunctionDefs.Should().Be(10);
    }

    [Fact]
    public void RunFontProgram_NullProgram_Throws()
    {
        HintingInterpreter interp = NewInterpreter();
        Action act = () => interp.RunFontProgram(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

// ── TrueTypeLoader.GetHintingLimits ────────────────────────────────────────

public sealed class TrueTypeLoaderHintingTests
{
    [Fact]
    public void GetHintingLimits_Maxp05Font_ReturnsDefaults()
    {
        byte[] font = TrueTypeLoaderTests.InvokeMinimalFont();
        TrueTypeLoader loader = new TrueTypeLoader(font);

        HintingLimits limits = loader.GetHintingLimits();

        limits.MaxFunctionDefs.Should().Be(HintingLimits.Default.MaxFunctionDefs);
        limits.MaxStorage.Should().Be(HintingLimits.Default.MaxStorage);
        limits.MaxStackElements.Should().Be(HintingLimits.Default.MaxStackElements);
    }
}
