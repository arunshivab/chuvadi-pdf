// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R3 — Search

using System;

namespace Chuvadi.Pdf.Text;

/// <summary>
/// Options controlling a <see cref="PdfDocumentTextExtensions.SearchAsync"/>
/// invocation.
/// </summary>
public sealed class SearchOptions
{
    /// <summary>The default options: case-insensitive, full document, no whole-word.</summary>
    public static SearchOptions Default { get; } = new SearchOptions();

    /// <summary>Initialises a <see cref="SearchOptions"/> with default values.</summary>
    public SearchOptions()
    {
        CaseSensitive = false;
        WholeWord = false;
        PageRangeStart = 0;
        PageRangeEnd = int.MaxValue;
    }

    /// <summary>
    /// Gets or initialises whether the search is case-sensitive.
    /// Default false.
    /// </summary>
    public bool CaseSensitive { get; init; }

    /// <summary>
    /// Gets or initialises whether to match whole words only.
    /// Default false.
    /// </summary>
    /// <remarks>
    /// A whole-word match requires the character before and after the
    /// match position to be a non-letter and non-digit (or to be at the
    /// start or end of the page text).
    /// </remarks>
    public bool WholeWord { get; init; }

    /// <summary>
    /// Gets or initialises the inclusive zero-based start of the page
    /// range to search. Default 0.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when set to a negative value.
    /// </exception>
    public int PageRangeStart
    {
        get => _pageRangeStart;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(PageRangeStart),
                    value,
                    "PageRangeStart must be non-negative.");
            }

            _pageRangeStart = value;
        }
    }

    private readonly int _pageRangeStart;

    /// <summary>
    /// Gets or initialises the inclusive zero-based end of the page
    /// range to search. Default <see cref="int.MaxValue"/> (no limit).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when set to a negative value.
    /// </exception>
    public int PageRangeEnd
    {
        get => _pageRangeEnd;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(PageRangeEnd),
                    value,
                    "PageRangeEnd must be non-negative.");
            }

            _pageRangeEnd = value;
        }
    }

    private readonly int _pageRangeEnd;
}
