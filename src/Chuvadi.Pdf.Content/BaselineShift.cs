// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.29 — raised and lowered text as structured data (D-104)

namespace Chuvadi.Pdf.Content;

/// <summary>
/// Whether a <see cref="TextFragment"/> sits on the baseline of the line it
/// belongs to, or is raised (a superscript) or lowered (a subscript) off it.
/// </summary>
/// <remarks>
/// Set by text extraction (Chuvadi.Pdf.Text) from the fragment's size and
/// position against the main text of its line; the content-stream parser
/// alone reports <see cref="None"/>. Lets a consumer render real superscripts
/// and subscripts (for example HTML <c>&lt;sup&gt;</c> and <c>&lt;sub&gt;</c>)
/// without inspecting characters.
/// </remarks>
public enum BaselineShift
{
    /// <summary>On the line's baseline, or not classified.</summary>
    None,

    /// <summary>Smaller than the line's main text and raised above its baseline.</summary>
    Raised,

    /// <summary>Smaller than the line's main text and lowered below its baseline.</summary>
    Lowered,
}
