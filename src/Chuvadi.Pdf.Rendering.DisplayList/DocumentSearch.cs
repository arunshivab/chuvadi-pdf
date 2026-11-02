// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — text search

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.Documents;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Searches the text of a <see cref="PdfDocument"/> by page, streaming
/// matches asynchronously.
/// </summary>
/// <remarks>
/// Builds on <see cref="PdfPageExtensions.GetTextRuns"/>. Concatenates the
/// page's text runs in reading order, performs a sliding-window string
/// search, and emits matches as they're found. Cancellation is checked
/// between pages and between matches.
/// </remarks>
public static class DocumentSearch
{
    /// <summary>Searches the document for occurrences of <paramref name="query"/>.</summary>
    public static async IAsyncEnumerable<SearchMatch> SearchAsync(
        PdfDocument document,
        string query,
        SearchOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(query);

        SearchOptions opts = options ?? new SearchOptions();
        if (query.Length == 0) { yield break; }

        int startPage = Math.Max(0, opts.PageRangeStart ?? 0);
        int endPage = Math.Min(document.PageCount, opts.PageRangeEnd ?? document.PageCount);

        StringComparison comparison = opts.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        for (int pageIdx = startPage; pageIdx < endPage; pageIdx++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Build the page's logical text + per-character run+offset map.
            (string text, List<int> runIndex, List<int> charOffsetInRun, IReadOnlyList<TextRun> runs)
                = await Task.Run(() => BuildPageText(document, pageIdx), cancellationToken)
                    .ConfigureAwait(false);

            if (text.Length == 0) { continue; }

            // Compare in compatibility decomposition when asked (3.29); the
            // folded text maps each of its characters back to the page text.
            string haystack = text;
            string needle = query;
            List<int>? origin = null;
            if (opts.MatchCompatibilityForms)
            {
                origin = new List<int>(text.Length);
                haystack = Fold(text, origin);
                needle = Fold(query, null);
                if (needle.Length == 0) { continue; }
            }

            // Find matches in the concatenated text.
            int from = 0;
            while (true)
            {
                int foundAt = haystack.IndexOf(needle, from, comparison);
                if (foundAt < 0) { break; }
                (int matchIdx, int matchLen) = origin is null
                    ? (foundAt, needle.Length)
                    : ToPageText(text, origin, foundAt, needle.Length);
                if (!opts.WholeWord || IsWholeWord(text, matchIdx, matchLen))
                {
                    SearchMatch match = BuildMatch(pageIdx, matchIdx, matchLen, runIndex, charOffsetInRun, runs);
                    yield return match;
                    cancellationToken.ThrowIfCancellationRequested();
                }
                from = foundAt + Math.Max(1, needle.Length);
            }
        }
    }

    private static (string Text, List<int> RunIndex, List<int> CharOffsetInRun, IReadOnlyList<TextRun> Runs)
        BuildPageText(PdfDocument document, int pageIdx)
    {
        IReadOnlyList<TextRun> runs = document.GetTextRuns(pageIdx);
        System.Text.StringBuilder sb = new();
        List<int> runIndex = new();
        List<int> charOffsetInRun = new();
        for (int r = 0; r < runs.Count; r++)
        {
            string txt = runs[r].Unicode;
            for (int c = 0; c < txt.Length; c++)
            {
                sb.Append(txt[c]);
                runIndex.Add(r);
                charOffsetInRun.Add(c);
            }
            // Insert a space between runs so word-boundary detection works
            // without merging adjacent runs into one logical word.
            if (r < runs.Count - 1)
            {
                sb.Append(' ');
                runIndex.Add(-1);
                charOffsetInRun.Add(0);
            }
        }
        return (sb.ToString(), runIndex, charOffsetInRun, runs);
    }

    private static SearchMatch BuildMatch(
        int pageIdx, int matchStart, int matchLen,
        List<int> runIndex, List<int> charOffsetInRun, IReadOnlyList<TextRun> runs)
    {
        // Determine which run(s) the match spans, and produce a bounding rect per run.
        List<Rect> boxes = new();
        int? currentRun = null;
        int? glyphFrom = null;
        int glyphTo = 0;

        for (int i = matchStart; i < matchStart + matchLen; i++)
        {
            int r = runIndex[i];
            if (r < 0) { continue; }   // run-boundary placeholder space
            int g = charOffsetInRun[i];
            if (currentRun is null || currentRun.Value != r)
            {
                if (currentRun is not null && glyphFrom is not null)
                {
                    boxes.Add(BoundingBoxOfGlyphRange(runs[currentRun.Value], glyphFrom.Value, glyphTo));
                }
                currentRun = r;
                glyphFrom = g;
                glyphTo = g;
            }
            else { glyphTo = g; }
        }
        if (currentRun is not null && glyphFrom is not null)
        {
            boxes.Add(BoundingBoxOfGlyphRange(runs[currentRun.Value], glyphFrom.Value, glyphTo));
        }

        return new SearchMatch(pageIdx, matchStart, matchLen, boxes);
    }

    private static Rect BoundingBoxOfGlyphRange(TextRun run, int fromGlyph, int toGlyph)
    {
        if (run.Glyphs.Count == 0) { return run.BoundingBox; }
        int lo = Math.Max(0, Math.Min(fromGlyph, toGlyph));
        int hi = Math.Min(run.Glyphs.Count - 1, Math.Max(fromGlyph, toGlyph));
        double minX = double.MaxValue, maxX = double.MinValue;
        for (int i = lo; i <= hi; i++)
        {
            double x = run.Glyphs[i].X;
            double xEnd = x + run.Glyphs[i].Advance;
            if (x < minX) { minX = x; }
            if (xEnd > maxX) { maxX = xEnd; }
        }
        return new Rect(minX, run.BoundingBox.Y, maxX - minX, run.BoundingBox.Height);
    }

    // The compatibility decomposition (NFKD) of each character, the minus
    // sign read as a hyphen-minus; origin receives, for each folded
    // character, the index of the page-text character it came from.
    private static string Fold(string text, List<int>? origin)
    {
        System.Text.StringBuilder sb = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < 0x80)
            {
                sb.Append(c);
                origin?.Add(i);
                continue;
            }

            int width = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;

            // A lone surrogate cannot be normalised: kept as it is.
            string folded = width == 1 && char.IsSurrogate(c)
                ? c.ToString()
                : text.Substring(i, width).Normalize(System.Text.NormalizationForm.FormKD).Replace('\u2212', '-');
            foreach (char f in folded)
            {
                sb.Append(f);
                origin?.Add(i);
            }

            i += width - 1;
        }

        return sb.ToString();
    }

    // A match in the folded text as a start and length in the page text.
    private static (int Start, int Length) ToPageText(string text, List<int> origin, int start, int length)
    {
        int first = origin[start];
        int last = origin[start + length - 1];
        int lastWidth = char.IsHighSurrogate(text[last]) && last + 1 < text.Length ? 2 : 1;
        return (first, last + lastWidth - first);
    }

    private static bool IsWholeWord(string text, int start, int length)
    {
        bool leftOk = start == 0 || !char.IsLetterOrDigit(text[start - 1]);
        int end = start + length;
        bool rightOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
        return leftOk && rightOk;
    }
}
