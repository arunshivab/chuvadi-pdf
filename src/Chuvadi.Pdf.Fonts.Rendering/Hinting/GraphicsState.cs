// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Apple TrueType Reference Manual — the graphics state
// PHASE: Phase 2 — TrueType bytecode hinting (Stage 2: VM skeleton)
// The interpreter's graphics state and its spec-defined default values.

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting;

/// <summary>
/// The TrueType hinting graphics state: the projection/freedom/dual vectors,
/// reference points, zone pointers, rounding mode, and the various cut-ins and
/// flags that instructions read and modify.
/// </summary>
/// <remarks>
/// <para>
/// Stage 2 establishes the state object and its spec defaults (via
/// <see cref="Reset"/>) so that later stages can build on a correct baseline.
/// Vectors are stored in F2Dot14 fixed point (16384 = 1.0); distances and
/// cut-ins are in F26Dot6 fixed point (64 = 1 pixel). The instructions that
/// consume these fields — vector setters, the rounding engine, point movement —
/// are implemented in subsequent stages.
/// </para>
/// </remarks>
internal sealed class GraphicsState
{
    /// <summary>The F2Dot14 value representing 1.0 (used for axis-aligned vectors).</summary>
    internal const int One2Dot14 = 0x4000;

    /// <summary>Initialises a <see cref="GraphicsState"/> with spec default values.</summary>
    internal GraphicsState()
    {
        Reset();
    }

    /// <summary>Projection vector X component (F2Dot14).</summary>
    internal int ProjectionVectorX { get; set; }

    /// <summary>Projection vector Y component (F2Dot14).</summary>
    internal int ProjectionVectorY { get; set; }

    /// <summary>Freedom vector X component (F2Dot14).</summary>
    internal int FreedomVectorX { get; set; }

    /// <summary>Freedom vector Y component (F2Dot14).</summary>
    internal int FreedomVectorY { get; set; }

    /// <summary>Dual projection vector X component (F2Dot14).</summary>
    internal int DualProjectionVectorX { get; set; }

    /// <summary>Dual projection vector Y component (F2Dot14).</summary>
    internal int DualProjectionVectorY { get; set; }

    /// <summary>Reference point 0 index.</summary>
    internal int Rp0 { get; set; }

    /// <summary>Reference point 1 index.</summary>
    internal int Rp1 { get; set; }

    /// <summary>Reference point 2 index.</summary>
    internal int Rp2 { get; set; }

    /// <summary>Zone pointer 0 (0 = twilight zone, 1 = glyph zone).</summary>
    internal int Zp0 { get; set; }

    /// <summary>Zone pointer 1 (0 = twilight zone, 1 = glyph zone).</summary>
    internal int Zp1 { get; set; }

    /// <summary>Zone pointer 2 (0 = twilight zone, 1 = glyph zone).</summary>
    internal int Zp2 { get; set; }

    /// <summary>The SLOOP loop counter (number of times the next looped op runs).</summary>
    internal int Loop { get; set; }

    /// <summary>The active rounding mode.</summary>
    internal RoundState RoundState { get; set; }

    /// <summary>Control value cut-in (F26Dot6); default 17/16 pixel.</summary>
    internal int ControlValueCutIn { get; set; }

    /// <summary>Single-width cut-in (F26Dot6).</summary>
    internal int SingleWidthCutIn { get; set; }

    /// <summary>Single-width value (F26Dot6).</summary>
    internal int SingleWidthValue { get; set; }

    /// <summary>Minimum distance enforced by MDRP/MIRP (F26Dot6); default 1 pixel.</summary>
    internal int MinimumDistance { get; set; }

    /// <summary>DELTA base point size (default 9).</summary>
    internal int DeltaBase { get; set; }

    /// <summary>DELTA shift exponent (default 3).</summary>
    internal int DeltaShift { get; set; }

    /// <summary>Whether MIRP may flip the sign of a negative CVT distance (auto_flip).</summary>
    internal bool AutoFlip { get; set; }

    /// <summary>INSTCTRL flags controlling instruction execution.</summary>
    internal int InstructControl { get; set; }

    /// <summary>SCANCTRL dropout-control threshold/flags.</summary>
    internal int ScanControl { get; set; }

    /// <summary>SCANTYPE dropout-control rule selector.</summary>
    internal int ScanType { get; set; }

    /// <summary>
    /// Restores every field to its TrueType-specified default. Called once at
    /// construction and again before each control-value-program (prep) run,
    /// which executes once per point size.
    /// </summary>
    internal void Reset()
    {
        // Projection, freedom, and dual vectors default to the x axis.
        ProjectionVectorX = One2Dot14;
        ProjectionVectorY = 0;
        FreedomVectorX = One2Dot14;
        FreedomVectorY = 0;
        DualProjectionVectorX = One2Dot14;
        DualProjectionVectorY = 0;

        Rp0 = 0;
        Rp1 = 0;
        Rp2 = 0;

        // Zone pointers default to the glyph zone (1).
        Zp0 = 1;
        Zp1 = 1;
        Zp2 = 1;

        Loop = 1;
        RoundState = RoundState.ToGrid;

        // 17/16 pixel in F26Dot6 = 68; 1 pixel = 64.
        ControlValueCutIn = 68;
        SingleWidthCutIn = 0;
        SingleWidthValue = 0;
        MinimumDistance = 64;

        DeltaBase = 9;
        DeltaShift = 3;

        AutoFlip = true;
        InstructControl = 0;
        ScanControl = 0;
        ScanType = 0;
    }
}
