// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Apple TrueType Reference Manual — the instruction set
//        https://learn.microsoft.com/typography/opentype/spec/tt_instructions
// PHASE: Phase 2 — TrueType bytecode hinting (Stage 2: VM skeleton)
// Operand stack, storage area, function/instruction definitions, and the
// push/stack/function-call opcode families. Enough to run a font program
// (fpgm) and register its functions. No vectors, rounding, or point movement
// yet — those arrive in later stages — and the interpreter never touches
// render output (RenderOptions.Hinting stays off until the final stage).

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting;

/// <summary>
/// Executes TrueType instruction bytecode. Stage 2 implements the program
/// execution loop, the operand stack, the storage area, function and
/// instruction definitions (FDEF/IDEF), and the push, stack-manipulation, and
/// call opcode families. Opcodes not yet implemented are skipped in a
/// length-aware manner so that a font program can be run and its functions
/// registered without error.
/// </summary>
/// <remarks>
/// Stack values are raw 32-bit integers. Operators interpret them as plain
/// integers or as F26Dot6 fixed-point distances depending on context; the
/// fixed-point-aware operators are added in later stages.
/// </remarks>
internal sealed class HintingInterpreter
{
    private const int MaxCallDepth = 128;

    // Opcodes used structurally by Stage 2.
    private const byte OpNPushB = 0x40;
    private const byte OpNPushW = 0x41;
    private const byte OpPushBFirst = 0xB0;
    private const byte OpPushBLast = 0xB7;
    private const byte OpPushWFirst = 0xB8;
    private const byte OpPushWLast = 0xBF;
    private const byte OpDup = 0x20;
    private const byte OpPop = 0x21;
    private const byte OpClear = 0x22;
    private const byte OpSwap = 0x23;
    private const byte OpDepth = 0x24;
    private const byte OpCIndex = 0x25;
    private const byte OpMIndex = 0x26;
    private const byte OpLoopCall = 0x2A;
    private const byte OpCall = 0x2B;
    private const byte OpFDef = 0x2C;
    private const byte OpEndF = 0x2D;
    private const byte OpIDef = 0x89;

    // Stage 3 — vector setters (axis-aligned) and round-state operators.
    private const byte OpSvtcaY = 0x00;
    private const byte OpSvtcaX = 0x01;
    private const byte OpSpvtcaY = 0x02;
    private const byte OpSpvtcaX = 0x03;
    private const byte OpSfvtcaY = 0x04;
    private const byte OpSfvtcaX = 0x05;
    private const byte OpRtg = 0x18;
    private const byte OpRthg = 0x19;
    private const byte OpRtdg = 0x3A;
    private const byte OpSround = 0x76;
    private const byte OpS45Round = 0x77;
    private const byte OpRoff = 0x7A;
    private const byte OpRutg = 0x7C;
    private const byte OpRdtg = 0x7D;

    // Grid period bases for SROUND/S45ROUND, in F26Dot6 (64 = 1 pixel).
    // S45ROUND rounds along the 45-degree diagonal: 1 pixel * sqrt(2)/2 ~= 45.25,
    // taken as 45. This single constant is the one rounding value worth
    // confirming against a reference during Stage 7 visual testing.
    private const int SRoundGridPeriod = 64;
    private const int S45RoundGridPeriod = 45;

    private readonly int[] _stack;
    private readonly int[] _storage;
    private readonly Dictionary<int, byte[]> _functions = new Dictionary<int, byte[]>();
    private readonly Dictionary<int, byte[]> _instructionDefs = new Dictionary<int, byte[]>();
    private readonly HintingLimits _limits;
    private int _sp;
    private int _callDepth;

    /// <summary>
    /// Initialises a <see cref="HintingInterpreter"/> with tables sized from the
    /// given limits.
    /// </summary>
    /// <param name="limits">The resource limits read from the font's <c>maxp</c> table.</param>
    internal HintingInterpreter(HintingLimits limits)
    {
        _limits = limits;
        _stack = new int[Math.Max(limits.MaxStackElements, 256)];
        _storage = new int[Math.Max(limits.MaxStorage, 0)];
        State = new GraphicsState();
    }

