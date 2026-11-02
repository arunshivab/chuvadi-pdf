// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.28 — rotated runs and raised or lowered text in extraction (D-99)

using System;
using Chuvadi.Pdf.Content;

namespace Chuvadi.Pdf.Text;

/// <summary>
/// Places fragments in the frame of a baseline direction, so the extractors
/// read rotated text (a caption up the side of a picture) as lines of its own,
/// and recognises raised or lowered small text (superscripts, subscripts) so it
/// stays a separate token instead of joining the word before it.
/// </summary>
internal static class TextGeometry
{
    // Baselines within this many degrees count as the same direction.
    private const double DirectionTolerance = 2.0;

    // Superscripts and subscripts: at most this fraction of the other text's
    // size, and moved off its baseline by more than this fraction of it.
    private const double ScriptSizeRatio = 0.85;
    private const double ScriptShift = 0.15;

    /// <summary>True when the two fragments run in the same direction.</summary>
    internal static bool SameDirection(TextFragment a, TextFragment b)
    {
        double d = Math.IEEERemainder(a.Angle - b.Angle, 360.0);
        return Math.Abs(d) < DirectionTolerance;
    }

    /// <summary>The position along a baseline running at <paramref name="angle"/> degrees.</summary>
    internal static double Along(TextFragment f, double angle)
    {
        double r = angle * Math.PI / 180.0;
        return (f.X * Math.Cos(r)) + (f.Y * Math.Sin(r));
    }

    /// <summary>The position across (above) a baseline running at <paramref name="angle"/> degrees.</summary>
    internal static double Across(TextFragment f, double angle)
    {
        double r = angle * Math.PI / 180.0;
        return (f.Y * Math.Cos(r)) - (f.X * Math.Sin(r));
    }

    /// <summary>
    /// True when one of two touching fragments on a line is a superscript or
    /// subscript of the other, and both sides of the boundary are letters or
    /// digits, so a space keeps them apart ("Cionini" + raised "a").
    /// </summary>
    internal static bool IsScriptBoundary(TextFragment prev, TextFragment current)
    {
        if (prev.Text.Length == 0 || current.Text.Length == 0
            || !char.IsLetterOrDigit(prev.Text[^1]) || !char.IsLetterOrDigit(current.Text[0]))
        {
            return false;
        }

        double small = Math.Min(prev.FontSize, current.FontSize);
        double large = Math.Max(prev.FontSize, current.FontSize);
        if (large <= 0 || small / large > ScriptSizeRatio)
        {
            return false;
        }

        double shift = Math.Abs(Across(current, prev.Angle) - Across(prev, prev.Angle));
        return shift > ScriptShift * large;
    }
}
