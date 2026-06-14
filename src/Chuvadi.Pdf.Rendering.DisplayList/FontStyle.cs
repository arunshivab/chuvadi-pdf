// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Slant classification for a text run.</summary>
public enum FontSlant
{
    /// <summary>Upright.</summary>
    Normal = 0,

    /// <summary>True italic (distinct cursive letterforms).</summary>
    Italic = 1,

    /// <summary>Slanted upright (oblique).</summary>
    Oblique = 2,
}

/// <summary>
/// Resolved presentation style for a text run — family, weight, and slant —
/// derived from a font's base name and FontDescriptor. Carried on
/// <see cref="TextOp"/> and surfaced on <see cref="TextRun"/> so callers can
/// reconstruct formatted text.
/// </summary>
public readonly record struct FontStyle(string FontFamily, int Weight, FontSlant Slant, double ItalicAngle)
{
    /// <summary>A neutral upright 400-weight style with no family.</summary>
    public static FontStyle Default => new(string.Empty, 400, FontSlant.Normal, 0.0);

    /// <summary>True when the weight is bold or heavier (>= 600).</summary>
    public bool IsBold => Weight >= 600;

    /// <summary>True when the slant is italic or oblique.</summary>
    public bool IsItalic => Slant != FontSlant.Normal;
}
