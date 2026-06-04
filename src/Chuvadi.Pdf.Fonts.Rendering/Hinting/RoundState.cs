// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Apple TrueType Reference Manual — rounding state (RTG/RTHG/RTDG/...)
// PHASE: Phase 2 — TrueType bytecode hinting (Stage 2: VM skeleton)
// The active rounding mode of the hinting graphics state.

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting;

/// <summary>
/// The rounding mode applied to engine distances by the hinting interpreter.
/// The numeric <c>round()</c> behaviour for each mode is implemented in a later
/// stage; Stage 2 stores and defaults the mode only.
/// </summary>
internal enum RoundState
{
    /// <summary>Round to the nearest grid line (RTG, the default).</summary>
    ToGrid,

    /// <summary>Round to the nearest half grid line (RTHG).</summary>
    ToHalfGrid,

    /// <summary>Round to the nearest half or whole grid line (RTDG).</summary>
    ToDoubleGrid,

    /// <summary>Round down to the grid (RDTG, toward zero floor).</summary>
    DownToGrid,

    /// <summary>Round up to the grid (RUTG, toward ceiling).</summary>
    UpToGrid,

    /// <summary>Rounding off — distances are used unrounded (ROFF).</summary>
    Off,

    /// <summary>Super-round state set by SROUND.</summary>
    Super,

    /// <summary>45-degree super-round state set by S45ROUND.</summary>
    Super45,
}
