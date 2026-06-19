// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Adobe AFM data for the Standard 14 fonts
// PHASE: Phase 2.1 — glyph-level text positioning

using System;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Per-character widths for the PDF Standard 14 fonts. Widths are in units
/// of 1/1000 em, the standard PDF font metric unit.
/// </summary>
/// <remarks>
/// When a PDF font dictionary does not include a /Widths array — as is
/// permitted for Standard 14 fonts — these tables fill in the gap so that
/// glyph-level positioning works correctly. Stage 9 will supplement this
/// with full per-glyph outline data from Liberation/URW.
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
    public static int Width(string baseFont, char ch)
    {
        ArgumentNullException.ThrowIfNull(baseFont);
        if (baseFont.StartsWith("Courier", StringComparison.Ordinal)) { return 600; }
        if (baseFont.StartsWith("Times", StringComparison.Ordinal))
        {
            int w = TimesWidth(ch, isBold: baseFont.Contains("Bold"));
            return w == 0 ? AverageOf(baseFont) : w;
        }
        if (baseFont.StartsWith("Helvetica", StringComparison.Ordinal))
        {
            int w = HelveticaWidth(ch, isBold: baseFont.Contains("Bold"));
            return w == 0 ? AverageOf(baseFont) : w;
        }
        return AverageOf(baseFont);
    }

    private static int AverageOf(string baseFont) => baseFont switch
    {
        "Helvetica" or "Helvetica-Oblique" => 528,
        "Helvetica-Bold" or "Helvetica-BoldOblique" => 565,
        "Times-Roman" or "Times-Italic" => 492,
        "Times-Bold" => 518,
        "Times-BoldItalic" => 506,
        "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => 600,
        "Symbol" => 525,
        "ZapfDingbats" => 752,
        _ => 500,
    };

    private static int HelveticaWidth(char ch, bool isBold)
    {
        int w = ch switch
        {
            ' ' => 278,
            '!' => 278,
            '"' => 355,
            '#' => 556,
            '$' => 556,
            '%' => 889,
            '&' => 667,
            '\'' => 191,
            '(' => 333,
            ')' => 333,
            '*' => 389,
            '+' => 584,
            ',' => 278,
            '-' => 333,
            '.' => 278,
            '/' => 278,
            ':' => 278,
            ';' => 278,
            '<' => 584,
            '=' => 584,
            '>' => 584,
            '?' => 556,
            '@' => 1015,
            'A' => 667,
            'B' => 667,
            'C' => 722,
            'D' => 722,
            'E' => 667,
            'F' => 611,
            'G' => 778,
            'H' => 722,
            'I' => 278,
            'J' => 500,
            'K' => 667,
            'L' => 556,
            'M' => 833,
            'N' => 722,
            'O' => 778,
            'P' => 667,
            'Q' => 778,
            'R' => 722,
            'S' => 667,
            'T' => 611,
            'U' => 722,
            'V' => 667,
            'W' => 944,
            'X' => 667,
            'Y' => 667,
            'Z' => 611,
            '[' => 278,
            '\\' => 278,
            ']' => 278,
            '^' => 469,
            '_' => 556,
            '`' => 333,
            'a' => 556,
            'b' => 556,
            'c' => 500,
            'd' => 556,
            'e' => 556,
            'f' => 278,
            'g' => 556,
            'h' => 556,
            'i' => 222,
            'j' => 222,
            'k' => 500,
            'l' => 222,
            'm' => 833,
            'n' => 556,
            'o' => 556,
            'p' => 556,
            'q' => 556,
            'r' => 333,
            's' => 500,
            't' => 278,
            'u' => 556,
            'v' => 500,
            'w' => 722,
            'x' => 500,
            'y' => 500,
            'z' => 500,
            '{' => 334,
            '|' => 260,
            '}' => 334,
            '~' => 584,
            _ => 0,
        };
        if (isBold && w != 0)
        {
            // Bold is slightly wider on average — approximate ratio ~1.07
            w = (int)(w * 1.07);
        }
        return w;
    }

    private static int TimesWidth(char ch, bool isBold)
    {
        int w = ch switch
        {
            ' ' => 250,
            '!' => 333,
            '"' => 408,
            '#' => 500,
            '$' => 500,
            '%' => 833,
            '&' => 778,
            '\'' => 180,
            '(' => 333,
            ')' => 333,
            '*' => 500,
            '+' => 564,
            ',' => 250,
            '-' => 333,
            '.' => 250,
            '/' => 278,
            ':' => 278,
            ';' => 278,
            '<' => 564,
            '=' => 564,
            '>' => 564,
            '?' => 444,
            '@' => 921,
            'A' => 722,
            'B' => 667,
            'C' => 667,
            'D' => 722,
            'E' => 611,
            'F' => 556,
            'G' => 722,
            'H' => 722,
            'I' => 333,
            'J' => 389,
            'K' => 722,
            'L' => 611,
            'M' => 889,
            'N' => 722,
            'O' => 722,
            'P' => 556,
            'Q' => 722,
            'R' => 667,
            'S' => 556,
            'T' => 611,
            'U' => 722,
            'V' => 722,
            'W' => 944,
            'X' => 722,
            'Y' => 722,
            'Z' => 611,
            '[' => 333,
            '\\' => 278,
            ']' => 333,
            '^' => 469,
            '_' => 500,
            '`' => 333,
            'a' => 444,
            'b' => 500,
            'c' => 444,
            'd' => 500,
            'e' => 444,
            'f' => 333,
            'g' => 500,
            'h' => 500,
            'i' => 278,
            'j' => 278,
            'k' => 500,
            'l' => 278,
            'm' => 778,
            'n' => 500,
            'o' => 500,
            'p' => 500,
            'q' => 500,
            'r' => 333,
            's' => 389,
            't' => 278,
            'u' => 500,
            'v' => 500,
            'w' => 722,
            'x' => 500,
            'y' => 500,
            'z' => 444,
            '{' => 480,
            '|' => 200,
            '}' => 480,
            '~' => 541,
            _ => 0,
        };
        if (isBold && w != 0) { w = (int)(w * 1.05); }
        return w;
    }
}