    /// <summary>The interpreter's graphics state.</summary>
    internal GraphicsState State { get; }

    /// <summary>The limits the interpreter was constructed with.</summary>
    internal HintingLimits Limits => _limits;

    /// <summary>The current operand-stack depth.</summary>
    internal int StackDepth => _sp;

    /// <summary>The number of storage-area locations.</summary>
    internal int StorageSize => _storage.Length;

    /// <summary>The number of functions defined so far.</summary>
    internal int DefinedFunctionCount => _functions.Count;

    /// <summary>Returns true when a function with the given number has been defined.</summary>
    /// <param name="functionNumber">The FDEF function number.</param>
    internal bool IsFunctionDefined(int functionNumber)
    {
        return _functions.ContainsKey(functionNumber);
    }

    /// <summary>Returns true when an instruction has been defined for the given opcode.</summary>
    /// <param name="opcode">The opcode an IDEF was installed for.</param>
    internal bool IsInstructionDefined(int opcode)
    {
        return _instructionDefs.ContainsKey(opcode);
    }

    /// <summary>Returns a copy of the operand stack from bottom to top.</summary>
    internal int[] StackSnapshot()
    {
        return _stack[0.._sp];
    }

    /// <summary>
    /// Runs the font program (<c>fpgm</c>), which typically consists of function
    /// definitions registered for later use. The operand stack is cleared first.
    /// </summary>
    /// <param name="fontProgram">The raw <c>fpgm</c> table bytes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fontProgram"/> is null.</exception>
    internal void RunFontProgram(byte[] fontProgram)
    {
        ArgumentNullException.ThrowIfNull(fontProgram);
        _sp = 0;
        Execute(fontProgram);
    }

    /// <summary>
    /// Executes an arbitrary instruction program against the current state
    /// without clearing the stack. Exposed for testing the VM with synthetic
    /// bytecode; production callers use <see cref="RunFontProgram"/> and the
    /// per-size programs added in later stages.
    /// </summary>
    /// <param name="program">The instruction bytes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="program"/> is null.</exception>
    internal void RunProgram(byte[] program)
    {
        ArgumentNullException.ThrowIfNull(program);
        Execute(program);
    }

    // ── Execution loop ────────────────────────────────────────────────────

    private void Execute(byte[] code)
    {
        int ip = 0;

        while (ip < code.Length)
        {
            byte op = code[ip];

            if (op == OpNPushB)
            {
                ip = PushNBytes(code, ip);
                continue;
            }

            if (op == OpNPushW)
            {
                ip = PushNWords(code, ip);
                continue;
            }

            if (op >= OpPushBFirst && op <= OpPushBLast)
            {
                ip = PushBytes(code, ip, op - OpPushBFirst + 1);
                continue;
            }

            if (op >= OpPushWFirst && op <= OpPushWLast)
            {
                ip = PushWords(code, ip, op - OpPushWFirst + 1);
                continue;
            }

            if (op == OpFDef)
            {
                ip = DefineFunction(code, ip);
                continue;
            }

            if (op == OpIDef)
            {
                ip = DefineInstruction(code, ip);
                continue;
            }

            if (op == OpEndF)
            {
                // End of a function or instruction body.
                return;
            }

            Dispatch(op);
            ip += 1;
        }
    }

