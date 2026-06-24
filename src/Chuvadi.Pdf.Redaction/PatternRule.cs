// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Built on PDF 32000-1:2008 §9.4.5 text-showing operators.
// PHASE: Phase 1.1.2 — Chuvadi.Pdf.Redaction pattern-based extension
// A regex pattern paired with page filtering, used to drive matching-based
// redaction.

using System;
using System.Text.RegularExpressions;

namespace Chuvadi.Pdf.Redaction;

/// <summary>
/// A regex pattern that locates text to redact, with optional per-page filtering.
/// </summary>
/// <remarks>
/// Use this when the exact rectangles aren't known up front (e.g., "redact every
/// SSN", "redact every email address"). At redaction time each text-showing
/// operator's glyphs are decoded to Unicode (via the font's ToUnicode CMap) and
/// matched against the pattern; matched glyphs are physically removed from the
/// content stream, independent of layout geometry. A black (or caller-chosen)
/// box is painted over each removed run when the overlay is enabled.
/// </remarks>
public sealed class PatternRule
{
    /// <summary>Initialises a new <see cref="PatternRule"/>.</summary>
    /// <param name="pattern">
    /// The regex pattern. Must compile against the .NET regex flavour.
    /// </param>
    /// <param name="pageIndices">
    /// Optional list of zero-based page indices to restrict the rule to.
    /// When null, applies to all pages.
    /// </param>
    /// <param name="validator">
    /// Optional post-match predicate, run on each regex match's text. When it
    /// returns <see langword="false"/> the match is not redacted. Use this to
    /// reject false positives via a checksum (e.g. <see cref="PatternValidators"/>).
    /// </param>
    /// <param name="ignoreCase">
    /// When <see langword="true"/>, matching is case-insensitive. Defaults to
    /// <see langword="false"/> (case-sensitive). To control other regex options,
    /// pass a pre-compiled <see cref="Regex"/> via the other constructor.
    /// </param>
    public PatternRule(string pattern, int[]? pageIndices = null, Func<string, bool>? validator = null, bool ignoreCase = false)
    {
        if (pattern is null)
        {
            throw new ArgumentNullException(nameof(pattern));
        }

        RegexOptions options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        Regex = new Regex(pattern, options);
        PageIndices = pageIndices;
        Validator = validator;
    }

    /// <summary>Initialises a new <see cref="PatternRule"/> from a pre-compiled regex.</summary>
    public PatternRule(Regex regex, int[]? pageIndices = null, Func<string, bool>? validator = null)
    {
        Regex = regex ?? throw new ArgumentNullException(nameof(regex));
        PageIndices = pageIndices;
        Validator = validator;
    }

    /// <summary>Gets the compiled regex.</summary>
    public Regex Regex { get; }

    /// <summary>
    /// Gets the page indices this rule applies to, or null for all pages.
    /// </summary>
    public int[]? PageIndices { get; }

    /// <summary>
    /// Gets the optional post-match validator, or <see langword="null"/> to
    /// redact every regex match unconditionally.
    /// </summary>
    public Func<string, bool>? Validator { get; }

    /// <summary>Returns true if this rule applies to the given zero-based page index.</summary>
    public bool AppliesToPage(int pageIndex)
    {
        if (PageIndices is null)
        {
            return true;
        }

        for (int i = 0; i < PageIndices.Length; i++)
        {
            if (PageIndices[i] == pageIndex)
            {
                return true;
            }
        }

        return false;
    }
}
