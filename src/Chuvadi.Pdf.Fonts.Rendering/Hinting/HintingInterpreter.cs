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

    // Stage 4 — vector-to-line setters (flag bit 0 selects perpendicular).
    private const byte OpSpvtlParallel = 0x06;
    private const byte OpSpvtlPerp = 0x07;
    private const byte OpSfvtlParallel = 0x08;
    private const byte OpSfvtlPerp = 0x09;
    private const byte OpSdpvtlParallel = 0x86;
    private const byte OpSdpvtlPerp = 0x87;

    // Reference points, zone pointers, loop, and state setters.
    private const byte OpSrp0 = 0x10;
    private const byte OpSrp1 = 0x11;
    private const byte OpSrp2 = 0x12;
    private const byte OpSzp0 = 0x13;
    private const byte OpSzp1 = 0x14;
    private const byte OpSzp2 = 0x15;
    private const byte OpSzps = 0x16;
    private const byte OpSloop = 0x17;
    private const byte OpSmd = 0x1A;
    private const byte OpScvtci = 0x1D;
    private const byte OpSswci = 0x1E;
    private const byte OpSsw = 0x1F;
    private const byte OpFlipOn = 0x4D;
    private const byte OpFlipOff = 0x4E;
    private const byte OpSdb = 0x5E;
    private const byte OpSds = 0x5F;
    private const byte OpScanctrl = 0x85;
    private const byte OpScantype = 0x8D;
    private const byte OpInstctrl = 0x8E;

    // Control Value Table access.
    private const byte OpWcvtp = 0x44;
    private const byte OpRcvt = 0x45;
    private const byte OpWcvtf = 0x70;

    // Measurement.
    private const byte OpGcCurrent = 0x46;
    private const byte OpGcOriginal = 0x47;
    private const byte OpScfs = 0x48;
    private const byte OpMdCurrent = 0x49;
    private const byte OpMdOriginal = 0x4A;
    private const byte OpMppem = 0x4B;
    private const byte OpMps = 0x4C;

    // Absolute movement.
    private const byte OpMdap = 0x2E;
    private const byte OpMdapRound = 0x2F;
    private const byte OpMiap = 0x3E;
    private const byte OpMiapRound = 0x3F;

    // Relative movement — opcode ranges, decoded by their flag bits in the
    // dispatch default arm.
    private const byte OpMdrpLow = 0xC0;
    private const byte OpMdrpHigh = 0xDF;
    private const byte OpMirpLow = 0xE0;
    private const byte OpMirpHigh = 0xFF;

    // Stage 5/6 — arithmetic and logical.
    private const byte OpAdd = 0x60;
    private const byte OpSub = 0x61;
    private const byte OpDiv = 0x62;
    private const byte OpMul = 0x63;
    private const byte OpAbs = 0x64;
    private const byte OpNeg = 0x65;
    private const byte OpFloor = 0x66;
    private const byte OpCeiling = 0x67;
    private const byte OpMax = 0x8B;
    private const byte OpMin = 0x8C;
    private const byte OpAnd = 0x5A;
    private const byte OpOr = 0x5B;
    private const byte OpNot = 0x5C;
    private const byte OpEq = 0x54;
    private const byte OpNeq = 0x55;
    private const byte OpGt = 0x52;
    private const byte OpGteq = 0x53;
    private const byte OpLt = 0x50;
    private const byte OpLteq = 0x51;
    private const byte OpOdd = 0x56;
    private const byte OpEven = 0x57;

    // Stage 5/6 — storage area.
    private const byte OpRs = 0x43;
    private const byte OpWs = 0x42;

    // Stage 5/6 — rounding by stack value (ranges; engine round/no-round).
    private const byte OpRoundLow = 0x68;
    private const byte OpRoundHigh = 0x6B;
    private const byte OpNroundLow = 0x6C;
    private const byte OpNroundHigh = 0x6F;

    // Stage 5/6 — flow control (handled in the execution loop, not Dispatch).
    private const byte OpIf = 0x58;
    private const byte OpElse = 0x1B;
    private const byte OpEif = 0x59;
    private const byte OpJmpr = 0x1C;
    private const byte OpJrot = 0x78;
    private const byte OpJrof = 0x79;

    // Stage 5/6 — DELTA exceptions.
    private const byte OpDeltaP1 = 0x5D;
    private const byte OpDeltaP2 = 0x71;
    private const byte OpDeltaP3 = 0x72;
    private const byte OpDeltaC1 = 0x73;
    private const byte OpDeltaC2 = 0x74;
    private const byte OpDeltaC3 = 0x75;

    // Stage 5/6 — shift, interpolation, and alignment.
    private const byte OpShpRp2 = 0x32;     // SHP[0] — uses rp2 in zp1
    private const byte OpShpRp1 = 0x33;     // SHP[1] — uses rp1 in zp0
    private const byte OpShc0 = 0x34;       // SHC[0] — shift contour by rp2 (zp1)
    private const byte OpShc1 = 0x35;       // SHC[1] — shift contour by rp1 (zp0)
    private const byte OpShz0 = 0x36;       // SHZ[0] — shift zone by rp2 (zp1)
    private const byte OpShz1 = 0x37;       // SHZ[1] — shift zone by rp1 (zp0)
    private const byte OpShpix = 0x38;
    private const byte OpIp = 0x39;
    private const byte OpIsect = 0x0F;
    private const byte OpIupX = 0x30;       // IUP[0] — x direction
    private const byte OpIupY = 0x31;       // IUP[1] — y direction
    private const byte OpAlignRp = 0x3C;
    private const byte OpAlignPts = 0x27;
    private const byte OpUtp = 0x29;

    // Stage 5/6 — environment query and miscellany.
    private const byte OpGetInfo = 0x88;
    private const byte OpRoll = 0x8A;

    // Move-op flag bits (shared by MDRP and MIRP).
    private const byte MoveFlagSetRp0 = 0x10;
    private const byte MoveFlagMinDistance = 0x08;
    private const byte MoveFlagRound = 0x04;

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

    // Stage 4 — per-size state (set by PrepareSize) and per-glyph state (set by
    // HintGlyph). The scale is 16.16 device-units-per-font-unit.
    private Zone? _twilightZone;
    private Zone? _glyphZone;
    private int[] _controlValues = Array.Empty<int>();
    private int _ppem;
    private int _scale;
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

            if (op == OpIf)
            {
                ip = ExecuteIf(code, ip);
                continue;
            }

            if (op == OpElse)
            {
                // Reached only by falling through the taken THEN branch; skip to
                // the matching EIF.
                ip = SkipToMatchingEif(code, ip + 1);
                continue;
            }

            if (op == OpEif)
            {
                // A balancing EIF for a taken branch: nothing to do.
                ip += 1;
                continue;
            }

            if (op == OpJmpr)
            {
                ip += Pop();
                continue;
            }

            if (op == OpJrot)
            {
                int condition = Pop();
                int offset = Pop();
                ip = condition != 0 ? ip + offset : ip + 1;
                continue;
            }

            if (op == OpJrof)
            {
                int condition = Pop();
                int offset = Pop();
                ip = condition == 0 ? ip + offset : ip + 1;
                continue;
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
            case OpSpvtlParallel:
                SetVectorToLine(projection: true, perpendicular: false);
                break;
            case OpSpvtlPerp:
                SetVectorToLine(projection: true, perpendicular: true);
                break;
            case OpSfvtlParallel:
                SetVectorToLine(projection: false, perpendicular: false);
                break;
            case OpSfvtlPerp:
                SetVectorToLine(projection: false, perpendicular: true);
                break;
            case OpSdpvtlParallel:
                SetDualVectorToLine(perpendicular: false);
                break;
            case OpSdpvtlPerp:
                SetDualVectorToLine(perpendicular: true);
                break;
            case OpSrp0:
                State.Rp0 = Pop();
                break;
            case OpSrp1:
                State.Rp1 = Pop();
                break;
            case OpSrp2:
                State.Rp2 = Pop();
                break;
            case OpSzp0:
                State.Zp0 = Pop();
                break;
            case OpSzp1:
                State.Zp1 = Pop();
                break;
            case OpSzp2:
                State.Zp2 = Pop();
                break;
            case OpSzps:
                {
                    int zone = Pop();
                    State.Zp0 = zone;
                    State.Zp1 = zone;
                    State.Zp2 = zone;
                    break;
                }

            case OpSloop:
                State.Loop = Pop();
                break;
            case OpSmd:
                State.MinimumDistance = Pop();
                break;
            case OpScvtci:
                State.ControlValueCutIn = Pop();
                break;
            case OpSswci:
                State.SingleWidthCutIn = Pop();
                break;
            case OpSsw:
                State.SingleWidthValue = Pop();
                break;
            case OpFlipOn:
                State.AutoFlip = true;
                break;
            case OpFlipOff:
                State.AutoFlip = false;
                break;
            case OpSdb:
                State.DeltaBase = Pop();
                break;
            case OpSds:
                State.DeltaShift = Pop();
                break;
            case OpScanctrl:
                State.ScanControl = Pop();
                break;
            case OpScantype:
                State.ScanType = Pop();
                break;
            case OpInstctrl:
                {
                    int selector = Pop();
                    int value = Pop();
                    ApplyInstructControl(selector, value);
                    break;
                }

            case OpRcvt:
                Push(GetControlValue(Pop()));
                break;
            case OpWcvtp:
                {
                    int value = Pop();
                    int location = Pop();
                    SetControlValue(location, value);
                    break;
                }

            case OpWcvtf:
                {
                    int funits = Pop();
                    int location = Pop();
                    SetControlValue(location, F26Dot6.MulFix(funits, _scale));
                    break;
                }

            case OpMppem:
                Push(_ppem);
                break;
            case OpMps:
                Push(_ppem);
                break;
            case OpGcCurrent:
                GetCoordinate(useDual: false);
                break;
            case OpGcOriginal:
                GetCoordinate(useDual: true);
                break;
            case OpScfs:
                SetCoordinateFromStack();
                break;
            case OpMdCurrent:
                MeasureDistance(useOriginal: false);
                break;
            case OpMdOriginal:
                MeasureDistance(useOriginal: true);
                break;
            case OpMdap:
                MoveDirectAbsolute(round: false);
                break;
            case OpMdapRound:
                MoveDirectAbsolute(round: true);
                break;
            case OpMiap:
                MoveIndirectAbsolute(round: false);
                break;
            case OpMiapRound:
                MoveIndirectAbsolute(round: true);
                break;

            // ── Arithmetic and logical (Stage 5/6) ──
            case OpAdd:
                BinaryOp(static (a, b) => a + b);
                break;
            case OpSub:
                BinaryOp(static (a, b) => a - b);
                break;
            case OpMul:
                BinaryOp(static (a, b) => F26Dot6.Mul(a, b));
                break;
            case OpDiv:
                BinaryOp(static (a, b) => F26Dot6.Div(a, b));
                break;
            case OpAbs:
                Push(Math.Abs(Pop()));
                break;
            case OpNeg:
                Push(-Pop());
                break;
            case OpFloor:
                Push(F26Dot6.Floor(Pop()));
                break;
            case OpCeiling:
                Push(F26Dot6.Ceiling(Pop()));
                break;
            case OpMax:
                BinaryOp(Math.Max);
                break;
            case OpMin:
                BinaryOp(Math.Min);
                break;
            case OpAnd:
                BinaryOp(static (a, b) => (a != 0 && b != 0) ? 1 : 0);
                break;
            case OpOr:
                BinaryOp(static (a, b) => (a != 0 || b != 0) ? 1 : 0);
                break;
            case OpNot:
                Push(Pop() == 0 ? 1 : 0);
                break;
            case OpEq:
                BinaryOp(static (a, b) => a == b ? 1 : 0);
                break;
            case OpNeq:
                BinaryOp(static (a, b) => a != b ? 1 : 0);
                break;
            case OpGt:
                BinaryOp(static (a, b) => a > b ? 1 : 0);
                break;
            case OpGteq:
                BinaryOp(static (a, b) => a >= b ? 1 : 0);
                break;
            case OpLt:
                BinaryOp(static (a, b) => a < b ? 1 : 0);
                break;
            case OpLteq:
                BinaryOp(static (a, b) => a <= b ? 1 : 0);
                break;
            case OpOdd:
                Push((Round(Pop(), 0) / F26Dot6.One) % 2 != 0 ? 1 : 0);
                break;
            case OpEven:
                Push((Round(Pop(), 0) / F26Dot6.One) % 2 == 0 ? 1 : 0);
                break;
            case OpRoll:
                Roll();
                break;

            // ── Storage area (Stage 5/6) ──
            case OpRs:
                Push(ReadStorage(Pop()));
                break;
            case OpWs:
                {
                    int value = Pop();
                    int location = Pop();
                    WriteStorage(location, value);
                    break;
                }

            // ── DELTA exceptions (Stage 5/6) ──
            case OpDeltaP1:
                ApplyDeltaP(0);
                break;
            case OpDeltaP2:
                ApplyDeltaP(16);
                break;
            case OpDeltaP3:
                ApplyDeltaP(32);
                break;
            case OpDeltaC1:
                ApplyDeltaC(0);
                break;
            case OpDeltaC2:
                ApplyDeltaC(16);
                break;
            case OpDeltaC3:
                ApplyDeltaC(32);
                break;

            // ── Shift, interpolation, alignment (Stage 5/6) ──
            case OpShpRp2:
                ShiftPoints(useRp1: false);
                break;
            case OpShpRp1:
                ShiftPoints(useRp1: true);
                break;
            case OpShc0:
                ShiftContour(useRp1: false);
                break;
            case OpShc1:
                ShiftContour(useRp1: true);
                break;
            case OpShz0:
                ShiftZone(useRp1: false);
                break;
            case OpShz1:
                ShiftZone(useRp1: true);
                break;
            case OpShpix:
                ShiftByPixels();
                break;
            case OpIp:
                InterpolatePoints();
                break;
            case OpIsect:
                Intersect();
                break;
            case OpIupX:
                InterpolateUntouched(yDirection: false);
                break;
            case OpIupY:
                InterpolateUntouched(yDirection: true);
                break;
            case OpAlignRp:
                AlignToReferencePoint();
                break;
            case OpAlignPts:
                AlignPoints();
                break;
            case OpUtp:
                UntouchPoint();
                break;

            // ── Environment query (Stage 5/6) ──
            case OpGetInfo:
                GetInformation();
                break;
            default:
                // A relative-move opcode, a round/no-round opcode (decoded by
                // flag bits), a custom instruction (IDEF), or an opcode not yet
                // implemented in this stage. Unimplemented opcodes are a no-op
                // while the interpreter is inert.
                if (op >= OpMdrpLow && op <= OpMdrpHigh)
                {
                    MoveDirectRelative(op);
                }
                else if (op >= OpMirpLow && op <= OpMirpHigh)
                {
                    MoveIndirectRelative(op);
                }
                else if (op >= OpRoundLow && op <= OpRoundHigh)
                {
                    Push(Round(Pop(), 0));
                }
                else if (op >= OpNroundLow && op <= OpNroundHigh)
                {
                    // No-round: the spec applies engine compensation only; with
                    // zero compensation this is the identity.
                    Push(Pop());
                }
                else if (_instructionDefs.TryGetValue(op, out byte[]? body))
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

    // ── Flow control (Stage 5/6) ──────────────────────────────────────────

    // Handles IF: pops the condition, executes the THEN branch if true, or skips
    // to the matching ELSE/EIF if false. Returns the next instruction pointer.
    private int ExecuteIf(byte[] code, int ip)
    {
        int condition = Pop();
        if (condition != 0)
        {
            return ip + 1;
        }

        return SkipToElseOrEif(code, ip + 1);
    }

    // Scans forward from a false IF to its matching ELSE (returning the position
    // after it, so the ELSE branch runs) or matching EIF (returning the position
    // after it). Nested IF blocks and inline push data are respected.
    private static int SkipToElseOrEif(byte[] code, int pos)
    {
        int depth = 0;
        while (pos < code.Length)
        {
            byte op = code[pos];
            if (op == OpIf)
            {
                depth += 1;
                pos += 1;
                continue;
            }

            if (op == OpEif)
            {
                if (depth == 0)
                {
                    return pos + 1;
                }

                depth -= 1;
                pos += 1;
                continue;
            }

            if (op == OpElse && depth == 0)
            {
                return pos + 1;
            }

            pos += InstructionLength(code, pos);
        }

        return pos;
    }

    // Scans forward from a taken THEN branch's ELSE to the matching EIF,
    // skipping the ELSE branch. Returns the position after the EIF.
    private static int SkipToMatchingEif(byte[] code, int pos)
    {
        int depth = 0;
        while (pos < code.Length)
        {
            byte op = code[pos];
            if (op == OpIf)
            {
                depth += 1;
                pos += 1;
                continue;
            }

            if (op == OpEif)
            {
                if (depth == 0)
                {
                    return pos + 1;
                }

                depth -= 1;
                pos += 1;
                continue;
            }

            pos += InstructionLength(code, pos);
        }

        return pos;
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

    // ── Sizing and glyph hinting (Stage 4) ────────────────────────────────

    /// <summary>
    /// Prepares the interpreter for a specific size: computes the font-unit to
    /// 26.6 scale, scales the Control Value Table, allocates the twilight zone,
    /// resets the graphics state, and runs the control-value program
    /// (<c>prep</c>) once for this size.
    /// </summary>
    /// <param name="ppem">The size in pixels per em.</param>
    /// <param name="unitsPerEm">The font's units-per-em from the <c>head</c> table.</param>
    /// <param name="controlValueTable">Raw <c>cvt </c> table bytes (big-endian int16 entries), or null.</param>
    /// <param name="controlValueProgram">Raw <c>prep</c> program bytes, or null.</param>
    internal void PrepareSize(int ppem, int unitsPerEm, byte[]? controlValueTable, byte[]? controlValueProgram)
    {
        if (unitsPerEm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitsPerEm));
        }

        _ppem = ppem;
        _scale = (int)(((long)ppem * F26Dot6.One * 0x10000) / unitsPerEm);
        _controlValues = ScaleControlValueTable(controlValueTable);
        _twilightZone = Zone.CreateTwilight(Math.Max(_limits.MaxTwilightPoints, 0));
        State.Reset();

        if (controlValueProgram is { Length: > 0 })
        {
            RunProgram(controlValueProgram);
        }
    }

    /// <summary>
    /// Hints a single glyph at the prepared size: builds the glyph zone by
    /// scaling the raw outline to 26.6, resets the graphics state, runs the
    /// glyph's instruction stream, and returns the fitted zone.
    /// <see cref="PrepareSize"/> must be called first.
    /// </summary>
    /// <param name="glyph">The raw glyph outline in font units (phantom points appended).</param>
    /// <returns>The glyph zone, with grid-fitted coordinates in <see cref="Zone.CurrentX"/>/<see cref="Zone.CurrentY"/>.</returns>
    internal Zone HintGlyph(RawGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);

        int count = glyph.PointCount;
        Zone zone = new Zone(count, glyph.ContourEnds, glyph.OnCurve);
        for (int i = 0; i < count; i++)
        {
            int x = F26Dot6.MulFix(glyph.X[i], _scale);
            int y = F26Dot6.MulFix(glyph.Y[i], _scale);
            zone.CurrentX[i] = x;
            zone.OriginalX[i] = x;
            zone.CurrentY[i] = y;
            zone.OriginalY[i] = y;
        }

        _glyphZone = zone;

        // The graphics state resets to its defaults before each glyph program;
        // the scaled Control Value Table modified by prep persists.
        State.Reset();

        if (glyph.Instructions is { Length: > 0 })
        {
            RunProgram(glyph.Instructions);
        }

        return zone;
    }

    // Parses big-endian int16 CVT entries (font units) and scales them to 26.6.
    private int[] ScaleControlValueTable(byte[]? table)
    {
        if (table is null || table.Length < 2)
        {
            return Array.Empty<int>();
        }

        int count = table.Length / 2;
        int[] values = new int[count];
        for (int i = 0; i < count; i++)
        {
            short funits = (short)((table[i * 2] << 8) | table[(i * 2) + 1]);
            values[i] = F26Dot6.MulFix(funits, _scale);
        }

        return values;
    }

    private int GetControlValue(int index)
    {
        return index >= 0 && index < _controlValues.Length ? _controlValues[index] : 0;
    }

    private void SetControlValue(int index, int value)
    {
        if (index >= 0 && index < _controlValues.Length)
        {
            _controlValues[index] = value;
        }
    }

    // Resolves a zone pointer (0 = twilight, otherwise glyph) to its zone.
    private Zone ZoneFor(int zonePointer)
    {
        if (zonePointer == 0)
        {
            return _twilightZone ?? throw new InvalidOperationException(
                "PrepareSize must be called before the twilight zone is used.");
        }

        return _glyphZone ?? throw new InvalidOperationException(
            "HintGlyph must be called before the glyph zone is used.");
    }

    // ── Projection, freedom, and point movement (Stage 4) ─────────────────

    // Projects a coordinate delta (26.6) onto the projection vector, returning
    // a signed distance in 26.6.
    private int Project(int dx, int dy)
    {
        return F2Dot14.Dot(dx, dy, State.ProjectionVectorX, State.ProjectionVectorY);
    }

    // Projects a coordinate delta (26.6) onto the dual projection vector, used
    // to measure distances in original (unhinted) coordinates.
    private int DualProject(int dx, int dy)
    {
        return F2Dot14.Dot(dx, dy, State.DualProjectionVectorX, State.DualProjectionVectorY);
    }

    // freedom · projection (2.14): the factor relating a projected distance to
    // the movement required along the freedom vector.
    private int FreedomDotProjection()
    {
        return F2Dot14.Dot(
            State.FreedomVectorX, State.FreedomVectorY,
            State.ProjectionVectorX, State.ProjectionVectorY);
    }

    // Moves a point along the freedom vector so that its projection onto the
    // projection vector changes by `distance` (26.6), marking the point touched
    // on each axis it moves along.
    private void MovePoint(Zone zone, int point, int distance)
    {
        if (point < 0 || point >= zone.PointCount)
        {
            return;
        }

        int fdotp = FreedomDotProjection();
        if (fdotp == 0)
        {
            return;
        }

        if (State.FreedomVectorX != 0)
        {
            zone.CurrentX[point] += MulDiv(distance, State.FreedomVectorX, fdotp);
            zone.TouchedX[point] = true;
        }

        if (State.FreedomVectorY != 0)
        {
            zone.CurrentY[point] += MulDiv(distance, State.FreedomVectorY, fdotp);
            zone.TouchedY[point] = true;
        }
    }

    // (a * b) / c, rounded half away from zero, guarding divide-by-zero.
    private static int MulDiv(int a, int b, int c)
    {
        if (c == 0)
        {
            return 0;
        }

        long numerator = (long)a * b;
        long absNum = numerator < 0 ? -numerator : numerator;
        long absDen = c < 0 ? -(long)c : c;
        long magnitude = (absNum + (absDen / 2)) / absDen;
        bool negative = (numerator < 0) ^ (c < 0);
        return (int)(negative ? -magnitude : magnitude);
    }

    private static int Sign(int value)
    {
        return value > 0 ? 1 : (value < 0 ? -1 : 0);
    }

    // ── Measurement (Stage 4) ─────────────────────────────────────────────

    // GC[a]: push the coordinate of a point (zp2) projected onto the projection
    // vector (a = 0, current) or the dual projection vector (a = 1, original).
    private void GetCoordinate(bool useDual)
    {
        int point = Pop();
        Zone zone = ZoneFor(State.Zp2);
        if (point < 0 || point >= zone.PointCount)
        {
            Push(0);
            return;
        }

        int value = useDual
            ? DualProject(zone.OriginalX[point], zone.OriginalY[point])
            : Project(zone.CurrentX[point], zone.CurrentY[point]);
        Push(value);
    }

    // SCFS: move a point (zp2) so its projection equals the value on the stack.
    private void SetCoordinateFromStack()
    {
        int value = Pop();
        int point = Pop();
        Zone zone = ZoneFor(State.Zp2);
        if (point < 0 || point >= zone.PointCount)
        {
            return;
        }

        int current = Project(zone.CurrentX[point], zone.CurrentY[point]);
        MovePoint(zone, point, value - current);

        // Setting a twilight point's coordinate also fixes its original.
        if (State.Zp2 == 0)
        {
            zone.OriginalX[point] = zone.CurrentX[point];
            zone.OriginalY[point] = zone.CurrentY[point];
        }
    }

    // MD[a]: push the distance between two points, measured in current
    // coordinates on the projection vector (a = 0) or in original coordinates
    // on the dual projection vector (a = 1). The deeper stack argument is taken
    // in zp0, the top argument in zp1.
    private void MeasureDistance(bool useOriginal)
    {
        int top = Pop();
        int deep = Pop();
        Zone zone0 = ZoneFor(State.Zp0);
        Zone zone1 = ZoneFor(State.Zp1);
        if (deep < 0 || deep >= zone0.PointCount || top < 0 || top >= zone1.PointCount)
        {
            Push(0);
            return;
        }

        int distance = useOriginal
            ? DualProject(
                zone0.OriginalX[deep] - zone1.OriginalX[top],
                zone0.OriginalY[deep] - zone1.OriginalY[top])
            : Project(
                zone0.CurrentX[deep] - zone1.CurrentX[top],
                zone0.CurrentY[deep] - zone1.CurrentY[top]);
        Push(distance);
    }

    // ── Absolute movement (Stage 4) ───────────────────────────────────────

    // MDAP[a]: touch a point (zp0); if a = 1, round its projection to the grid.
    private void MoveDirectAbsolute(bool round)
    {
        int point = Pop();
        Zone zone = ZoneFor(State.Zp0);
        if (point < 0 || point >= zone.PointCount)
        {
            return;
        }

        int distance = 0;
        if (round)
        {
            int current = Project(zone.CurrentX[point], zone.CurrentY[point]);
            distance = Round(current, 0) - current;
        }

        MovePoint(zone, point, distance);
        State.Rp0 = point;
        State.Rp1 = point;
    }

    // MIAP[a]: move a point (zp0) to a Control Value Table distance from the
    // origin; if a = 1, apply the control-value cut-in and round to the grid.
    private void MoveIndirectAbsolute(bool round)
    {
        int cvtIndex = Pop();
        int point = Pop();
        Zone zone = ZoneFor(State.Zp0);
        if (point < 0 || point >= zone.PointCount)
        {
            return;
        }

        int distance = GetControlValue(cvtIndex);

        // A twilight point has no outline; MIAP defines its position directly
        // along the projection vector.
        if (State.Zp0 == 0)
        {
            zone.OriginalX[point] = F2Dot14.Mul(distance, State.ProjectionVectorX);
            zone.OriginalY[point] = F2Dot14.Mul(distance, State.ProjectionVectorY);
            zone.CurrentX[point] = zone.OriginalX[point];
            zone.CurrentY[point] = zone.OriginalY[point];
        }

        int current = Project(zone.CurrentX[point], zone.CurrentY[point]);
        if (round)
        {
            if (Math.Abs(distance - current) > State.ControlValueCutIn)
            {
                distance = current;
            }

            distance = Round(distance, 0);
        }

        MovePoint(zone, point, distance - current);
        State.Rp0 = point;
        State.Rp1 = point;
    }

    // ── Relative movement (Stage 4) ───────────────────────────────────────

    // MDRP[abcde]: move a point (zp1) to a distance from rp0 (zp0) derived from
    // their original distance, optionally rounded and clamped to the minimum
    // distance. Compensation for distance type (the de bits) is treated as zero
    // (grey rendering); the colour compensations are a later refinement.
    private void MoveDirectRelative(byte opcode)
    {
        bool setRp0 = (opcode & MoveFlagSetRp0) != 0;
        bool keepMinimum = (opcode & MoveFlagMinDistance) != 0;
        bool round = (opcode & MoveFlagRound) != 0;

        int point = Pop();
        Zone refZone = ZoneFor(State.Zp0);
        Zone zone = ZoneFor(State.Zp1);
        if (!IsValidPoint(point, zone) || !IsValidPoint(State.Rp0, refZone))
        {
            return;
        }

        int originalDistance = DualProject(
            zone.OriginalX[point] - refZone.OriginalX[State.Rp0],
            zone.OriginalY[point] - refZone.OriginalY[State.Rp0]);

        int distance = ApplySingleWidth(originalDistance);
        if (round)
        {
            distance = Round(distance, 0);
        }

        distance = EnforceMinimumDistance(distance, originalDistance, keepMinimum);

        int currentDistance = Project(
            zone.CurrentX[point] - refZone.CurrentX[State.Rp0],
            zone.CurrentY[point] - refZone.CurrentY[State.Rp0]);

        MovePoint(zone, point, distance - currentDistance);

        State.Rp1 = State.Rp0;
        State.Rp2 = point;
        if (setRp0)
        {
            State.Rp0 = point;
        }
    }

    // MIRP[abcde]: move a point (zp1) to a Control Value Table distance from
    // rp0 (zp0), with auto-flip against the original distance's sign, the
    // control-value cut-in, optional rounding, and the minimum-distance clamp.
    private void MoveIndirectRelative(byte opcode)
    {
        bool setRp0 = (opcode & MoveFlagSetRp0) != 0;
        bool keepMinimum = (opcode & MoveFlagMinDistance) != 0;
        bool round = (opcode & MoveFlagRound) != 0;

        int cvtIndex = Pop();
        int point = Pop();
        Zone refZone = ZoneFor(State.Zp0);
        Zone zone = ZoneFor(State.Zp1);
        if (!IsValidPoint(point, zone) || !IsValidPoint(State.Rp0, refZone))
        {
            return;
        }

        int cvtDistance = ApplySingleWidth(GetControlValue(cvtIndex));

        int originalDistance = DualProject(
            zone.OriginalX[point] - refZone.OriginalX[State.Rp0],
            zone.OriginalY[point] - refZone.OriginalY[State.Rp0]);

        if (State.AutoFlip && Sign(cvtDistance) != Sign(originalDistance))
        {
            cvtDistance = -cvtDistance;
        }

        int distance = cvtDistance;
        if (round)
        {
            // Control-value cut-in: when the CVT distance is too far from the
            // actual original distance, use the original distance instead.
            if (Math.Abs(cvtDistance - originalDistance) > State.ControlValueCutIn)
            {
                distance = originalDistance;
            }

            distance = Round(distance, 0);
        }

        distance = EnforceMinimumDistance(distance, originalDistance, keepMinimum);

        int currentDistance = Project(
            zone.CurrentX[point] - refZone.CurrentX[State.Rp0],
            zone.CurrentY[point] - refZone.CurrentY[State.Rp0]);

        MovePoint(zone, point, distance - currentDistance);

        State.Rp1 = State.Rp0;
        State.Rp2 = point;
        if (setRp0)
        {
            State.Rp0 = point;
        }
    }

    private static bool IsValidPoint(int point, Zone zone)
    {
        return point >= 0 && point < zone.PointCount;
    }

    // Snaps a distance to the single-width value when it lies within the
    // single-width cut-in. With the default cut-in of zero this is a no-op;
    // the full single-width semantics are a later refinement.
    private int ApplySingleWidth(int distance)
    {
        if (State.SingleWidthCutIn <= 0)
        {
            return distance;
        }

        int width = State.SingleWidthValue;
        if (distance >= 0)
        {
            if (Math.Abs(distance - width) < State.SingleWidthCutIn)
            {
                distance = width;
            }
        }
        else if (Math.Abs(distance + width) < State.SingleWidthCutIn)
        {
            distance = -width;
        }

        return distance;
    }

    // Clamps a distance to the graphics-state minimum distance, preserving the
    // sign of the original distance.
    private int EnforceMinimumDistance(int distance, int originalDistance, bool keep)
    {
        if (!keep)
        {
            return distance;
        }

        if (originalDistance >= 0)
        {
            return distance < State.MinimumDistance ? State.MinimumDistance : distance;
        }

        return distance > -State.MinimumDistance ? -State.MinimumDistance : distance;
    }

    // ── Vector-to-line setters (Stage 4) ──────────────────────────────────

    // SPVTL[a] / SFVTL[a]: set the projection (and dual) or freedom vector
    // parallel (a = 0) or perpendicular (a = 1) to the current-coordinate line
    // from p2 (zp2) to p1 (zp1).
    private void SetVectorToLine(bool projection, bool perpendicular)
    {
        int p1 = Pop();
        int p2 = Pop();
        Zone zone1 = ZoneFor(State.Zp1);
        Zone zone2 = ZoneFor(State.Zp2);
        if (!IsValidPoint(p1, zone1) || !IsValidPoint(p2, zone2))
        {
            return;
        }

        int dx = zone1.CurrentX[p1] - zone2.CurrentX[p2];
        int dy = zone1.CurrentY[p1] - zone2.CurrentY[p2];
        (int vx, int vy) = NormalizeToVector(dx, dy, perpendicular);

        if (projection)
        {
            SetProjection(vx, vy);
        }
        else
        {
            SetFreedom(vx, vy);
        }
    }

    // SDPVTL[a]: set the dual projection vector from the original-coordinate
    // line and the projection vector from the current-coordinate line, parallel
    // (a = 0) or perpendicular (a = 1) to the line from p2 (zp2) to p1 (zp1).
    private void SetDualVectorToLine(bool perpendicular)
    {
        int p1 = Pop();
        int p2 = Pop();
        Zone zone1 = ZoneFor(State.Zp1);
        Zone zone2 = ZoneFor(State.Zp2);
        if (!IsValidPoint(p1, zone1) || !IsValidPoint(p2, zone2))
        {
            return;
        }

        (int dvx, int dvy) = NormalizeToVector(
            zone1.OriginalX[p1] - zone2.OriginalX[p2],
            zone1.OriginalY[p1] - zone2.OriginalY[p2],
            perpendicular);
        (int pvx, int pvy) = NormalizeToVector(
            zone1.CurrentX[p1] - zone2.CurrentX[p2],
            zone1.CurrentY[p1] - zone2.CurrentY[p2],
            perpendicular);

        // Set both directly: SetProjection would overwrite the dual vector.
        State.ProjectionVectorX = pvx;
        State.ProjectionVectorY = pvy;
        State.DualProjectionVectorX = dvx;
        State.DualProjectionVectorY = dvy;
    }

    // Normalizes a 26.6 coordinate delta to a 2.14 unit vector, optionally
    // rotated 90 degrees. A degenerate (zero-length) line defaults to the
    // x axis. Uses an exact integer square root, so the result is deterministic
    // across platforms.
    private static (int X, int Y) NormalizeToVector(int dx, int dy, bool perpendicular)
    {
        if (perpendicular)
        {
            int swap = dx;
            dx = -dy;
            dy = swap;
        }

        if (dx == 0 && dy == 0)
        {
            return (GraphicsState.One2Dot14, 0);
        }

        long length = IntegerSqrt(((long)dx * dx) + ((long)dy * dy));
        if (length == 0)
        {
            return (GraphicsState.One2Dot14, 0);
        }

        int vx = (int)(((long)dx * GraphicsState.One2Dot14) / length);
        int vy = (int)(((long)dy * GraphicsState.One2Dot14) / length);
        return (vx, vy);
    }

    // Exact integer floor of the square root; the double seed is corrected by
    // integer steps so the result does not depend on floating-point precision.
    private static long IntegerSqrt(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        long root = (long)Math.Sqrt(value);
        while (root > 0 && root * root > value)
        {
            root--;
        }

        while ((root + 1) * (root + 1) <= value)
        {
            root++;
        }

        return root;
    }

    // INSTCTRL: set or clear a selector bit of the instruct-control state. The
    // exact selector semantics are refined when hinting is wired in.
    private void ApplyInstructControl(int selector, int value)
    {
        if (value != 0)
        {
            State.InstructControl |= selector;
        }
        else
        {
            State.InstructControl &= ~selector;
        }
    }

    // ── Arithmetic, logical, storage (Stage 5/6) ──────────────────────────

    // Pops two operands (deeper = a, top = b) and pushes op(a, b).
    private void BinaryOp(Func<int, int, int> op)
    {
        int b = Pop();
        int a = Pop();
        Push(op(a, b));
    }

    // ROLL: rotates the top three stack elements so the third moves to the top.
    private void Roll()
    {
        if (_sp < 3)
        {
            return;
        }

        int a = _stack[_sp - 1];
        int b = _stack[_sp - 2];
        int c = _stack[_sp - 3];
        _stack[_sp - 3] = b;
        _stack[_sp - 2] = a;
        _stack[_sp - 1] = c;
    }

    private int ReadStorage(int location)
    {
        return location >= 0 && location < _storage.Length ? _storage[location] : 0;
    }

    private void WriteStorage(int location, int value)
    {
        if (location >= 0 && location < _storage.Length)
        {
            _storage[location] = value;
        }
    }

    // ── DELTA exceptions (Stage 5/6) ──────────────────────────────────────

    // DELTAP1/2/3: each stack pair is (point number, argument). The argument's
    // high nibble selects the relative ppem and the low nibble the magnitude.
    // The exception applies only at the matching ppem. tableBase is 0/16/32 for
    // DELTAP1/2/3.
    private void ApplyDeltaP(int tableBase)
    {
        int count = Pop();
        Zone zone = ZoneFor(State.Zp0);
        for (int k = 0; k < count; k++)
        {
            int argument = Pop();
            int point = Pop();
            int targetPpem = State.DeltaBase + tableBase + ((argument >> 4) & 0x0F);
            if (_ppem == targetPpem)
            {
                MovePoint(zone, point, DeltaMagnitude(argument & 0x0F));
            }
        }
    }

    // DELTAC1/2/3: like DELTAP but each pair is (CVT index, argument) and the
    // exception adjusts a Control Value Table entry.
    private void ApplyDeltaC(int tableBase)
    {
        int count = Pop();
        for (int k = 0; k < count; k++)
        {
            int argument = Pop();
            int cvtIndex = Pop();
            int targetPpem = State.DeltaBase + tableBase + ((argument >> 4) & 0x0F);
            if (_ppem == targetPpem)
            {
                SetControlValue(cvtIndex, GetControlValue(cvtIndex) + DeltaMagnitude(argument & 0x0F));
            }
        }
    }

    // Maps a DELTA magnitude selector (0..15) to a signed step count (skipping
    // zero: 0..7 -> -8..-1, 8..15 -> +1..+8) scaled by the delta step size,
    // 1/(2^DeltaShift) of a pixel.
    private int DeltaMagnitude(int selector)
    {
        int steps = selector < 8 ? selector - 8 : selector - 7;
        int stepSize = F26Dot6.One >> State.DeltaShift;
        return steps * stepSize;
    }

    // ── Shift and alignment (Stage 5/6) ───────────────────────────────────

    // Distance a reference point has moved, measured along the projection
    // vector from its original to its current position.
    private int ReferenceShift(Zone zone, int point)
    {
        if (!IsValidPoint(point, zone))
        {
            return 0;
        }

        return Project(
            zone.CurrentX[point] - zone.OriginalX[point],
            zone.CurrentY[point] - zone.OriginalY[point]);
    }

    // SHP[a]: shift Loop points in zp2 by the reference point's movement.
    // a = 1 uses rp1 in zp0; a = 0 uses rp2 in zp1.
    private void ShiftPoints(bool useRp1)
    {
        int refPoint = useRp1 ? State.Rp1 : State.Rp2;
        Zone refZone = ZoneFor(useRp1 ? State.Zp0 : State.Zp1);
        Zone zone = ZoneFor(State.Zp2);
        int distance = ReferenceShift(refZone, refPoint);
        int count = State.Loop;
        for (int i = 0; i < count; i++)
        {
            MovePoint(zone, Pop(), distance);
        }

        State.Loop = 1;
    }

    // SHC[a]: shift every point of a contour (popped) in zp2 by the reference
    // point's movement.
    private void ShiftContour(bool useRp1)
    {
        int contour = Pop();
        int refPoint = useRp1 ? State.Rp1 : State.Rp2;
        Zone refZone = ZoneFor(useRp1 ? State.Zp0 : State.Zp1);
        Zone zone = ZoneFor(State.Zp2);
        int distance = ReferenceShift(refZone, refPoint);
        if (contour < 0 || contour >= zone.ContourEnds.Length)
        {
            return;
        }

        int start = contour == 0 ? 0 : zone.ContourEnds[contour - 1] + 1;
        int end = zone.ContourEnds[contour];
        for (int p = start; p <= end && p < zone.PointCount; p++)
        {
            MovePoint(zone, p, distance);
        }
    }

    // SHZ[a]: shift every point of a zone (popped) by the reference movement.
    private void ShiftZone(bool useRp1)
    {
        int zoneSelector = Pop();
        int refPoint = useRp1 ? State.Rp1 : State.Rp2;
        Zone refZone = ZoneFor(useRp1 ? State.Zp0 : State.Zp1);
        Zone zone = ZoneFor(zoneSelector);
        int distance = ReferenceShift(refZone, refPoint);
        for (int p = 0; p < zone.PointCount; p++)
        {
            MovePoint(zone, p, distance);
        }
    }

    // SHPIX: shift Loop points in zp2 by a pixel amount (26.6) directly along
    // the freedom vector.
    private void ShiftByPixels()
    {
        int amount = Pop();
        Zone zone = ZoneFor(State.Zp2);
        int count = State.Loop;
        for (int i = 0; i < count; i++)
        {
            int point = Pop();
            if (!IsValidPoint(point, zone))
            {
                continue;
            }

            if (State.FreedomVectorX != 0)
            {
                zone.CurrentX[point] += F2Dot14.Mul(amount, State.FreedomVectorX);
                zone.TouchedX[point] = true;
            }

            if (State.FreedomVectorY != 0)
            {
                zone.CurrentY[point] += F2Dot14.Mul(amount, State.FreedomVectorY);
                zone.TouchedY[point] = true;
            }
        }

        State.Loop = 1;
    }

    // ALIGNRP: move Loop points in zp1 onto rp0's projected position (zp0).
    private void AlignToReferencePoint()
    {
        Zone refZone = ZoneFor(State.Zp0);
        Zone zone = ZoneFor(State.Zp1);
        int rp0 = State.Rp0;
        int count = State.Loop;
        for (int i = 0; i < count; i++)
        {
            int point = Pop();
            if (!IsValidPoint(point, zone) || !IsValidPoint(rp0, refZone))
            {
                continue;
            }

            int distance = Project(
                zone.CurrentX[point] - refZone.CurrentX[rp0],
                zone.CurrentY[point] - refZone.CurrentY[rp0]);
            MovePoint(zone, point, -distance);
        }

        State.Loop = 1;
    }

    // ALIGNPTS: move two points (p1 in zp1, p2 in zp0) to their midpoint along
    // the projection vector.
    private void AlignPoints()
    {
        int p2 = Pop();
        int p1 = Pop();
        Zone zone1 = ZoneFor(State.Zp1);
        Zone zone0 = ZoneFor(State.Zp0);
        if (!IsValidPoint(p1, zone1) || !IsValidPoint(p2, zone0))
        {
            return;
        }

        int distance = Project(
            zone0.CurrentX[p2] - zone1.CurrentX[p1],
            zone0.CurrentY[p2] - zone1.CurrentY[p1]);
        int half = distance / 2;
        MovePoint(zone1, p1, half);
        MovePoint(zone0, p2, -half);
    }

    // UTP: clear the touch flags of a point (zp0) on the freedom-vector axes.
    private void UntouchPoint()
    {
        int point = Pop();
        Zone zone = ZoneFor(State.Zp0);
        if (!IsValidPoint(point, zone))
        {
            return;
        }

        if (State.FreedomVectorX != 0)
        {
            zone.TouchedX[point] = false;
        }

        if (State.FreedomVectorY != 0)
        {
            zone.TouchedY[point] = false;
        }
    }

    // ── Interpolation (Stage 5/6) ─────────────────────────────────────────

    // IP: interpolate Loop points in zp2 between rp1 (zp0) and rp2 (zp1),
    // preserving each point's original relative position within the current
    // range between the reference points.
    private void InterpolatePoints()
    {
        Zone zone0 = ZoneFor(State.Zp0);
        Zone zone1 = ZoneFor(State.Zp1);
        Zone zone = ZoneFor(State.Zp2);
        int rp1 = State.Rp1;
        int rp2 = State.Rp2;
        bool refsValid = IsValidPoint(rp1, zone0) && IsValidPoint(rp2, zone1);

        int originalRange = 0;
        int currentRange = 0;
        if (refsValid)
        {
            originalRange = DualProject(
                zone1.OriginalX[rp2] - zone0.OriginalX[rp1],
                zone1.OriginalY[rp2] - zone0.OriginalY[rp1]);
            currentRange = Project(
                zone1.CurrentX[rp2] - zone0.CurrentX[rp1],
                zone1.CurrentY[rp2] - zone0.CurrentY[rp1]);
        }

        int count = State.Loop;
        for (int i = 0; i < count; i++)
        {
            int point = Pop();
            if (!refsValid || !IsValidPoint(point, zone))
            {
                continue;
            }

            int originalPosition = DualProject(
                zone.OriginalX[point] - zone0.OriginalX[rp1],
                zone.OriginalY[point] - zone0.OriginalY[rp1]);
            int currentPosition = Project(
                zone.CurrentX[point] - zone0.CurrentX[rp1],
                zone.CurrentY[point] - zone0.CurrentY[rp1]);
            int target = originalRange != 0
                ? MulDiv(originalPosition, currentRange, originalRange)
                : originalPosition;
            MovePoint(zone, point, target - currentPosition);
        }

        State.Loop = 1;
    }

    // ISECT: move a point (zp2) to the intersection of line A (a0,a1 in zp1)
    // and line B (b0,b1 in zp0). Parallel lines fall back to the four-point
    // average. Products are kept in 26.6 by dividing by 0x40.
    private void Intersect()
    {
        int b1 = Pop();
        int b0 = Pop();
        int a1 = Pop();
        int a0 = Pop();
        int point = Pop();
        Zone aZone = ZoneFor(State.Zp1);
        Zone bZone = ZoneFor(State.Zp0);
        Zone pointZone = ZoneFor(State.Zp2);
        if (!IsValidPoint(a0, aZone) || !IsValidPoint(a1, aZone) ||
            !IsValidPoint(b0, bZone) || !IsValidPoint(b1, bZone) ||
            !IsValidPoint(point, pointZone))
        {
            return;
        }

        int dax = aZone.CurrentX[a1] - aZone.CurrentX[a0];
        int day = aZone.CurrentY[a1] - aZone.CurrentY[a0];
        int dbx = bZone.CurrentX[b1] - bZone.CurrentX[b0];
        int dby = bZone.CurrentY[b1] - bZone.CurrentY[b0];
        int dx = bZone.CurrentX[b0] - aZone.CurrentX[a0];
        int dy = bZone.CurrentY[b0] - aZone.CurrentY[a0];

        int discriminant = MulDiv(dax, -dby, 0x40) + MulDiv(day, dbx, 0x40);
        int dotProduct = MulDiv(dax, dbx, 0x40) + MulDiv(day, dby, 0x40);

        if (9 * Math.Abs(discriminant) > Math.Abs(dotProduct))
        {
            int val = MulDiv(dx, -dby, 0x40) + MulDiv(dy, dbx, 0x40);
            pointZone.CurrentX[point] = aZone.CurrentX[a0] + MulDiv(val, dax, discriminant);
            pointZone.CurrentY[point] = aZone.CurrentY[a0] + MulDiv(val, day, discriminant);
        }
        else
        {
            pointZone.CurrentX[point] =
                (aZone.CurrentX[a0] + aZone.CurrentX[a1] + bZone.CurrentX[b0] + bZone.CurrentX[b1]) / 4;
            pointZone.CurrentY[point] =
                (aZone.CurrentY[a0] + aZone.CurrentY[a1] + bZone.CurrentY[b0] + bZone.CurrentY[b1]) / 4;
        }

        pointZone.TouchedX[point] = true;
        pointZone.TouchedY[point] = true;
    }

    // IUP[a]: interpolate points left untouched on the chosen axis within each
    // contour of the glyph zone, anchored to the touched points. Writes
    // coordinates directly and does not set touch flags. Always operates on the
    // glyph zone, independent of the zone pointers.
    private void InterpolateUntouched(bool yDirection)
    {
        Zone zone = ZoneFor(1);
        int[] current = yDirection ? zone.CurrentY : zone.CurrentX;
        int[] original = yDirection ? zone.OriginalY : zone.OriginalX;
        bool[] touched = yDirection ? zone.TouchedY : zone.TouchedX;

        int start = 0;
        for (int c = 0; c < zone.ContourEnds.Length; c++)
        {
            int end = zone.ContourEnds[c];
            if (end >= start && end < zone.PointCount)
            {
                InterpolateContour(current, original, touched, start, end);
            }

            start = end + 1;
        }
    }

    // Interpolates one contour [start, end] (inclusive) for a single axis.
    private static void InterpolateContour(int[] current, int[] original, bool[] touched, int start, int end)
    {
        List<int> anchors = new List<int>();
        for (int i = start; i <= end; i++)
        {
            if (touched[i])
            {
                anchors.Add(i);
            }
        }

        if (anchors.Count == 0)
        {
            return;
        }

        if (anchors.Count == 1)
        {
            int anchor = anchors[0];
            int delta = current[anchor] - original[anchor];
            if (delta != 0)
            {
                for (int i = start; i <= end; i++)
                {
                    if (i != anchor)
                    {
                        current[i] = original[i] + delta;
                    }
                }
            }

            return;
        }

        for (int k = 0; k < anchors.Count; k++)
        {
            InterpolateRun(current, original, anchors[k], anchors[(k + 1) % anchors.Count], start, end);
        }
    }

    // Interpolates the untouched points strictly between two touched anchors,
    // walking the contour forward (with wrap-around) from anchorA to anchorB.
    private static void InterpolateRun(int[] current, int[] original, int anchorA, int anchorB, int start, int end)
    {
        int low = anchorA;
        int high = anchorB;
        if (original[low] > original[high])
        {
            (low, high) = (high, low);
        }

        int originalLow = original[low];
        int originalHigh = original[high];
        int currentLow = current[low];
        int currentHigh = current[high];

        int i = NextInContour(anchorA, start, end);
        while (i != anchorB)
        {
            int o = original[i];
            if (o <= originalLow)
            {
                current[i] = o + (currentLow - originalLow);
            }
            else if (o >= originalHigh)
            {
                current[i] = o + (currentHigh - originalHigh);
            }
            else
            {
                current[i] = originalHigh != originalLow
                    ? currentLow + MulDiv(o - originalLow, currentHigh - currentLow, originalHigh - originalLow)
                    : currentLow;
            }

            i = NextInContour(i, start, end);
        }
    }

    private static int NextInContour(int index, int start, int end)
    {
        return index >= end ? start : index + 1;
    }

    // ── Environment query (Stage 5/6) ─────────────────────────────────────

    // GETINFO: report a conservative engine profile. Returns a scaler version
    // when requested; reports neither rotation nor stretching, consistent with
    // grey (anti-aliased) rendering. The exact version and capability bits are
    // refined when hinting is wired to real output.
    private void GetInformation()
    {
        int selector = Pop();
        int result = 0;
        if ((selector & 0x01) != 0)
        {
            result |= 42;
        }

        if ((selector & 0x20) != 0)
        {
            result |= 1 << 12;
        }

        Push(result);
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
