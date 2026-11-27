// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Type 2 Charstring Format §3.2 (subr nesting); Apple TrueType
//        Reference Manual (the instruction set)
// PHASE: 3.36.0 (D-410 B) — font-program guards: a CFF charstring whose
//        subroutines call each other without end stops at a nesting cap (c23),
//        and a hinting program caught in a runaway loop stops at a per-program
//        instruction budget. Without the guards these loop until the stack or
//        the clock gives out.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Fonts.Rendering.Hinting;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class CrashGuards336Tests
{
    [Fact]
    public void Cff_SelfCallingSubroutine_StopsAtTheNestingCap()
    {
        // Local subr 0 calls itself: push -107 (byte 32, since bias is 107 for a
        // single subr) then callsubr (op 10). The entry charstring calls subr 0.
        List<byte[]> local = new List<byte[]> { new byte[] { 32, 10 } };
        byte[] charstring = { 32, 10 };
        Type2Interpreter interpreter = new Type2Interpreter(new List<byte[]>(), local, 0, 0);

        Action run = () => interpreter.Run(charstring);

        run.Should().Throw<FontRenderingException>();
    }

    [Fact]
    public void Hinting_InfiniteJump_StopsAtTheInstructionBudget()
    {
        // PUSHW[0] -3 then JMPR: the jump is relative to the JMPR position, so
        // -3 lands back on the push — an endless two-instruction loop.
        byte[] program = { 0xB8, 0xFF, 0xFD, 0x1C };
        HintingInterpreter interpreter = new HintingInterpreter(HintingLimits.Default);

        Action run = () => interpreter.RunProgram(program);

        run.Should().Throw<FontRenderingException>();
    }
}
