// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 Annex D.2 — WinAnsiEncoding (Windows-1252)
// PHASE: Phase 3 — PDF/A font embedding

namespace Chuvadi.Pdf.PdfA;

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
