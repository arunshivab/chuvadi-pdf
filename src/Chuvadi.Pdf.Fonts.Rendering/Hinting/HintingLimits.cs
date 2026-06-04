// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType specification — maxp (Maximum Profile), version 1.0 fields
// PHASE: Phase 2 — TrueType bytecode hinting (Stage 2: VM skeleton)
// Resource limits that size the interpreter's function/storage/stack tables.

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting;

/// <summary>
/// The hinting-relevant maximums declared by a font's <c>maxp</c> table
/// (version 1.0). Used by <see cref="HintingInterpreter"/> to size its function
/// table, storage area, and operand stack. Fonts with a <c>maxp</c> version 0.5
/// table carry no such limits; <see cref="Default"/> supplies conservative
/// fallbacks in that case.
/// </summary>
internal readonly struct HintingLimits
{
    /// <summary>
    /// Initialises a <see cref="HintingLimits"/> value.
    /// </summary>
    /// <param name="maxFunctionDefs">Maximum number of FDEF function definitions.</param>
    /// <param name="maxInstructionDefs">Maximum number of IDEF instruction definitions.</param>
    /// <param name="maxStorage">Number of storage-area locations.</param>
    /// <param name="maxStackElements">Maximum depth of the operand stack.</param>
    /// <param name="maxTwilightPoints">Number of points in the twilight zone (zone 0).</param>
    internal HintingLimits(
        int maxFunctionDefs,
        int maxInstructionDefs,
        int maxStorage,
        int maxStackElements,
        int maxTwilightPoints)
    {
        MaxFunctionDefs = maxFunctionDefs;
        MaxInstructionDefs = maxInstructionDefs;
        MaxStorage = maxStorage;
        MaxStackElements = maxStackElements;
        MaxTwilightPoints = maxTwilightPoints;
    }

    /// <summary>Maximum number of FDEF function definitions.</summary>
    internal int MaxFunctionDefs { get; }

    /// <summary>Maximum number of IDEF instruction definitions.</summary>
    internal int MaxInstructionDefs { get; }

    /// <summary>Number of storage-area locations.</summary>
    internal int MaxStorage { get; }

    /// <summary>Maximum depth of the operand stack.</summary>
    internal int MaxStackElements { get; }

    /// <summary>Number of points in the twilight zone (zone 0).</summary>
    internal int MaxTwilightPoints { get; }

    /// <summary>
    /// Conservative fallback limits for fonts whose <c>maxp</c> table is
    /// version 0.5 (no hinting maximums) or otherwise unreadable. These fonts
    /// carry no instructions, so the values exist only to keep the
    /// interpreter's tables non-degenerate.
    /// </summary>
    internal static HintingLimits Default { get; } =
        new HintingLimits(
            maxFunctionDefs: 64,
            maxInstructionDefs: 4,
            maxStorage: 64,
            maxStackElements: 256,
            maxTwilightPoints: 16);
}
