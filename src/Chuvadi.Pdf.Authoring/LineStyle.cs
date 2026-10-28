// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.4.3 — Line width, cap, join, miter limit, dash pattern

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// How a line or outline is stroked: width, dash pattern, caps and joins.
/// Pass it to the <see cref="PageBuilder"/> drawing overloads that take one.
/// </summary>
public sealed class LineStyle
{
    /// <summary>Gets the line width in points. Default 1.</summary>
    public double Width { get; init; } = 1.0;

    /// <summary>
    /// Gets the dash pattern: alternating dash and gap lengths in points, as
    /// in PDF's <c>d</c> operator. Empty (the default) draws a solid line.
    /// </summary>
    public IReadOnlyList<double> DashPattern { get; init; } = Array.Empty<double>();

    /// <summary>Gets how far into the dash pattern the line starts, in points. Default 0.</summary>
    public double DashPhase { get; init; }

    /// <summary>Gets the line cap. Default <see cref="LineCap.Butt"/>.</summary>
    public LineCap Cap { get; init; } = LineCap.Butt;

    /// <summary>Gets the line join. Default <see cref="LineJoin.Miter"/>.</summary>
    public LineJoin Join { get; init; } = LineJoin.Miter;

    /// <summary>Gets the miter limit (PDF default 10).</summary>
    public double MiterLimit { get; init; } = 10.0;

    // Throws when the style cannot be written as valid PDF.
    internal void Validate()
    {
        if (!double.IsFinite(Width) || Width < 0)
        {
            throw new ArgumentException("LineStyle.Width must be a finite, non-negative number.");
        }

        if (!double.IsFinite(MiterLimit) || MiterLimit < 1)
        {
            throw new ArgumentException("LineStyle.MiterLimit must be at least 1.");
        }

        if (!double.IsFinite(DashPhase))
        {
            throw new ArgumentException("LineStyle.DashPhase must be finite.");
        }

        ArgumentNullException.ThrowIfNull(DashPattern, nameof(DashPattern));
        bool anyPositive = false;
        foreach (double d in DashPattern)
        {
            if (!double.IsFinite(d) || d < 0)
            {
                throw new ArgumentException("LineStyle.DashPattern lengths must be finite and non-negative.");
            }

            anyPositive |= d > 0;
        }

        if (DashPattern.Count > 0 && !anyPositive)
        {
            throw new ArgumentException("LineStyle.DashPattern needs at least one non-zero length.");
        }
    }
}
