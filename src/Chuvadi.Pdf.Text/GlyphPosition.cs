// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 — Text-showing operators
// PHASE: v2.0.0 R3 — Text run extraction

namespace Chuvadi.Pdf.Text;

/// <summary>
/// Position and identity of a single glyph in a <see cref="TextRun"/>.
/// </summary>
/// <remarks>
/// <para>
/// In v2.0.0 the positions returned by <see cref="TextRunBuilder"/> are
/// estimated from the run's bounding-box geometry and font size, using
/// the average-glyph-width heuristic (0.6 of font size). Production-grade
/// per-glyph positions — which need font-metric tables to give exact
/// advance widths per glyph — are scheduled for v2.1.
/// </para>
/// <para>
/// Despite the estimation, the X coordinates within a run are
/// monotonically non-decreasing and the sum of advances closely tracks
/// the run's geometric width, which is sufficient for hit-testing,
/// caret placement, and highlight overlays in most use cases.
/// </para>
/// </remarks>
public readonly struct GlyphPosition
{
    /// <summary>Initialises a <see cref="GlyphPosition"/> with the given fields.</summary>
    /// <param name="x">X coordinate in PDF user space, points (1/72 inch).</param>
    /// <param name="y">Y coordinate in PDF user space, baseline; Y up.</param>
    /// <param name="advance">Glyph advance width in PDF user-space points.</param>
    /// <param name="unicode">The Unicode code point this glyph represents.</param>
    public GlyphPosition(double x, double y, double advance, int unicode)
    {
        X = x;
        Y = y;
        Advance = advance;
        Unicode = unicode;
    }

    /// <summary>Gets the X coordinate in PDF user space.</summary>
    public double X { get; }

    /// <summary>Gets the Y coordinate (baseline) in PDF user space.</summary>
    public double Y { get; }

    /// <summary>Gets the glyph advance width in PDF user-space points.</summary>
    public double Advance { get; }

    /// <summary>
    /// Gets the Unicode code point (UTF-32) this glyph represents. For
    /// characters outside the Basic Multilingual Plane the value is the
    /// scalar value, not a surrogate code unit.
    /// </summary>
    public int Unicode { get; }
}