    private void Dispatch(byte op)
    {
        switch (op)
        {
            case OpDup:
                Push(PeekTop());
                break;
            case OpPop:
                _ = Pop();
                break;
            case OpClear:
                _sp = 0;
                break;
            case OpSwap:
                Swap();
                break;
            case OpDepth:
                Push(_sp);
                break;
            case OpCIndex:
                CopyIndex();
                break;
            case OpMIndex:
                MoveIndex();
                break;
            case OpCall:
                CallFunction();
                break;
            case OpLoopCall:
                LoopCall();
                break;
            case OpSvtcaY:
                SetFreedomAndProjection(0, GraphicsState.One2Dot14);
                break;
            case OpSvtcaX:
                SetFreedomAndProjection(GraphicsState.One2Dot14, 0);
                break;
            case OpSpvtcaY:
                SetProjection(0, GraphicsState.One2Dot14);
                break;
            case OpSpvtcaX:
                SetProjection(GraphicsState.One2Dot14, 0);
                break;
            case OpSfvtcaY:
                SetFreedom(0, GraphicsState.One2Dot14);
                break;
            case OpSfvtcaX:
                SetFreedom(GraphicsState.One2Dot14, 0);
                break;
            case OpRtg:
                SetRound(RoundState.ToGrid, 64, 0, 32);
                break;
            case OpRthg:
                SetRound(RoundState.ToHalfGrid, 64, 32, 32);
                break;
            case OpRtdg:
                SetRound(RoundState.ToDoubleGrid, 32, 0, 16);
                break;
            case OpRdtg:
                SetRound(RoundState.DownToGrid, 64, 0, 0);
                break;
            case OpRutg:
                SetRound(RoundState.UpToGrid, 64, 0, 63);
                break;
            case OpRoff:
                SetRound(RoundState.Off, 64, 0, 32);
                break;
            case OpSround:
                SetSuperRound(RoundState.Super, SRoundGridPeriod, Pop());
                break;
            case OpS45Round:
                SetSuperRound(RoundState.Super45, S45RoundGridPeriod, Pop());
                break;
            default:
                // Either a custom instruction (IDEF) or an opcode not yet
                // implemented in this stage. Custom instructions run; anything
                // else is a no-op while the interpreter is inert.
                if (_instructionDefs.TryGetValue(op, out byte[]? body))
                {
                    CallBody(body);
                }

                break;
        }
    }

    // ── Function and instruction definitions ──────────────────────────────

    private int DefineFunction(byte[] code, int fdefIp)
    {
        int functionNumber = Pop();

        if (functionNumber < 0
            || (_limits.MaxFunctionDefs > 0 && functionNumber >= _limits.MaxFunctionDefs))
        {
            throw new FontRenderingException(
                $"FDEF function number {functionNumber} is out of range.");
        }

        int bodyStart = fdefIp + 1;
        int endf = FindEndf(code, bodyStart);
        _functions[functionNumber] = Slice(code, bodyStart, endf - bodyStart);
        return endf + 1;
    }

    private int DefineInstruction(byte[] code, int idefIp)
    {
        int opcode = Pop();

        if (opcode < 0 || opcode > 255)
        {
            throw new FontRenderingException(
                $"IDEF opcode {opcode} is out of range.");
        }

        int bodyStart = idefIp + 1;
        int endf = FindEndf(code, bodyStart);
        _instructionDefs[opcode] = Slice(code, bodyStart, endf - bodyStart);
        return endf + 1;
    }

    private void CallFunction()
    {
        int functionNumber = Pop();
        CallBody(GetFunction(functionNumber));
    }

    private void LoopCall()
    {
        int functionNumber = Pop();
        int count = Pop();
        byte[] body = GetFunction(functionNumber);

        for (int i = 0; i < count; i++)
        {
            CallBody(body);
        }
    }

    private byte[] GetFunction(int functionNumber)
    {
        if (!_functions.TryGetValue(functionNumber, out byte[]? body))
        {
            throw new FontRenderingException(
                $"Call to undefined function {functionNumber}.");
        }

        return body;
    }

    private void CallBody(byte[] body)
    {
        _callDepth += 1;

        if (_callDepth > MaxCallDepth)
        {
            _callDepth -= 1;
            throw new FontRenderingException("Hinting call stack depth exceeded.");
        }

        try
        {
            Execute(body);
        }
        finally
        {
            _callDepth -= 1;
        }
    }

