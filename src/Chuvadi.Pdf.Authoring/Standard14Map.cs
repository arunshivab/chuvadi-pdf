// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.2.2 — Standard Type 1 Fonts (Standard 14 Fonts)
// PHASE: Phase 3 — PDF/A font embedding
//
// Maps the non-embeddable Standard-14 base fonts (and common aliases) to
// metric-compatible Liberation faces that can be embedded for PDF/A.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Authoring;

/// <summary>A Liberation substitute for a Standard-14 base font.</summary>
/// <param name="Face">The Liberation face key, e.g. "LiberationSans-Regular".</param>
/// <param name="Serif">Whether the face is serif (for the descriptor flag).</param>
internal sealed record LiberationSubstitute(string Face, bool Serif);

internal static class Standard14Map
{
    private static readonly Dictionary<string, LiberationSubstitute> Map = Build();

    /// <summary>
    /// Resolves a base-font name (subset prefix and style suffixes tolerated) to
    /// a Liberation substitute, or null when the name is not a known Standard-14
    /// font or alias.
    /// </summary>
    /// <param name="baseFont">The /BaseFont name from the font dictionary.</param>
    /// <returns>The substitute, or null when unmapped.</returns>
    internal static LiberationSubstitute? Lookup(string baseFont)
    {
        ArgumentNullException.ThrowIfNull(baseFont);
        string name = Normalize(baseFont);
        return Map.TryGetValue(name, out LiberationSubstitute? sub) ? sub : null;
    }

    /// <summary>
    /// True when <paramref name="baseFont"/> is one of the twelve standard 14
    /// text font names (subset prefix tolerated), not an alias such as Arial.
    /// </summary>
    /// <param name="baseFont">The /BaseFont name from the font dictionary.</param>
    /// <returns>True for a standard 14 text font name.</returns>
    internal static bool IsStandard14Name(string baseFont)
    {
        ArgumentNullException.ThrowIfNull(baseFont);
        return Normalize(baseFont) is "helvetica" or "helvetica-bold" or "helvetica-oblique" or "helvetica-boldoblique"
            or "times-roman" or "times-bold" or "times-italic" or "times-bolditalic"
            or "courier" or "courier-bold" or "courier-oblique" or "courier-boldoblique";
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

    private static Dictionary<string, LiberationSubstitute> Build()
    {
        LiberationSubstitute sans = new LiberationSubstitute("LiberationSans-Regular", false);
        LiberationSubstitute sansB = new LiberationSubstitute("LiberationSans-Bold", false);
        LiberationSubstitute sansI = new LiberationSubstitute("LiberationSans-Italic", false);
        LiberationSubstitute sansBI = new LiberationSubstitute("LiberationSans-BoldItalic", false);
        LiberationSubstitute serif = new LiberationSubstitute("LiberationSerif-Regular", true);
        LiberationSubstitute serifB = new LiberationSubstitute("LiberationSerif-Bold", true);
        LiberationSubstitute serifI = new LiberationSubstitute("LiberationSerif-Italic", true);
        LiberationSubstitute serifBI = new LiberationSubstitute("LiberationSerif-BoldItalic", true);
        LiberationSubstitute mono = new LiberationSubstitute("LiberationMono-Regular", false);
        LiberationSubstitute monoB = new LiberationSubstitute("LiberationMono-Bold", false);
        LiberationSubstitute monoI = new LiberationSubstitute("LiberationMono-Italic", false);
        LiberationSubstitute monoBI = new LiberationSubstitute("LiberationMono-BoldItalic", false);

        return new Dictionary<string, LiberationSubstitute>(StringComparer.Ordinal)
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
