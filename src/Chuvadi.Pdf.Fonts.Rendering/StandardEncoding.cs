// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Adobe StandardEncoding: maps a 1-byte character code to a glyph name.
/// Used by the Type1 interpreter for the built-in encoding default and for
/// resolving <c>seac</c> accent-composition component codes.
/// </summary>
internal static class StandardEncoding
{
    /// <summary>Returns the glyph name for <paramref name="code"/>, or null.</summary>
    public static string? GetName(int code)
    {
        return code >= 0 && code < 256 ? Names[code] : null;
    }

    private static readonly string?[] Names = BuildNames();

    private static string?[] BuildNames()
    {
        string?[] n = new string?[256];

        // 0x20–0x7E printable ASCII (Adobe StandardEncoding).
        string[] ascii =
        {
            "space", "exclam", "quotedbl", "numbersign", "dollar", "percent", "ampersand", "quoteright",
            "parenleft", "parenright", "asterisk", "plus", "comma", "hyphen", "period", "slash",
            "zero", "one", "two", "three", "four", "five", "six", "seven",
            "eight", "nine", "colon", "semicolon", "less", "equal", "greater", "question",
            "at", "A", "B", "C", "D", "E", "F", "G",
            "H", "I", "J", "K", "L", "M", "N", "O",
            "P", "Q", "R", "S", "T", "U", "V", "W",
            "X", "Y", "Z", "bracketleft", "backslash", "bracketright", "asciicircum", "underscore",
            "quoteleft", "a", "b", "c", "d", "e", "f", "g",
            "h", "i", "j", "k", "l", "m", "n", "o",
            "p", "q", "r", "s", "t", "u", "v", "w",
            "x", "y", "z", "braceleft", "bar", "braceright", "asciitilde",
        };
        for (int i = 0; i < ascii.Length; i++)
        {
            n[0x20 + i] = ascii[i];
        }

        // High codes used by StandardEncoding (incl. accents for seac).
        n[0xA1] = "exclamdown";
        n[0xA2] = "cent";
        n[0xA3] = "sterling";
        n[0xA4] = "fraction";
        n[0xA5] = "yen";
        n[0xA6] = "florin";
        n[0xA7] = "section";
        n[0xA8] = "currency";
        n[0xA9] = "quotesingle";
        n[0xAA] = "quotedblleft";
        n[0xAB] = "guillemotleft";
        n[0xAC] = "guilsinglleft";
        n[0xAD] = "guilsinglright";
        n[0xAE] = "fi";
        n[0xAF] = "fl";
        n[0xB1] = "endash";
        n[0xB2] = "dagger";
        n[0xB3] = "daggerdbl";
        n[0xB4] = "periodcentered";
        n[0xB6] = "paragraph";
        n[0xB7] = "bullet";
        n[0xB8] = "quotesinglbase";
        n[0xB9] = "quotedblbase";
        n[0xBA] = "quotedblright";
        n[0xBB] = "guillemotright";
        n[0xBC] = "ellipsis";
        n[0xBD] = "perthousand";
        n[0xBF] = "questiondown";
        n[0xC1] = "grave";
        n[0xC2] = "acute";
        n[0xC3] = "circumflex";
        n[0xC4] = "tilde";
        n[0xC5] = "macron";
        n[0xC6] = "breve";
        n[0xC7] = "dotaccent";
        n[0xC8] = "dieresis";
        n[0xCA] = "ring";
        n[0xCB] = "cedilla";
        n[0xCD] = "hungarumlaut";
        n[0xCE] = "ogonek";
        n[0xCF] = "caron";
        n[0xD0] = "emdash";
        n[0xE1] = "AE";
        n[0xE3] = "ordfeminine";
        n[0xE8] = "Lslash";
        n[0xE9] = "Oslash";
        n[0xEA] = "OE";
        n[0xEB] = "ordmasculine";
        n[0xF1] = "ae";
        n[0xF5] = "dotlessi";
        n[0xF8] = "lslash";
        n[0xF9] = "oslash";
        n[0xFA] = "oe";
        n[0xFB] = "germandbls";

        return n;
    }
}
