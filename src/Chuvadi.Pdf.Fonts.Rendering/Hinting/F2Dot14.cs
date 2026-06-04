// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Apple TrueType Reference Manual — F2Dot14 fixed-point arithmetic
// PHASE: Phase 2 — TrueType bytecode hinting (Stage 3)
// 2.14 fixed-point helpers: 2 integer bits, 14 fraction bits (16384 = 1.0).

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting;

/// <summary>
/// Helpers for 2.14 fixed-point values, the representation the hinting
/// interpreter uses for the projection, freedom, and dual-projection unit
/// vectors. A value of <see cref="One"/> (16384) represents 1.0. Values are
/// stored as plain <see cref="int"/>; these methods supply the fixed-point
/// operations the projection arithmetic in later stages relies on.
/// </summary>
internal static class F2Dot14
{
    /// <summary>The 2.14 representation of 1.0.</summary>
    internal const int One = 0x4000;

    /// <summary>Converts a 2.14 value to a double.</summary>
    /// <param name="value">The 2.14 value.</param>
    internal static double ToDouble(int value)
    {
        return value / (double)One;
    }

    /// <summary>Multiplies two 2.14 values, returning a 2.14 result (rounded).</summary>
    /// <param name="a">A 2.14 value.</param>
    /// <param name="b">A 2.14 value.</param>
    internal static int Mul(int a, int b)
    {
        long product = (long)a * b;
        return product >= 0
            ? (int)((product + (One / 2)) / One)
            : (int)((product - (One / 2)) / One);
    }

    /// <summary>
    /// Computes the dot product of two 2.14 vectors, returning a 2.14 scalar
    /// (rounded). Used to project distances onto a vector in later stages.
    /// </summary>
    /// <param name="ax">First vector X (2.14).</param>
    /// <param name="ay">First vector Y (2.14).</param>
    /// <param name="bx">Second vector X (2.14).</param>
    /// <param name="by">Second vector Y (2.14).</param>
    internal static int Dot(int ax, int ay, int bx, int by)
    {
        long sum = ((long)ax * bx) + ((long)ay * by);
        return sum >= 0
            ? (int)((sum + (One / 2)) / One)
            : (int)((sum - (One / 2)) / One);
    }
}
