// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Apple TrueType Reference Manual — F26Dot6 fixed-point arithmetic
// PHASE: Phase 2 — TrueType bytecode hinting (Stage 3)
// 26.6 fixed-point helpers: 26 integer bits, 6 fraction bits (64 = 1.0).

namespace Chuvadi.Pdf.Fonts.Rendering.Hinting;

/// <summary>
/// Helpers for 26.6 fixed-point values, the representation the hinting
/// interpreter uses for distances and device-space coordinates. A value of
/// <see cref="One"/> (64) represents one pixel. Values are stored as plain
/// <see cref="int"/>; these methods supply the fixed-point operations.
/// </summary>
internal static class F26Dot6
{
    /// <summary>The 26.6 representation of 1.0 (one pixel).</summary>
    internal const int One = 64;

    /// <summary>Converts a whole number of pixels to 26.6.</summary>
    /// <param name="pixels">The pixel count.</param>
    internal static int FromPixels(int pixels)
    {
        return pixels * One;
    }

    /// <summary>Converts a 26.6 value to a double, in pixels.</summary>
    /// <param name="value">The 26.6 value.</param>
    internal static double ToDouble(int value)
    {
        return value / (double)One;
    }

    /// <summary>Truncates a 26.6 value toward zero to whole pixels.</summary>
    /// <param name="value">The 26.6 value.</param>
    /// <returns>The whole-pixel count.</returns>
    internal static int ToPixels(int value)
    {
        return value / One;
    }

    /// <summary>Rounds a 26.6 value down (toward negative infinity) to a whole pixel.</summary>
    /// <param name="value">The 26.6 value.</param>
    internal static int Floor(int value)
    {
        return value & ~(One - 1);
    }

    /// <summary>Rounds a 26.6 value up (toward positive infinity) to a whole pixel.</summary>
    /// <param name="value">The 26.6 value.</param>
    internal static int Ceiling(int value)
    {
        return (value + One - 1) & ~(One - 1);
    }

    /// <summary>Rounds a 26.6 value to the nearest whole pixel (ties toward positive infinity).</summary>
    /// <param name="value">The 26.6 value.</param>
    internal static int Round(int value)
    {
        return (value + (One / 2)) & ~(One - 1);
    }

    /// <summary>Multiplies two 26.6 values, returning a 26.6 result (rounded).</summary>
    /// <param name="a">A 26.6 value.</param>
    /// <param name="b">A 26.6 value.</param>
    internal static int Mul(int a, int b)
    {
        long product = (long)a * b;
        return product >= 0
            ? (int)((product + (One / 2)) / One)
            : (int)((product - (One / 2)) / One);
    }

    /// <summary>Divides one 26.6 value by another, returning a 26.6 result (rounded).</summary>
    /// <param name="a">The 26.6 dividend.</param>
    /// <param name="b">The 26.6 divisor.</param>
    /// <returns>The quotient in 26.6, or 0 when <paramref name="b"/> is 0.</returns>
    internal static int Div(int a, int b)
    {
        if (b == 0)
        {
            return 0;
        }

        long numerator = (long)a * One;
        long absNum = numerator < 0 ? -numerator : numerator;
        long absDen = b < 0 ? -(long)b : b;
        long magnitude = (absNum + (absDen / 2)) / absDen;
        bool negative = (numerator < 0) ^ (b < 0);
        return (int)(negative ? -magnitude : magnitude);
    }
}
