// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.8.2, Table 121 (FontDescriptor /Flags).

using System;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Derives a <see cref="FontStyle"/> from a font's base name and, when
/// available, its FontDescriptor <c>/Flags</c>, <c>/ItalicAngle</c>, and
/// <c>/StemV</c>. Used by both the display-list text path and the SVG renderer
/// so style classification stays consistent across consumers. Name heuristics
/// and descriptor signals are combined — either source alone is sufficient to
/// mark a run bold or italic.
/// </summary>
public static class FontStyleClassifier
{
    private const int FlagItalic = 1 << 6;       // Table 121, bit 7
    private const int FlagForceBold = 1 << 18;   // Table 121, bit 19
    private const int BoldStemVThreshold = 140;

    /// <summary>Classifies a font into a <see cref="FontStyle"/>.</summary>
    /// <param name="baseFont">Base font name (subset tag tolerated).</param>
    /// <param name="flags">FontDescriptor <c>/Flags</c>, if known.</param>
    /// <param name="italicAngle">FontDescriptor <c>/ItalicAngle</c>, if known.</param>
    /// <param name="stemV">FontDescriptor <c>/StemV</c>, if known.</param>
    public static FontStyle Classify(string baseFont, int? flags, double? italicAngle, int? stemV)
    {
        ArgumentNullException.ThrowIfNull(baseFont);

        string name = StripSubsetTag(baseFont);
        string family = ExtractFamily(name);

        bool boldByName = ContainsToken(name, "Bold") || ContainsToken(name, "Black")
            || ContainsToken(name, "Heavy");
        bool boldByFlag = flags.HasValue && (flags.Value & FlagForceBold) != 0;
        bool boldByStemV = stemV.HasValue && stemV.Value >= BoldStemVThreshold;
        int weight = boldByName || boldByFlag || boldByStemV ? 700 : 400;

        double angle = italicAngle ?? 0.0;
        FontSlant slant;
        if (ContainsToken(name, "Oblique"))
        {
            slant = FontSlant.Oblique;
        }
        else if (ContainsToken(name, "Italic")
            || (flags.HasValue && (flags.Value & FlagItalic) != 0)
            || Math.Abs(angle) > 0.0)
        {
            slant = FontSlant.Italic;
        }
        else
        {
            slant = FontSlant.Normal;
        }

        return new FontStyle(family, weight, slant, angle);
    }

    private static string StripSubsetTag(string name)
    {
        // Subset fonts are tagged "ABCDEF+RealName".
        if (name.Length > 7 && name[6] == '+')
        {
            bool isTag = true;
            for (int i = 0; i < 6; i++)
            {
                if (name[i] < 'A' || name[i] > 'Z')
                {
                    isTag = false;
                    break;
                }
            }

            if (isTag)
            {
                return name[7..];
            }
        }

        return name;
    }

    private static string ExtractFamily(string name)
    {
        // Family is the portion before the first style separator ('-' or ',').
        int cut = name.Length;
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] == '-' || name[i] == ',')
            {
                cut = i;
                break;
            }
        }

        string family = name[..cut].Trim();
        return family.Length == 0 ? name : family;
    }

    private static bool ContainsToken(string name, string token) =>
        name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
}
