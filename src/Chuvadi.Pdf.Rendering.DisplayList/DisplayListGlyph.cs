// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 — Text-showing operators
// PHASE: Phase 2.1 — display-list intermediate

using System;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// A single glyph entry inside a <see cref="TextOp"/>: position in the
/// text's local coordinate system, advance width, and the Unicode string
/// the glyph represents (one or more code points to allow ligatures).
/// </summary>
/// <remarks>
/// <para>
/// Positions are in text-matrix-local space — the enclosing
/// <see cref="TextOp"/>'s <see cref="TextOp.Transform"/> maps them into
/// PDF user space. The advance is also in text-local units.
/// </para>
/// <para>
/// <see cref="Unicode"/> is a string (not a single <c>char</c>) so a
/// single glyph that represents a ligature such as "ﬁ" can carry its
/// full canonical decomposition ("fi"). For non-ligature glyphs the
/// string is a one-code-point sequence.
/// </para>
/// </remarks>
public readonly struct DisplayListGlyph
{
    /// <summary>Initialises a <see cref="DisplayListGlyph"/>.</summary>
    /// <param name="x">X coordinate in text-local space.</param>
    /// <param name="y">Y coordinate (baseline) in text-local space.</param>
    /// <param name="advance">Glyph advance width in text-local units.</param>
    /// <param name="unicode">
    /// The Unicode string this glyph represents. Must be non-null; may be
    /// empty for non-printing positioning entries.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="unicode"/> is null.
    /// </exception>
    public DisplayListGlyph(double x, double y, double advance, string unicode)
    {
        ArgumentNullException.ThrowIfNull(unicode);

        X = x;
        Y = y;
        Advance = advance;
        Unicode = unicode;
    }

    /// <summary>Gets the X coordinate in text-local space.</summary>
    public double X { get; }

    /// <summary>Gets the Y coordinate (baseline) in text-local space.</summary>
    public double Y { get; }

    /// <summary>Gets the glyph advance width in text-local units.</summary>
    public double Advance { get; }

    /// <summary>
    /// Gets the Unicode string the glyph represents. One code point per
    /// non-ligature glyph; multi-code-point for ligatures.
    /// </summary>
    public string Unicode { get; }
}
