// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 Annex D.2 — WinAnsiEncoding (Windows-1252)
// PHASE: Phase 3 — PDF/A font embedding; 3.35.0 (AUT-01) encoding text for
//        the standard fonts

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// The WinAnsiEncoding (Windows-1252) character-code to Unicode mapping, used to
/// build a Unicode cmap when embedding a substitute font for a non-embedded
/// simple font that declares <c>/Encoding /WinAnsiEncoding</c>.
/// </summary>
internal static class WinAnsiEncoding
{
    // Codes 0x80–0x9F differ from Latin-1; 0 marks an unmapped slot.
    private static readonly int[] High =
    {
        0x20AC, 0x0000, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021,
        0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0x0000, 0x017D, 0x0000,
        0x0000, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014,
        0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0x0000, 0x017E, 0x0178,
    };

    // Unicode scalar value to WinAnsi code, built once.
    private static readonly Dictionary<int, byte> UnicodeToCode = BuildReverse();

    /// <summary>
    /// True when <paramref name="scalar"/> has a WinAnsi code (or is a tab,
    /// carriage return or line feed, which are written as they are).
    /// </summary>
    /// <param name="scalar">A Unicode scalar value.</param>
    /// <returns>Whether the character can be written for a standard font.</returns>
    internal static bool CanEncode(int scalar) =>
        scalar is '\r' or '\n' or '\t' || UnicodeToCode.ContainsKey(scalar);

    /// <summary>
    /// Encodes text for a standard font with <c>/Encoding /WinAnsiEncoding</c>
    /// (3.35.0, AUT-01). Each character is mapped through the WinAnsi table
    /// ('€' to 0x80, '–' to 0x96, '“' to 0x93, and so on); before, the low
    /// byte of each character was written, which mangled those.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="fontName">The font's name, for the error message.</param>
    /// <returns>One code per character.</returns>
    /// <exception cref="ArgumentException">
    /// A character has no WinAnsi code (for example Tamil, Greek or CJK).
    /// The message names the character and points to embedding a font.
    /// </exception>
    internal static byte[] Encode(string text, string fontName)
    {
        byte[] codes = new byte[text.Length];
        int n = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch is '\r' or '\n' or '\t')
            {
                codes[n++] = (byte)ch;
                continue;
            }

            int scalar = ch;
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                scalar = char.ConvertToUtf32(ch, text[i + 1]);
            }

            if (!UnicodeToCode.TryGetValue(scalar, out byte code))
            {
                string shown = char.ConvertFromUtf32(scalar);
                throw new ArgumentException(
                    "The text contains '" + shown + "' (U+" + scalar.ToString("X4", CultureInfo.InvariantCulture)
                    + "), which the standard font " + fontName + " cannot show: the standard fonts use WinAnsiEncoding"
                    + " (Western European letters and common punctuation). Add a font that has the character with"
                    + " PdfDocumentBuilder.AddFont and draw the text with it.",
                    nameof(text));
            }

            codes[n++] = code;
        }

        return n == codes.Length ? codes : codes.AsSpan(0, n).ToArray();
    }

    /// <summary>
    /// Writes codes as the body of a PDF literal string (without the
    /// parentheses): printable ASCII as is, the delimiters escaped, other
    /// codes as octal escapes.
    /// </summary>
    /// <param name="codes">The codes.</param>
    /// <returns>The escaped body.</returns>
    internal static string ToLiteralBody(byte[] codes)
    {
        StringBuilder sb = new StringBuilder(codes.Length + 8);
        foreach (byte b in codes)
        {
            switch (b)
            {
                case (byte)'\\': sb.Append("\\\\"); break;
                case (byte)'(': sb.Append("\\("); break;
                case (byte)')': sb.Append("\\)"); break;
                case (byte)'\r': sb.Append("\\r"); break;
                case (byte)'\n': sb.Append("\\n"); break;
                case (byte)'\t': sb.Append("\\t"); break;
                default:
                    if (b < 0x20 || b > 0x7E)
                    {
                        sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    }
                    else
                    {
                        sb.Append((char)b);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    private static Dictionary<int, byte> BuildReverse()
    {
        int[] map = CodeToUnicode();
        Dictionary<int, byte> reverse = new Dictionary<int, byte>();
        for (int code = 255; code >= 0x20; code--)
        {
            if (map[code] != 0)
            {
                reverse[map[code]] = (byte)code;
            }
        }

        return reverse;
    }

    /// <summary>
    /// Builds the 256-entry code-to-Unicode table for WinAnsiEncoding. Unmapped
    /// codes are 0.
    /// </summary>
    /// <returns>A 256-element array mapping byte code to Unicode scalar value.</returns>
    internal static int[] CodeToUnicode()
    {
        int[] map = new int[256];
        for (int code = 0; code < 256; code++)
        {
            if (code >= 0x80 && code <= 0x9F)
            {
                map[code] = High[code - 0x80];
            }
            else if (code >= 0x20)
            {
                map[code] = code;
            }
            else
            {
                map[code] = 0;
            }
        }

        return map;
    }
}
