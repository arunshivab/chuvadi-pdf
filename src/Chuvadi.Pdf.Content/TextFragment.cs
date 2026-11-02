// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4.5 — Text showing operators
// PHASE: Phase 1 — Chuvadi.Pdf.Content
// A piece of text extracted from a content stream with its position.

namespace Chuvadi.Pdf.Content;

/// <summary>
/// A piece of text extracted from a PDF content stream, together with
/// its approximate position in user space.
/// </summary>
/// <remarks>
/// Position coordinates are in PDF user space (origin bottom-left, Y increases upward):
/// the origin of the first glyph, from the text matrix mapped through the current
/// transformation matrix (and a form XObject's /Matrix). Since 3.28 the CTM is
/// applied; before, positions were in text space.
///
/// For a full layout-aware extraction, these fragments should be sorted and
/// grouped based on their Y coordinates (same line = similar Y) and X coordinates
/// (reading order = ascending X). That logic lives in Chuvadi.Pdf.Text.
/// </remarks>
public sealed class TextFragment
{
    /// <summary>
    /// Initialises a new <see cref="TextFragment"/>.
    /// </summary>
    /// <param name="text">The Unicode text content of this fragment.</param>
    /// <param name="x">The X position in user space (left edge of first glyph).</param>
    /// <param name="y">The Y position in user space (baseline of the text).</param>
    /// <param name="fontSize">The font size in points at the time of rendering.</param>
    public TextFragment(string text, double x, double y, double fontSize)
        : this(text, x, y, fontSize, double.NaN)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="TextFragment"/> with its advance width.
    /// </summary>
    /// <param name="text">The Unicode text content of this fragment.</param>
    /// <param name="x">The X position in user space (left edge of first glyph).</param>
    /// <param name="y">The Y position in user space (baseline of the text).</param>
    /// <param name="fontSize">The font size in points at the time of rendering.</param>
    /// <param name="width">
    /// The horizontal advance of the shown glyphs in the same space as
    /// <paramref name="x"/>, or <see cref="double.NaN"/> when the font's
    /// widths are not known.
    /// </param>
    public TextFragment(string text, double x, double y, double fontSize, double width)
        : this(text, x, y, fontSize, width, 0.0)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="TextFragment"/> with its advance width and
    /// the direction of its baseline.
    /// </summary>
    /// <param name="text">The Unicode text content of this fragment.</param>
    /// <param name="x">The X position in user space (origin of the first glyph).</param>
    /// <param name="y">The Y position in user space (baseline of the text).</param>
    /// <param name="fontSize">The font size in user space.</param>
    /// <param name="width">
    /// The advance of the shown glyphs along the baseline, in user space, or
    /// <see cref="double.NaN"/> when the font's widths are not known.
    /// </param>
    /// <param name="angle">
    /// The direction of the baseline in degrees, counter-clockwise from the
    /// positive X axis: 0 for ordinary text, 90 for text running up the page.
    /// </param>
    public TextFragment(string text, double x, double y, double fontSize, double width, double angle)
    {
        Text = text ?? string.Empty;
        X = x;
        Y = y;
        FontSize = fontSize;
        Width = width;
        Angle = angle;
    }

    /// <summary>Gets the Unicode text content of this fragment.</summary>
    public string Text { get; }

    /// <summary>Gets the X position (left edge) in PDF user space.</summary>
    public double X { get; }

    /// <summary>Gets the Y position (baseline) in PDF user space.</summary>
    public double Y { get; }

    /// <summary>
    /// Gets the font size in user space: the <c>Tf</c> size scaled by the
    /// vertical scale of the text matrix and the CTM (since 3.28; before, the
    /// <c>Tf</c> size alone).
    /// </summary>
    public double FontSize { get; }

    /// <summary>
    /// Gets the advance of the fragment's glyphs along the baseline, in user
    /// space (from the font's widths), or <see cref="double.NaN"/> when it is
    /// not known.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the direction of the baseline in degrees, counter-clockwise from
    /// the positive X axis of user space (since 3.28): 0 for ordinary text, 90
    /// for text running up the page, -90 for text running down it.
    /// </summary>
    public double Angle { get; }

    /// <summary>
    /// Gets whether the fragment is raised or lowered off the baseline of its
    /// line (since 3.29). Set by text extraction, which compares the fragment
    /// with the main text of its line; <see cref="BaselineShift.None"/> on
    /// fragments straight from the content-stream parser.
    /// </summary>
    public BaselineShift BaselineShift { get; init; }

    /// <summary>
    /// Gets how far the fragment's baseline is off its line's baseline, in
    /// user space, measured across the baseline: positive when raised,
    /// negative when lowered, 0 when <see cref="BaselineShift"/> is
    /// <see cref="BaselineShift.None"/> (since 3.29).
    /// </summary>
    public double BaselineShiftAmount { get; init; }

    /// <summary>
    /// Gets whether the text came from a marked-content <c>/ActualText</c>
    /// entry (PDF 32000-1:2008 §14.9.4) rather than from the glyphs' own
    /// Unicode mapping (since 3.29). Text extraction never rewrites such
    /// text: the source text wins.
    /// </summary>
    public bool IsActualText { get; init; }

    /// <summary>
    /// Gets the marked-content identifier (<c>/MCID</c>) of the innermost
    /// marked-content sequence the text was shown in, or null when it has
    /// none (since 3.29). In a Tagged PDF the structure tree refers to page
    /// content by these identifiers (PDF 32000-1:2008 §14.7.4).
    /// </summary>
    public int? MarkedContentId { get; init; }

    /// <summary>
    /// Returns a copy of this fragment with the given baseline shift; every
    /// other property is kept.
    /// </summary>
    /// <param name="shift">Whether the copy is raised, lowered or on the baseline.</param>
    /// <param name="amount">
    /// The distance off the line's baseline in user space, positive when
    /// raised and negative when lowered.
    /// </param>
    /// <returns>The copy.</returns>
    public TextFragment WithBaselineShift(BaselineShift shift, double amount) =>
        new TextFragment(Text, X, Y, FontSize, Width, Angle)
        {
            BaselineShift = shift,
            BaselineShiftAmount = shift == BaselineShift.None ? 0.0 : amount,
            IsActualText = IsActualText,
            MarkedContentId = MarkedContentId,
        };

    /// <inheritdoc/>
    public override string ToString() =>
        $"[{X:F1},{Y:F1}] \"{Text}\" ({FontSize}pt)";
}
