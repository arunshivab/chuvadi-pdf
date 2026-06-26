// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.2.2 — Standard Type 1 Fonts (Standard 14 Fonts)
// PHASE: Phase 3 — PDF/A font embedding
//
// Maps the non-embeddable Standard-14 base fonts (and common aliases) to
// metric-compatible Liberation faces that can be embedded for PDF/A.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.PdfA;

/// <summary>A Liberation substitute for a Standard-14 base font.</summary>
/// <param name="Face">The Liberation face key, e.g. "LiberationSans-Regular".</param>
/// <param name="Serif">Whether the face is serif (for the descriptor flag).</param>
internal sealed record Standard14Substitute(string Face, bool Serif);

internal static class Standard14Map
{
    private static readonly Dictionary<string, Standard14Substitute> Map = Build();

    /// <summary>
    /// Resolves a base-font name (subset prefix and style suffixes tolerated) to
    /// a Liberation substitute, or null when the name is not a known Standard-14
    /// font or alias.
    /// </summary>
    /// <param name="baseFont">The /BaseFont name from the font dictionary.</param>
    /// <returns>The substitute, or null when unmapped.</returns>
    internal static Standard14Substitute? Lookup(string baseFont)
    {
        ArgumentNullException.ThrowIfNull(baseFont);
        string name = Normalize(baseFont);
        return Map.TryGetValue(name, out Standard14Substitute? sub) ? sub : null;
    }

    // Strip a 6-uppercase-letter subset prefix ("ABCDEF+") and lowercase.
    private static string Normalize(string baseFont)
    {
        string name = baseFont;
        if (name.Length > 7 && name[6] == '+')
        {
            bool prefix = true;
            for (int i = 0; i < 6; i++)
            {
                if (name[i] < 'A' || name[i] > 'Z')
                {
                    prefix = false;
                    break;
                }
            }

            if (prefix)
            {
                name = name.Substring(7);
            }
        }

        return name.Replace(" ", string.Empty).Replace(",", "-").ToLowerInvariant();
    }

    private static Dictionary<string, Standard14Substitute> Build()
    {
        Standard14Substitute sans = new Standard14Substitute("LiberationSans-Regular", false);
        Standard14Substitute sansB = new Standard14Substitute("LiberationSans-Bold", false);
        Standard14Substitute sansI = new Standard14Substitute("LiberationSans-Italic", false);
        Standard14Substitute sansBI = new Standard14Substitute("LiberationSans-BoldItalic", false);
        Standard14Substitute serif = new Standard14Substitute("LiberationSerif-Regular", true);
        Standard14Substitute serifB = new Standard14Substitute("LiberationSerif-Bold", true);
        Standard14Substitute serifI = new Standard14Substitute("LiberationSerif-Italic", true);
        Standard14Substitute serifBI = new Standard14Substitute("LiberationSerif-BoldItalic", true);
        Standard14Substitute mono = new Standard14Substitute("LiberationMono-Regular", false);
        Standard14Substitute monoB = new Standard14Substitute("LiberationMono-Bold", false);
        Standard14Substitute monoI = new Standard14Substitute("LiberationMono-Italic", false);
        Standard14Substitute monoBI = new Standard14Substitute("LiberationMono-BoldItalic", false);

        return new Dictionary<string, Standard14Substitute>(StringComparer.Ordinal)
        {
            ["helvetica"] = sans,
            ["helvetica-bold"] = sansB,
            ["helvetica-oblique"] = sansI,
            ["helvetica-italic"] = sansI,
            ["helvetica-boldoblique"] = sansBI,
            ["helvetica-bolditalic"] = sansBI,
            ["arial"] = sans,
            ["arial-bold"] = sansB,
            ["arial-boldmt"] = sansB,
            ["arialmt"] = sans,
            ["arial-italic"] = sansI,
            ["arial-italicmt"] = sansI,
            ["arial-bolditalic"] = sansBI,
            ["arial-bolditalicmt"] = sansBI,
            ["times-roman"] = serif,
            ["timesnewroman"] = serif,
            ["timesnewromanpsmt"] = serif,
            ["times-bold"] = serifB,
            ["timesnewromanps-boldmt"] = serifB,
            ["times-italic"] = serifI,
            ["timesnewromanps-italicmt"] = serifI,
            ["times-bolditalic"] = serifBI,
            ["timesnewromanps-bolditalicmt"] = serifBI,
            ["courier"] = mono,
            ["couriernew"] = mono,
            ["couriernewpsmt"] = mono,
            ["courier-bold"] = monoB,
            ["couriernewps-boldmt"] = monoB,
            ["courier-oblique"] = monoI,
            ["courier-italic"] = monoI,
            ["couriernewps-italicmt"] = monoI,
            ["courier-boldoblique"] = monoBI,
            ["courier-bolditalic"] = monoBI,
            ["couriernewps-bolditalicmt"] = monoBI,
        };
    }
}