    // Scans from the given position to the next ENDF, advancing instruction by
    // instruction so that variable-length push data is skipped rather than
    // misread as an ENDF opcode.
    private static int FindEndf(byte[] code, int start)
    {
        int pos = start;

        while (pos < code.Length)
        {
            if (code[pos] == OpEndF)
            {
                return pos;
            }

            pos += InstructionLength(code, pos);
        }

        throw new FontRenderingException("FDEF or IDEF without a matching ENDF.");
    }

    private static int InstructionLength(byte[] code, int pos)
    {
        byte op = code[pos];

        if (op == OpNPushB)
        {
            if (pos + 1 >= code.Length)
            {
                return code.Length - pos;
            }

            return 2 + code[pos + 1];
        }

        if (op == OpNPushW)
        {
            if (pos + 1 >= code.Length)
            {
                return code.Length - pos;
            }

            return 2 + (2 * code[pos + 1]);
        }

        if (op >= OpPushBFirst && op <= OpPushBLast)
        {
            return 1 + (op - OpPushBFirst + 1);
        }

        if (op >= OpPushWFirst && op <= OpPushWLast)
        {
            return 1 + (2 * (op - OpPushWFirst + 1));
        }

        return 1;
    }

    private static byte[] Slice(byte[] code, int start, int length)
    {
        byte[] result = new byte[length];
        Array.Copy(code, start, result, 0, length);
        return result;
    }

    // ── Push family ───────────────────────────────────────────────────────

    private int PushNBytes(byte[] code, int ip)
    {
        int n = (ip + 1 < code.Length) ? code[ip + 1] : 0;
        int pos = ip + 2;

        for (int i = 0; i < n && pos < code.Length; i++)
        {
            Push(code[pos]);
            pos += 1;
        }

        return pos;
    }

    private int PushNWords(byte[] code, int ip)
    {
        int n = (ip + 1 < code.Length) ? code[ip + 1] : 0;
        int pos = ip + 2;

        for (int i = 0; i < n && pos + 1 < code.Length; i++)
        {
            Push((short)((code[pos] << 8) | code[pos + 1]));
            pos += 2;
        }

        return pos;
    }

    private int PushBytes(byte[] code, int ip, int count)
    {
        int pos = ip + 1;

        for (int i = 0; i < count && pos < code.Length; i++)
        {
            Push(code[pos]);
            pos += 1;
        }

        return pos;
    }

    private int PushWords(byte[] code, int ip, int count)
    {
        int pos = ip + 1;

        for (int i = 0; i < count && pos + 1 < code.Length; i++)
        {
            Push((short)((code[pos] << 8) | code[pos + 1]));
            pos += 2;
        }

        return pos;
    }

    // ── Stack primitives ──────────────────────────────────────────────────

    private void Push(int value)
    {
        if (_sp >= _stack.Length)
        {
            throw new FontRenderingException("Hinting operand stack overflow.");
        }

        _stack[_sp] = value;
        _sp += 1;
    }

    private int Pop()
    {
        if (_sp <= 0)
        {
            throw new FontRenderingException("Hinting operand stack underflow.");
        }

        _sp -= 1;
        return _stack[_sp];
    }

    private int PeekTop()
    {
        if (_sp <= 0)
        {
            throw new FontRenderingException("Hinting operand stack underflow.");
        }

        return _stack[_sp - 1];
    }

    private void Swap()
    {
        if (_sp < 2)
        {
            throw new FontRenderingException("SWAP requires two stack elements.");
        }

        (_stack[_sp - 1], _stack[_sp - 2]) = (_stack[_sp - 2], _stack[_sp - 1]);
    }

    private void CopyIndex()
    {
        int k = Pop();

        if (k < 1 || k > _sp)
        {
            throw new FontRenderingException("CINDEX index out of range.");
        }

        Push(_stack[_sp - k]);
    }

