// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Unicode Bidirectional Algorithm (UAX #9)
// PHASE: v2.0.0 R3 — Text run extraction

namespace Chuvadi.Pdf.Text;

/// <summary>
/// Directional flow of a <see cref="TextRun"/>.
/// </summary>
/// <remarks>
/// The direction is inferred from the Unicode characters in the run by
/// <see cref="TextRunBuilder"/>. Mixed-direction runs (a single fragment
/// containing both LTR and RTL text) are reported as the direction of
/// the dominant strong-directional script in the run; bidi reordering
/// of glyph positions is a v2.1 feature.
/// </remarks>
public enum TextDirection
{
    /// <summary>
    /// Left-to-right text (Latin, Cyrillic, Greek, Indic, CJK, etc.).
    /// </summary>
    LeftToRight = 0,

    /// <summary>
    /// Right-to-left text (Arabic, Hebrew, Syriac, Thaana, N'Ko).
    /// </summary>
    RightToLeft = 1,

    /// <summary>
    /// Top-to-bottom text, used in some vertical CJK and historical layouts.
    /// Reserved; the v2.0.0 builder never returns this value.
    /// </summary>
    TopToBottom = 2,
}
