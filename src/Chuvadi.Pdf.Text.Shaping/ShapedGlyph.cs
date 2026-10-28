// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

namespace Chuvadi.Pdf.Text.Shaping;

/// <summary>
/// One glyph of a pre-shaped run, as produced by an external shaper or by
/// <see cref="TextShaper"/>. Advances and offsets are in 1000-units-per-em
/// text space (1000 = one em), so device values scale by size/1000.
/// </summary>
/// <param name="GlyphId">The glyph index into the run's font.</param>
/// <param name="XAdvance">The horizontal advance after this glyph, in 1000ths of an em.</param>
/// <param name="XOffset">The horizontal placement offset, in 1000ths of an em.</param>
/// <param name="YOffset">The vertical placement offset (up positive), in 1000ths of an em.</param>
/// <param name="Cluster">
/// The cluster this glyph belongs to: for <see cref="TextShaper"/> output, the
/// UTF-16 index in the source text of the cluster's first character (HarfBuzz
/// cluster values: glyphs of one cluster share it, and characters that form one
/// glyph or reorder together merge into one cluster). Since 3.26 for every
/// script; before, Indic runs stored a code point here.
/// </param>
public readonly record struct ShapedGlyph(int GlyphId, double XAdvance, double XOffset, double YOffset, int Cluster);