    private void MoveIndex()
    {
        int k = Pop();

        if (k < 1 || k > _sp)
        {
            throw new FontRenderingException("MINDEX index out of range.");
        }

        int index = _sp - k;
        int value = _stack[index];

        for (int i = index; i < _sp - 1; i++)
        {
            _stack[i] = _stack[i + 1];
        }

        _stack[_sp - 1] = value;
    }

    // ── Vector setters (Stage 3) ──────────────────────────────────────────

    private void SetProjection(int x, int y)
    {
        State.ProjectionVectorX = x;
        State.ProjectionVectorY = y;

        // SPVTCA also sets the dual projection vector to the same axis.
        State.DualProjectionVectorX = x;
        State.DualProjectionVectorY = y;
    }

    private void SetFreedom(int x, int y)
    {
        State.FreedomVectorX = x;
        State.FreedomVectorY = y;
    }

    private void SetFreedomAndProjection(int x, int y)
    {
        SetProjection(x, y);
        SetFreedom(x, y);
    }

    // ── Rounding (Stage 3) ────────────────────────────────────────────────

    private void SetRound(RoundState state, int period, int phase, int threshold)
    {
        State.RoundState = state;
        State.RoundPeriod = period;
        State.RoundPhase = phase;
        State.RoundThreshold = threshold;
    }

    // Decodes an SROUND/S45ROUND selector byte into period, phase, and
    // threshold, following the TrueType super-round specification.
    private void SetSuperRound(RoundState state, int gridPeriod, int selector)
    {
        int period;
        switch (selector & 0xC0)
        {
            case 0x00:
                period = gridPeriod / 2;
                break;
            case 0x80:
                period = gridPeriod * 2;
                break;
            default:
                // 0x40 (one grid period) and 0xC0 (reserved) both use the grid.
                period = gridPeriod;
                break;
        }

        int phase;
        switch (selector & 0x30)
        {
            case 0x10:
                phase = period / 4;
                break;
            case 0x20:
                phase = period / 2;
                break;
            case 0x30:
                phase = (period * 3) / 4;
                break;
            default:
                phase = 0;
                break;
        }

        int threshold;
        if ((selector & 0x0F) == 0)
        {
            threshold = period - 1;
        }
        else
        {
            threshold = (((selector & 0x0F) - 4) * period) / 8;
        }

        State.RoundState = state;
        State.RoundPeriod = period;
        State.RoundPhase = phase;
        State.RoundThreshold = threshold;
    }

    /// <summary>
    /// Rounds an engine distance (F26Dot6) under the current round state,
    /// applying the given engine compensation. Implements the TrueType
    /// super-round formula via floor-to-multiple, which is correct for any
    /// period (including S45ROUND's non-power-of-two grid). Used by the point
    /// movement instructions added in a later stage.
    /// </summary>
    /// <param name="distance">The distance to round, in F26Dot6.</param>
    /// <param name="compensation">The engine compensation, in F26Dot6.</param>
    /// <returns>The rounded distance, in F26Dot6.</returns>
    internal int Round(int distance, int compensation)
    {
        if (State.RoundState == RoundState.Off)
        {
            if (distance >= 0)
            {
                int raw = distance + compensation;
                return raw < 0 ? 0 : raw;
            }
            else
            {
                int raw = distance - compensation;
                return raw > 0 ? 0 : raw;
            }
        }

        int period = State.RoundPeriod;
        int phase = State.RoundPhase;
        int threshold = State.RoundThreshold;

        if (distance >= 0)
        {
            int val = FloorToMultiple(distance - phase + threshold + compensation, period) + phase;
            return val < 0 ? phase : val;
        }
        else
        {
            int val = -FloorToMultiple(threshold - phase - distance + compensation, period) - phase;
            return val > 0 ? -phase : val;
        }
    }

    // Floors a value to the nearest lower multiple of period (toward negative
    // infinity), valid for any positive period.
    private static int FloorToMultiple(int value, int period)
    {
        if (period <= 0)
        {
            return value;
        }

        int quotient = value / period;

        if (value % period != 0 && value < 0)
        {
            quotient -= 1;
        }

        return quotient * period;
    }
}
