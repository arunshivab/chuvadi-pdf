// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.29 — raised and lowered text in plain text (D-104)

namespace Chuvadi.Pdf.Text;

/// <summary>
/// How extracted plain text writes raised and lowered small text
/// (superscripts and subscripts).
/// </summary>
/// <remarks>
/// Plain text is a derived representation: the PDF is never changed, and
/// <see cref="TextExtractor.ExtractFragments(Chuvadi.Pdf.Documents.PdfPage)"/>
/// still returns every fragment with its own text and its
/// <see cref="Chuvadi.Pdf.Content.TextFragment.BaselineShift"/>, whatever the
/// mode. Text that came from a marked-content <c>/ActualText</c> entry is
/// never rewritten.
/// </remarks>
public enum SuperscriptMode
{
    /// <summary>
    /// The default. A raised ordinal ending after a digit joins it as typed
    /// ("09th"). Other raised or lowered text is written with the Unicode
    /// superscript or subscript characters when every letter and digit has an
    /// exact one ("mc²", "H₂O", "10⁻³", "Cioniniᵃ"); Unicode compatibility
    /// normalisation (NFKC) turns these back into the plain characters. Text
    /// with no exact form stays a separate word, as in
    /// <see cref="Separate"/>.
    /// </summary>
    Unicode,

    /// <summary>
    /// Raised and lowered text joins the text next to it unchanged ("mc2",
    /// "H2O", "Cioninia"), the way common PDF viewers copy it.
    /// </summary>
    Join,

    /// <summary>
    /// Raised and lowered text between letters or digits is kept apart from
    /// them by a space ("mc 2", "Cionini a"), as in 3.28.
    /// </summary>
    Separate,
}
