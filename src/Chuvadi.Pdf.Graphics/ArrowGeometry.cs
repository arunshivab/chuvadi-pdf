// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Graphics — shared arrow geometry for authoring and on-screen previews.

using System;

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// Pure arrow geometry: the triangular head polygon at the tip of a line and
/// the shortened shaft that meets the head's base. The same numbers drive
/// <c>PageBuilder.DrawArrow</c> output and any on-screen preview, so a
/// preview drawn from these points matches the authored PDF exactly.
/// Coordinates are taken as given; the geometry is symmetric about the shaft,
/// so it is valid in both y-up (PDF) and y-down (screen) coordinate systems.
/// </summary>
public static class ArrowGeometry
{
    /// <summary>
    /// The default head length for a given stroke width: four line widths,
    /// never shorter than 6 units.
    /// </summary>
    /// <param name="strokeWidth">The shaft stroke width.</param>
    /// <returns>The default head length.</returns>
    public static double DefaultHeadLength(double strokeWidth)
    {
        return Math.Max(6.0, strokeWidth * 4.0);
    }

    /// <summary>
    /// The default head width (full base width) for a given head length:
    /// three quarters of the length.
    /// </summary>
    /// <param name="headLength">The head length.</param>
    /// <returns>The default head width.</returns>
    public static double DefaultHeadWidth(double headLength)
    {
        return headLength * 0.75;
    }

    /// <summary>
    /// Computes the three corners of an arrowhead pointing at
    /// <paramref name="tip"/> along the direction from <paramref name="tail"/>.
    /// The returned array is <c>[tip, baseLeft, baseRight]</c>. The head length
    /// is clamped to the shaft length so a short arrow never overshoots its
    /// tail. Returns an empty array when the tail and tip coincide (no
    /// direction to point along).
    /// </summary>
    /// <param name="tail">The start of the arrow shaft.</param>
    /// <param name="tip">The point the arrowhead touches.</param>
    /// <param name="headLength">Distance from the tip to the head's base.</param>
    /// <param name="headWidth">Full width of the head's base.</param>
    /// <returns>The head polygon, or an empty array for a zero-length arrow.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="headLength"/> or <paramref name="headWidth"/>
    /// is negative or not finite.
    /// </exception>
    public static PointF[] HeadPoints(PointF tail, PointF tip, double headLength, double headWidth)
    {
        ValidateSize(headLength, nameof(headLength));
        ValidateSize(headWidth, nameof(headWidth));

        double dx = tip.X - tail.X;
        double dy = tip.Y - tail.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0.0)
        {
            return Array.Empty<PointF>();
        }

        double ux = dx / length;
        double uy = dy / length;
        double l = Math.Min(headLength, length);
        double scale = headLength > 0.0 ? l / headLength : 0.0;
        double half = headWidth * scale / 2.0;

        double baseX = tip.X - (ux * l);
        double baseY = tip.Y - (uy * l);

        // Perpendicular to the shaft.
        double px = -uy;
        double py = ux;

        return new[]
        {
            tip,
            new PointF(baseX + (px * half), baseY + (py * half)),
            new PointF(baseX - (px * half), baseY - (py * half)),
        };
    }

    /// <summary>
    /// The point where a shaft ending in an arrowhead should stop: the centre
    /// of the head's base. Stroking the shaft only to this point keeps the
    /// line cap from poking through the head's tip. Returns
    /// <paramref name="tip"/> for a zero-length arrow.
    /// </summary>
    /// <param name="tail">The start of the arrow shaft.</param>
    /// <param name="tip">The point the arrowhead touches.</param>
    /// <param name="headLength">Distance from the tip to the head's base.</param>
    /// <returns>The shortened shaft end point.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="headLength"/> is negative or not finite.
    /// </exception>
    public static PointF ShaftEnd(PointF tail, PointF tip, double headLength)
    {
        ValidateSize(headLength, nameof(headLength));

        double dx = tip.X - tail.X;
        double dy = tip.Y - tail.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0.0)
        {
            return tip;
        }

        double l = Math.Min(headLength, length);
        return new PointF(tip.X - (dx / length * l), tip.Y - (dy / length * l));
    }

    private static void ValidateSize(double value, string name)
    {
        if (value < 0.0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(name, value, "Size must be finite and non-negative.");
        }
    }
}

/// <summary>Which ends of a line carry an arrowhead.</summary>
public enum ArrowEnds
{
    /// <summary>A head at the end point only (the usual pointer arrow).</summary>
    End,

    /// <summary>A head at the start point only.</summary>
    Start,

    /// <summary>Heads at both ends.</summary>
    Both,
}
