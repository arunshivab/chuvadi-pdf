// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.5 — Character encoding
//        PDF 32000-1:2008 §9.6.6 — Differences array
// PHASE: Phase 1 — Chuvadi.Pdf.Fonts
// Maps 1-byte character codes to glyph names, then to Unicode.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Fonts;

/// <summary>
/// Maps 1-byte character codes (0-255) to Unicode codepoints for simple fonts.
/// </summary>
/// <remarks>
/// PDF simple fonts use a single-byte encoding. The encoding may be:
/// <list type="bullet">
///   <item>A named standard encoding (WinAnsiEncoding, MacRomanEncoding, etc.)</item>
///   <item>A font's built-in encoding</item>
///   <item>A custom encoding with a /Differences array that overrides specific codes</item>
/// </list>
/// This class builds the code→Unicode map from an encoding dictionary,
/// falling back to WinAnsiEncoding when no encoding is specified.
///
/// PDF 32000-1:2008 §9.6.5, §9.6.6.
/// </remarks>
public sealed class PdfFontEncoding
{
    // The 256-entry map: index = char code, value = Unicode char (or \0 if unmapped).
    private readonly char[] _map;

    private PdfFontEncoding(char[] map)
    {
        _map = map;
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an encoding from an /Encoding entry in a font dictionary.
    /// </summary>
    /// <param name="encoding">
    /// The /Encoding value — either a PdfName (named encoding) or a
    /// PdfDictionary (custom encoding with optional Differences).
    /// May be null, in which case WinAnsiEncoding is used.
    /// </param>
    public static PdfFontEncoding Build(PdfPrimitive? encoding)
    {
        if (encoding is null)
        {
            return FromNamedEncoding("WinAnsiEncoding");
        }

        if (encoding is PdfName named)
        {
            return FromNamedEncoding(named.Value);
        }

        if (encoding is PdfDictionary dict)
        {
            return FromEncodingDictionary(dict);
        }

        return FromNamedEncoding("WinAnsiEncoding");
    }

    /// <summary>
    /// Returns a <see cref="PdfFontEncoding"/> for a standard named encoding.
    /// </summary>
    public static PdfFontEncoding FromNamedEncoding(string name)
    {
        char[] map = name switch
        {
            "WinAnsiEncoding" => BuildWinAnsi(),
            "MacRomanEncoding" => BuildMacRoman(),
            "StandardEncoding" => BuildStandard(),
            "PDFDocEncoding" => BuildPdfDoc(),
            _ => BuildWinAnsi()
        };

        return new PdfFontEncoding(map);
    }

    /// <summary>
    /// Builds an encoding for a font whose built-in encoding is known (3.31,
    /// D-172): an encoding dictionary without <c>/BaseEncoding</c> starts from
    /// <paramref name="builtIn"/> (PDF 32000-1:2008 §9.6.6.1, Table 114).
    /// </summary>
    internal static PdfFontEncoding Build(PdfPrimitive? encoding, string? builtIn)
    {
        return encoding is PdfDictionary dict && builtIn is not null
            ? FromEncodingDictionary(dict, builtIn.ToCharArray())
            : Build(encoding);
    }

    // A built-in encoding table (code to Unicode) as an encoding (3.31, D-176).
    internal static PdfFontEncoding FromTable(string table) => new PdfFontEncoding(table.ToCharArray());

    // True for an encoding dictionary with /Differences and no /BaseEncoding,
    // whose base is the font's implicit built-in encoding (Table 114).
    internal static bool HasImplicitBase(PdfPrimitive? encoding) =>
        encoding is PdfDictionary dict
        && dict.GetName(PdfName.Intern("BaseEncoding")) is null
        && dict.GetArray(PdfName.Intern("Differences")) is not null;

    // The built-in encoding of a standard 14 font, by its /BaseFont name (a
    // subset prefix such as "ABCDEF+" is ignored), or null for other fonts.
    internal static string? Standard14BuiltIn(string? baseFont)
    {
        if (baseFont is null)
        {
            return null;
        }

        string name = baseFont.Length > 7 && baseFont[6] == '+' ? baseFont[7..] : baseFont;
        return name switch
        {
            "Symbol" => BuiltInEncodings.Symbol,
            "ZapfDingbats" => BuiltInEncodings.ZapfDingbats,
            "Helvetica" or "Helvetica-Bold" or "Helvetica-Oblique" or "Helvetica-BoldOblique"
                or "Times-Roman" or "Times-Bold" or "Times-Italic" or "Times-BoldItalic"
                or "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => BuiltInEncodings.Standard,
            _ => null,
        };
    }

    private static PdfFontEncoding FromEncodingDictionary(PdfDictionary dict) => FromEncodingDictionary(dict, null);

    private static PdfFontEncoding FromEncodingDictionary(PdfDictionary dict, char[]? builtIn)
    {
        // Start from the base encoding: /BaseEncoding when present, else the
        // font's built-in encoding when known (3.31), else WinAnsiEncoding.
        PdfName? baseName = dict.GetName(PdfName.Intern("BaseEncoding"));
        char[] map = baseName is not null
            ? FromNamedEncoding(baseName.Value)._map.Clone() as char[] ?? BuildWinAnsi()
            : builtIn ?? BuildWinAnsi();

        // Apply /Differences array.
        // Format: [code name name name code name ...] where each integer
        // sets the starting code for the following glyph names.
        PdfArray? differences = dict.GetArray(PdfName.Intern("Differences"));

        if (differences is not null)
        {
            int currentCode = 0;

            for (int i = 0; i < differences.Count; i++)
            {
                PdfPrimitive item = differences[i];

                if (item is PdfInteger intItem)
                {
                    currentCode = intItem.Value;
                }
                else if (item is PdfName nameItem)
                {
                    if (currentCode >= 0 && currentCode < 256)
                    {
                        map[currentCode] = GlyphNameToUnicode(nameItem.Value);
                    }

                    currentCode++;
                }
            }
        }

        return new PdfFontEncoding(map);
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a 1-byte character code to a Unicode character.
    /// Returns '\0' when the code is not mapped.
    /// </summary>
    public char GetCharacter(byte code) => _map[code];

    /// <summary>
    /// Returns true when the character code has a Unicode mapping.
    /// </summary>
    public bool IsMapped(byte code) => _map[code] != '\0';

    // ── Glyph name to Unicode ─────────────────────────────────────────────

    /// <summary>
    /// Maps a PDF glyph name to its Unicode codepoint.
    /// Returns '\0' for unknown names.
    /// Covers the Adobe Glyph List subset most commonly seen in PDFs.
    /// PDF 32000-1:2008 §9.6.6 — use of glyph names.
    /// </summary>
    public static char GlyphNameToUnicode(string name)
    {
        if (name is null || name.Length == 0)
        {
            return '\0';
        }

        // Handle uniXXXX and uXXXX notation first.
        if (name.StartsWith("uni", StringComparison.Ordinal) && name.Length == 7)
        {
            if (int.TryParse(name[3..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int cp))
            {
                return cp < 0x10000 ? (char)cp : '\0';
            }
        }

        if (name.StartsWith("u", StringComparison.Ordinal) && name.Length >= 5 && name.Length <= 7)
        {
            if (int.TryParse(name[1..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int cp))
            {
                return cp < 0x10000 ? (char)cp : '\0';
            }
        }

        if (AdobeGlyphList.TryGetValue(name, out char c))
        {
            return c;
        }

        // 3.31 (D-172): every other name of the Adobe Glyph List ("A",
        // "eacute", "one", ...). Before, these resolved to nothing, so a
        // /Differences entry naming them was ignored.
        return Chuvadi.Pdf.Fonts.Rendering.AdobeGlyphList.TryGetCodePoint(name, out int scalar) && scalar < 0x10000
            ? (char)scalar
            : '\0';
    }

    // ── Standard encoding tables ──────────────────────────────────────────

    private static char[] BuildWinAnsi()
    {
        // Windows-1252 / WinAnsiEncoding.
        // For codes 0x20-0xFF, this matches ISO-8859-1 except for 0x80-0x9F.
        char[] map = new char[256];

        for (int i = 0x20; i <= 0x7E; i++)
        {
            map[i] = (char)i;
        }

        // 0x80-0x9F Windows-1252 specific overrides.
        map[0x80] = '\u20AC'; // Euro sign
        map[0x82] = '\u201A'; // Single low-9 quotation mark
        map[0x83] = '\u0192'; // Latin small letter f with hook
        map[0x84] = '\u201E'; // Double low-9 quotation mark
        map[0x85] = '\u2026'; // Horizontal ellipsis
        map[0x86] = '\u2020'; // Dagger
        map[0x87] = '\u2021'; // Double dagger
        map[0x88] = '\u02C6'; // Modifier letter circumflex accent
        map[0x89] = '\u2030'; // Per mille sign
        map[0x8A] = '\u0160'; // Latin capital letter S with caron
        map[0x8B] = '\u2039'; // Single left-pointing angle quotation mark
        map[0x8C] = '\u0152'; // Latin capital ligature OE
        map[0x8E] = '\u017D'; // Latin capital letter Z with caron
        map[0x91] = '\u2018'; // Left single quotation mark
        map[0x92] = '\u2019'; // Right single quotation mark
        map[0x93] = '\u201C'; // Left double quotation mark
        map[0x94] = '\u201D'; // Right double quotation mark
        map[0x95] = '\u2022'; // Bullet
        map[0x96] = '\u2013'; // En dash
        map[0x97] = '\u2014'; // Em dash
        map[0x98] = '\u02DC'; // Small tilde
        map[0x99] = '\u2122'; // Trade mark sign
        map[0x9A] = '\u0161'; // Latin small letter s with caron
        map[0x9B] = '\u203A'; // Single right-pointing angle quotation mark
        map[0x9C] = '\u0153'; // Latin small ligature oe
        map[0x9E] = '\u017E'; // Latin small letter z with caron
        map[0x9F] = '\u0178'; // Latin capital letter Y with diaeresis

        for (int i = 0xA0; i <= 0xFF; i++)
        {
            map[i] = (char)i;
        }

        return map;
    }

    private static char[] BuildMacRoman()
    {
        // Mac OS Roman encoding for codes 0x80-0xFF.
        char[] map = new char[256];

        for (int i = 0x20; i <= 0x7E; i++)
        {
            map[i] = (char)i;
        }

        char[] highBytes =
        [
            '\u00C4', '\u00C5', '\u00C7', '\u00C9', '\u00D1', '\u00D6', '\u00DC', '\u00E1',
            '\u00E0', '\u00E2', '\u00E4', '\u00E5', '\u00E7', '\u00E9', '\u00E8', '\u00EA',
            '\u00EB', '\u00ED', '\u00EC', '\u00EE', '\u00EF', '\u00F1', '\u00F3', '\u00F2',
            '\u00F4', '\u00F6', '\u00FA', '\u00F9', '\u00FB', '\u00FC', '\u2020', '\u00B0',
            '\u00A2', '\u00A3', '\u00A7', '\u2022', '\u00B6', '\u00DF', '\u00AE', '\u00A9',
            '\u2122', '\u00B4', '\u00A8', '\u2260', '\u00C6', '\u00D8', '\u221E', '\u00B1',
            '\u2264', '\u2265', '\u00A5', '\u00B5', '\u2202', '\u2211', '\u220F', '\u03C0',
            '\u222B', '\u00AA', '\u00BA', '\u03A9', '\u00E6', '\u00F8', '\u00BF', '\u00A1',
            '\u00AC', '\u221A', '\u0192', '\u2248', '\u2206', '\u00AB', '\u00BB', '\u2026',
            '\u00A0', '\u00C0', '\u00C3', '\u00D5', '\u0152', '\u0153', '\u2013', '\u2014',
            '\u201C', '\u201D', '\u2018', '\u2019', '\u00F7', '\u25CA', '\u00FF', '\u0178',
            '\u2044', '\u20AC', '\u2039', '\u203A', '\uFB01', '\uFB02', '\u2021', '\u00B7',
            '\u201A', '\u201E', '\u2030', '\u00C2', '\u00CA', '\u00C1', '\u00CB', '\u00C8',
            '\u00CD', '\u00CE', '\u00CF', '\u00CC', '\u00D3', '\u00D4', '\uF8FF', '\u00D2',
            '\u00DA', '\u00DB', '\u00D9', '\u0131', '\u02C6', '\u02DC', '\u00AF', '\u02D8',
            '\u02D9', '\u02DA', '\u00B8', '\u02DD', '\u02DB', '\u02C7',
        ];

        for (int i = 0; i < highBytes.Length && i + 0x80 < 256; i++)
        {
            map[i + 0x80] = highBytes[i];
        }

        return map;
    }

    // StandardEncoding, all 149 codes (3.31; before, only 0x20-0x7E).
    private static char[] BuildStandard() => BuiltInEncodings.Standard.ToCharArray();

    private static char[] BuildPdfDoc()
    {
        // PDFDocEncoding — similar to ISO-8859-1 with overrides in 0x18-0x1F and 0x80-0x9F.
        char[] map = new char[256];

        for (int i = 0x20; i <= 0xFF; i++)
        {
            map[i] = (char)i;
        }

        // Override 0x80-0x9F with Unicode values (same as WinAnsi for common cases).
        map[0x80] = '\u2022'; map[0x81] = '\u2020'; map[0x82] = '\u2021';
        map[0x83] = '\u2026'; map[0x84] = '\u2014'; map[0x85] = '\u2013';
        map[0x86] = '\u0192'; map[0x87] = '\u2044'; map[0x88] = '\u2039';
        map[0x89] = '\u203A'; map[0x8A] = '\u2212'; map[0x8B] = '\u2030';
        map[0x8C] = '\u201E'; map[0x8D] = '\u201C'; map[0x8E] = '\u201D';
        map[0x8F] = '\u2018'; map[0x90] = '\u2019'; map[0x91] = '\u201A';
        map[0x92] = '\u2122'; map[0x93] = '\uFB01'; map[0x94] = '\uFB02';
        map[0x95] = '\u0141'; map[0x96] = '\u0152'; map[0x97] = '\u0160';
        map[0x98] = '\u0178'; map[0x99] = '\u017D'; map[0x9A] = '\u0131';
        map[0x9B] = '\u0142'; map[0x9C] = '\u0153'; map[0x9D] = '\u0161';
        map[0x9E] = '\u017E'; map[0xA0] = '\u20AC';
        return map;
    }

    // ── Adobe Glyph List (subset) ─────────────────────────────────────────
    // The full AGL has ~4000 entries. This covers the most common glyph names
    // seen in real-world PDFs. Extended on demand.

    private static readonly Dictionary<string, char> AdobeGlyphList =
        new Dictionary<string, char>(StringComparer.Ordinal)
        {
            { "space", ' ' }, { "exclam", '!' }, { "quotedbl", '"' },
            { "numbersign", '#' }, { "dollar", '$' }, { "percent", '%' },
            { "ampersand", '&' }, { "quotesingle", '\'' }, { "parenleft", '(' },
            { "parenright", ')' }, { "asterisk", '*' }, { "plus", '+' },
            { "comma", ',' }, { "hyphen", '-' }, { "period", '.' },
            { "slash", '/' }, { "colon", ':' }, { "semicolon", ';' },
            { "less", '<' }, { "equal", '=' }, { "greater", '>' },
            { "question", '?' }, { "at", '@' }, { "bracketleft", '[' },
            { "backslash", '\\' }, { "bracketright", ']' }, { "asciicircum", '^' },
            { "underscore", '_' }, { "grave", '`' }, { "braceleft", '{' },
            { "bar", '|' }, { "braceright", '}' }, { "asciitilde", '~' },
            { "bullet", '\u2022' }, { "dagger", '\u2020' }, { "daggerdbl", '\u2021' },
            { "ellipsis", '\u2026' }, { "emdash", '\u2014' }, { "endash", '\u2013' },
            { "fi", '\uFB01' }, { "fl", '\uFB02' },
            { "florin", '\u0192' }, { "fraction", '\u2044' },
            { "guilsinglleft", '\u2039' }, { "guilsinglright", '\u203A' },
            { "quotedblbase", '\u201E' }, { "quotedblleft", '\u201C' },
            { "quotedblright", '\u201D' }, { "quoteleft", '\u2018' },
            { "quoteright", '\u2019' }, { "quotesinglbase", '\u201A' },
            { "trademark", '\u2122' }, { "Euro", '\u20AC' },
            { "minus", '\u2212' }, { "perthousand", '\u2030' },
            { "OE", '\u0152' }, { "oe", '\u0153' },
            { "Scaron", '\u0160' }, { "scaron", '\u0161' },
            { "Ydieresis", '\u0178' }, { "Zcaron", '\u017D' }, { "zcaron", '\u017E' },
            { "dotlessi", '\u0131' }, { "Lslash", '\u0141' }, { "lslash", '\u0142' },
            { "nbspace", '\u00A0' }, { "softhyphen", '\u00AD' },
            { "registered", '\u00AE' }, { "copyright", '\u00A9' },
            { "degree", '\u00B0' }, { "plusminus", '\u00B1' },
            { "mu", '\u00B5' }, { "periodcentered", '\u00B7' },
            { "ae", '\u00E6' }, { "AE", '\u00C6' },
            { "oslash", '\u00F8' }, { "Oslash", '\u00D8' },
            { "germandbls", '\u00DF' }, { "questiondown", '\u00BF' },
            { "exclamdown", '\u00A1' },
        };
}
