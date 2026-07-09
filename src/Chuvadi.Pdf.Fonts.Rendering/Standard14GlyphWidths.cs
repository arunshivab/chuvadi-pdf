// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Adobe AFM data for the Standard 14 fonts;
//        PDF 32000-1:2008 Annex D — WinAnsiEncoding
// PHASE: Phase 2.1 — glyph-level text positioning

using System;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Per-character widths for the PDF Standard 14 fonts. Widths are in units
/// of 1/1000 em, the standard PDF font metric unit.
/// </summary>
/// <remarks>
/// When a PDF font dictionary does not include a /Widths array — as is
/// permitted for Standard 14 fonts — these widths fill in the gap so that
/// glyph-level positioning works correctly. Delegates to
/// <see cref="Standard14Widths"/>, the single authoritative source of the
/// exact Adobe Core 14 AFM tables, mapping the character through WinAnsi
/// (cp1252); the two width surfaces can never disagree.
/// </remarks>
public static class Standard14GlyphWidths
{
    /// <summary>
    /// Returns true when the given base font name is one of the PDF Standard 14
    /// fonts (Helvetica, Times, Courier families, Symbol, ZapfDingbats).
    /// </summary>
    public static bool IsStandard14(string baseFont)
    {
        if (string.IsNullOrEmpty(baseFont)) { return false; }
        if (baseFont.StartsWith("Helvetica", StringComparison.Ordinal)) { return true; }
        if (baseFont.StartsWith("Times", StringComparison.Ordinal)) { return true; }
        if (baseFont.StartsWith("Courier", StringComparison.Ordinal)) { return true; }
        if (baseFont.Equals("Symbol", StringComparison.Ordinal)) { return true; }
        if (baseFont.Equals("ZapfDingbats", StringComparison.Ordinal)) { return true; }
        return false;
    }

    /// <summary>Returns the width in 1/1000 em of the given character.</summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="baseFont"/> is null.
    /// </exception>
    public static int Width(string baseFont, char ch)
    {
        ArgumentNullException.ThrowIfNull(baseFont);
        return Standard14Widths.GetWidth(NormalizeName(baseFont), ToWinAnsiCode(ch));
    }

    // Maps a Unicode character to its WinAnsi (cp1252) code: Latin-1 is
    // identity; the C1 range 0x80–0x9F carries the cp1252 specials
    // (PDF 32000-1:2008 Annex D). Unmappable characters return 0, which the
    // width tables resolve to the per-font average.
    private static int ToWinAnsiCode(char ch)
    {
        if (ch <= 0x7F || (ch >= 0xA0 && ch <= 0xFF))
        {
            return ch;
        }

        return ch switch
        {
            '\u20AC' => 0x80, // Euro sign
            '\u201A' => 0x82, // single low-9 quotation mark
            '\u0192' => 0x83, // latin small letter f with hook
            '\u201E' => 0x84, // double low-9 quotation mark
            '\u2026' => 0x85, // horizontal ellipsis
            '\u2020' => 0x86, // dagger
            '\u2021' => 0x87, // double dagger
            '\u02C6' => 0x88, // modifier letter circumflex accent
            '\u2030' => 0x89, // per mille sign
            '\u0160' => 0x8A, // latin capital letter s with caron
            '\u2039' => 0x8B, // single left-pointing angle quotation mark
            '\u0152' => 0x8C, // latin capital ligature oe
            '\u017D' => 0x8E, // latin capital letter z with caron
            '\u2018' => 0x91, // left single quotation mark
            '\u2019' => 0x92, // right single quotation mark
            '\u201C' => 0x93, // left double quotation mark
            '\u201D' => 0x94, // right double quotation mark
            '\u2022' => 0x95, // bullet
            '\u2013' => 0x96, // en dash
            '\u2014' => 0x97, // em dash
            '\u02DC' => 0x98, // small tilde
            '\u2122' => 0x99, // trade mark sign
            '\u0161' => 0x9A, // latin small letter s with caron
            '\u203A' => 0x9B, // single right-pointing angle quotation mark
            '\u0153' => 0x9C, // latin small ligature oe
            '\u017E' => 0x9E, // latin small letter z with caron
            '\u0178' => 0x9F, // latin capital letter y with diaeresis
            _ => 0,
        };
    }

    // Maps family prefixes to canonical Standard 14 PostScript names so
    // shorthand base-font values still hit the exact tables.
    private static string NormalizeName(string baseFont)
    {
        if (Standard14Widths.IsStandard14(baseFont))
        {
            return baseFont;
        }

        bool bold = baseFont.Contains("Bold", StringComparison.Ordinal);
        bool italic = baseFont.Contains("Italic", StringComparison.Ordinal)
            || baseFont.Contains("Oblique", StringComparison.Ordinal);

        if (baseFont.StartsWith("Times", StringComparison.Ordinal))
        {
            if (bold && italic) { return "Times-BoldItalic"; }
            if (bold) { return "Times-Bold"; }
            if (italic) { return "Times-Italic"; }
            return "Times-Roman";
        }

        if (baseFont.StartsWith("Helvetica", StringComparison.Ordinal))
        {
            if (bold && italic) { return "Helvetica-BoldOblique"; }
            if (bold) { return "Helvetica-Bold"; }
            if (italic) { return "Helvetica-Oblique"; }
            return "Helvetica";
        }

        if (baseFont.StartsWith("Courier", StringComparison.Ordinal))
        {
            if (bold && italic) { return "Courier-BoldOblique"; }
            if (bold) { return "Courier-Bold"; }
            if (italic) { return "Courier-Oblique"; }
            return "Courier";
        }

        return baseFont;
    }
}
