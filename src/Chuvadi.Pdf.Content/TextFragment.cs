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
/// Position coordinates are in PDF user space (origin bottom-left, Y increases upward).
/// The X and Y values come from the text matrix at the point the text was rendered.
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
    {
        Text = text ?? string.Empty;
        X = x;
        Y = y;
        FontSize = fontSize;
        Width = width;
    }

    /// <summary>Gets the Unicode text content of this fragment.</summary>
    public string Text { get; }

    /// <summary>Gets the X position (left edge) in PDF user space.</summary>
    public double X { get; }

    /// <summary>Gets the Y position (baseline) in PDF user space.</summary>
    public double Y { get; }

    /// <summary>Gets the font size in points.</summary>
    public double FontSize { get; }

    /// <summary>
    /// Gets the advance width of the fragment's glyphs (from the font's
    /// widths), or <see cref="double.NaN"/> when it is not known.
    /// </summary>
    public double Width { get; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"[{X:F1},{Y:F1}] \"{Text}\" ({FontSize}pt)";
}
