// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.2 — Chuvadi.Pdf.Redaction pattern-based extension
// Resolves regex matches in extracted text to device-space rectangles.

using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Text;

namespace Chuvadi.Pdf.Redaction;

/// <summary>
/// Resolves <see cref="PatternRule"/> matches against a page's extracted text
/// into <see cref="RedactionRect"/> values.
/// </summary>
/// <remarks>
/// Algorithm:
/// 1. Extract positioned text fragments via <see cref="TextExtractor.ExtractFragments"/>.
/// 2. Concatenate fragments into a single search string with a character-index
///    mapping back to each character's originating fragment.
/// 3. Run each pattern against the concatenated text.
/// 4. For each match, emit one rectangle per contiguous fragment span the match
///    overlaps — a match crossing a TJ entry boundary yields multiple rectangles.
///
/// Limitations:
/// - Glyph widths are approximated as 0.5 × font size (Helvetica-ish). Per-glyph
///   widths from embedded font tables are not consulted here. Compensate with
///   <see cref="RedactionOptions.PatternPadding"/>.
/// - Matches that span line breaks split into per-line rectangles, which is
///   typically what callers want.
/// </remarks>
internal static class PatternMatcher
{
    /// <summary>
    /// Resolves all pattern rules applicable to the given page into redaction rectangles.
    /// </summary>
    public static List<RedactionRect> Resolve(
        PdfDocument document,
        int pageIndex,
        IEnumerable<PatternRule> patterns,
        double padding)
    {
        List<RedactionRect> result = new List<RedactionRect>();

        TextExtractor extractor = new TextExtractor(document.Objects, ExtractionStrategy.Operator);
        List<TextFragment> fragments = extractor.ExtractFragments(document.Pages[pageIndex]);

        if (fragments.Count == 0)
        {
            return result;
        }

        // Build flat text + char-to-fragment map.
        StringBuilder sb = new StringBuilder();
        List<int> charToFragment = new List<int>();

        for (int i = 0; i < fragments.Count; i++)
        {
            string text = fragments[i].Text;

            for (int j = 0; j < text.Length; j++)
            {
                sb.Append(text[j]);
                charToFragment.Add(i);
            }

            // Word-boundary separator between fragments.
            sb.Append(' ');
            charToFragment.Add(i);
        }

        string flatText = sb.ToString();

        foreach (PatternRule rule in patterns)
        {
            if (!rule.AppliesToPage(pageIndex))
            {
                continue;
            }

            MatchCollection matches = rule.Regex.Matches(flatText);

            foreach (Match match in matches)
            {
                // A rule may carry a post-match validator (e.g. a checksum) that
                // rejects false regex hits; skip matches it declines.
                if (rule.Validator is not null && !rule.Validator(match.Value))
                {
                    continue;
                }

                int start = match.Index;
                int end = match.Index + match.Length;

                if (end > charToFragment.Count)
                {
                    continue;
                }

                AddMatchRects(fragments, charToFragment, start, end, pageIndex, padding, result);
            }
        }

        return result;
    }

    private static void AddMatchRects(
        List<TextFragment> fragments,
        List<int> charToFragment,
        int start,
        int end,
        int pageIndex,
        double padding,
        List<RedactionRect> result)
    {
        int i = start;

        while (i < end)
        {
            int fragIdx = charToFragment[i];
            int spanStart = i;

            while (i < end && charToFragment[i] == fragIdx)
            {
                i++;
            }

            int spanEnd = i;
            TextFragment frag = fragments[fragIdx];
            int fragmentFlatStart = FindFragmentFlatStart(charToFragment, fragIdx);
            int charsBeforeSpan = spanStart - fragmentFlatStart;
            int spanCharCount = spanEnd - spanStart;

            // 0.5 × font size: Helvetica-ish average advance. Embedded-font widths
            // would be more accurate but require the Font subsystem at this layer.
            double approxCharWidth = frag.FontSize * 0.5;

            double x = frag.X + (charsBeforeSpan * approxCharWidth) - padding;
            double y = frag.Y - (padding * 0.5);
            double width = (spanCharCount * approxCharWidth) + (padding * 2);
            double height = frag.FontSize + padding;

            if (width <= 0 || height <= 0)
            {
                continue;
            }

            result.Add(new RedactionRect(pageIndex, new RectangleF(x, y, width, height)));
        }
    }

    private static int FindFragmentFlatStart(List<int> charToFragment, int fragIdx)
    {
        for (int i = 0; i < charToFragment.Count; i++)
        {
            if (charToFragment[i] == fragIdx)
            {
                return i;
            }
        }

        return 0;
    }
}
