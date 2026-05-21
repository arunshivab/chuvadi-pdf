// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R3 — Text run extraction + Search

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.Content;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Text;

/// <summary>
/// Convenience extension methods that surface text-extraction and search
/// capabilities directly on <see cref="PdfDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// Imported via <c>using Chuvadi.Pdf.Text;</c>:
/// </para>
/// <code>
///   using Chuvadi.Pdf.Text;
///   IReadOnlyList&lt;TextRun&gt; runs = document.GetTextRuns(pageIndex: 0);
///   await foreach (SearchMatch m in document.SearchAsync("invoice")) { … }
/// </code>
/// </remarks>
public static class PdfDocumentTextExtensions
{
    /// <summary>
    /// Returns the <see cref="TextRun"/>s on the page at
    /// <paramref name="pageIndex"/>, in content-stream order.
    /// </summary>
    /// <param name="document">The document to read from.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="document"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pageIndex"/> is outside
    /// <c>[0, document.PageCount)</c>.
    /// </exception>
    public static IReadOnlyList<TextRun> GetTextRuns(this PdfDocument document, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (pageIndex < 0 || pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                pageIndex,
                $"Page index must be in [0, {document.PageCount}).");
        }

        PdfPage page = document.Pages[pageIndex];
        TextExtractor extractor = new TextExtractor(document.Objects);
        List<TextFragment> fragments = extractor.ExtractFragments(page);
        return TextRunBuilder.BuildFromFragments(fragments);
    }

    /// <summary>
    /// Searches the document for <paramref name="query"/> and yields one
    /// <see cref="SearchMatch"/> per occurrence, page by page in order.
    /// </summary>
    /// <param name="document">The document to search.</param>
    /// <param name="query">The substring to find. Must be non-empty.</param>
    /// <param name="options">
    /// Search options. When null, <see cref="SearchOptions.Default"/> is used.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the search.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="document"/> or <paramref name="query"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="query"/> is empty.
    /// </exception>
    public static async IAsyncEnumerable<SearchMatch> SearchAsync(
        this PdfDocument document,
        string query,
        SearchOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(query);

        if (query.Length == 0)
        {
            throw new ArgumentException("Query must be non-empty.", nameof(query));
        }

        SearchOptions opts = options ?? SearchOptions.Default;
        int start = opts.PageRangeStart;
        int end = Math.Min(opts.PageRangeEnd, document.PageCount - 1);

        for (int pageIndex = start; pageIndex <= end; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<TextRun> runs = document.GetTextRuns(pageIndex);

            if (runs.Count == 0)
            {
                continue;
            }

            // Build the page text and the inverse map from character index
            // to the originating run.
            PageText page = BuildPageText(runs);

            foreach (SearchMatch match in FindMatches(page, query, opts, pageIndex + 1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return match;
            }

            // Asynchronous yield point so very large documents stay responsive.
            await Task.Yield();
        }
    }

    // ── Internal helpers ──────────────────────────────────────────────────

    private sealed class PageText
    {
        internal string Text { get; init; } = string.Empty;
        internal int[] RunIndexByChar { get; init; } = Array.Empty<int>();
        internal IReadOnlyList<TextRun> Runs { get; init; } = Array.Empty<TextRun>();
    }

    private static PageText BuildPageText(IReadOnlyList<TextRun> runs)
    {
        // Total length: sum of run lengths + (runs.Count - 1) spaces between them.
        int totalChars = 0;
        for (int i = 0; i < runs.Count; i++)
        {
            totalChars += runs[i].Unicode.Length;
        }
        if (runs.Count > 1)
        {
            totalChars += runs.Count - 1;
        }

        char[] buffer = new char[totalChars];
        int[] map = new int[totalChars];
        int cursor = 0;

        for (int i = 0; i < runs.Count; i++)
        {
            string s = runs[i].Unicode;

            for (int j = 0; j < s.Length; j++)
            {
                buffer[cursor] = s[j];
                map[cursor] = i;
                cursor++;
            }

            if (i < runs.Count - 1)
            {
                buffer[cursor] = ' ';
                // Inter-run space belongs to the previous run for box-overlap
                // purposes; the next run starts at the following character.
                map[cursor] = i;
                cursor++;
            }
        }

        return new PageText
        {
            Text = new string(buffer, 0, cursor),
            RunIndexByChar = map,
            Runs = runs,
        };
    }

    private static IEnumerable<SearchMatch> FindMatches(
        PageText page, string query, SearchOptions options, int pageNumber)
    {
        StringComparison cmp = options.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        int searchFrom = 0;

        while (searchFrom <= page.Text.Length - query.Length)
        {
            int idx = page.Text.IndexOf(query, searchFrom, cmp);

            if (idx < 0)
            {
                yield break;
            }

            if (!options.WholeWord || IsWholeWord(page.Text, idx, query.Length))
            {
                yield return BuildMatch(page, pageNumber, idx, query.Length);
            }

            searchFrom = idx + 1;
        }
    }

    private static bool IsWholeWord(string text, int start, int length)
    {
        bool leftOk = start == 0 || !IsWordChar(text[start - 1]);
        int rightIdx = start + length;
        bool rightOk = rightIdx >= text.Length || !IsWordChar(text[rightIdx]);
        return leftOk && rightOk;
    }

    private static bool IsWordChar(char c)
    {
        UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);

        switch (cat)
        {
            case UnicodeCategory.UppercaseLetter:
            case UnicodeCategory.LowercaseLetter:
            case UnicodeCategory.TitlecaseLetter:
            case UnicodeCategory.ModifierLetter:
            case UnicodeCategory.OtherLetter:
            case UnicodeCategory.DecimalDigitNumber:
            case UnicodeCategory.LetterNumber:
            case UnicodeCategory.OtherNumber:
            case UnicodeCategory.ConnectorPunctuation:
                return true;
            default:
                return false;
        }
    }

    private static SearchMatch BuildMatch(
        PageText page, int pageNumber, int charOffset, int length)
    {
        // Collect distinct run indices the match overlaps.
        List<RectangleF> boxes = new List<RectangleF>();
        int prevRunIndex = -1;

        for (int i = 0; i < length; i++)
        {
            int absIdx = charOffset + i;

            if (absIdx >= page.RunIndexByChar.Length)
            {
                break;
            }

            int runIdx = page.RunIndexByChar[absIdx];

            if (runIdx != prevRunIndex)
            {
                boxes.Add(page.Runs[runIdx].BoundingBox);
                prevRunIndex = runIdx;
            }
        }

        return new SearchMatch(pageNumber, charOffset, length, boxes);
    }
}
