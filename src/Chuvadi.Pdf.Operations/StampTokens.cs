// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Document operations — stamp token substitution.

using System;
using System.Globalization;
using System.Text;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Substitutes tokens in a stamp template into final text for one page.
/// Supported tokens:
/// <list type="bullet">
/// <item><c>{page}</c> — 1-based page number (arabic).</item>
/// <item><c>{page:roman}</c> / <c>{page:ROMAN}</c> — lower/upper roman numerals.</item>
/// <item><c>{page:alpha}</c> / <c>{page:ALPHA}</c> — lower/upper bijective base-26 (a, b, … z, aa, …).</item>
/// <item><c>{total}</c> — total page count.</item>
/// <item><c>{filename}</c> — source file name without directory path.</item>
/// <item><c>{filepath}</c> — full source file path as supplied.</item>
/// <item><c>{number}</c> — styled running number (Bates) supplied via the
/// <see cref="TextStamper"/> numbering overload; empty when none is supplied.</item>
/// <item><c>{date:FORMAT}</c>, <c>{time:FORMAT}</c>, <c>{datetime:FORMAT}</c> —
/// the caller-supplied timestamp formatted with a .NET format string.</item>
/// </list>
/// A literal brace is written as <c>{{</c> or <c>}}</c>. Unknown tokens are
/// left verbatim. Date/time tokens render empty when no timestamp is supplied.
/// </summary>
public sealed class StampContext
{
    /// <summary>Initialises a stamp context for one page.</summary>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="totalPages">Total page count.</param>
    /// <param name="filePath">Source file path, or null if unknown.</param>
    /// <param name="timestamp">Caller-supplied timestamp, or null.</param>
    public StampContext(
        int pageNumber,
        int totalPages,
        string? filePath,
        DateTimeOffset? timestamp)
        : this(pageNumber, totalPages, filePath, timestamp, null)
    {
    }

    /// <summary>Initialises a stamp context for one page, including a styled number.</summary>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="totalPages">Total page count.</param>
    /// <param name="filePath">Source file path, or null if unknown.</param>
    /// <param name="timestamp">Caller-supplied timestamp, or null.</param>
    /// <param name="number">Pre-formatted numbering label for the <c>{number}</c> token, or null.</param>
    public StampContext(
        int pageNumber,
        int totalPages,
        string? filePath,
        DateTimeOffset? timestamp,
        string? number)
    {
        PageNumber = pageNumber;
        TotalPages = totalPages;
        FilePath = filePath;
        Timestamp = timestamp;
        Number = number;
    }

    /// <summary>Gets the 1-based page number.</summary>
    public int PageNumber { get; }

    /// <summary>Gets the total page count.</summary>
    public int TotalPages { get; }

    /// <summary>Gets the source file path, or null.</summary>
    public string? FilePath { get; }

    /// <summary>Gets the caller-supplied timestamp, or null.</summary>
    public DateTimeOffset? Timestamp { get; }

    /// <summary>Gets the pre-formatted numbering label for the <c>{number}</c> token, or null.</summary>
    public string? Number { get; }
}

/// <summary>
/// Resolves stamp templates against a <see cref="StampContext"/>.
/// </summary>
public static class StampTokens
{
    /// <summary>
    /// Expands all tokens in <paramref name="template"/> for the given context.
    /// </summary>
    /// <param name="template">The template text containing tokens.</param>
    /// <param name="context">The per-page values.</param>
    /// <returns>The fully substituted text.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="template"/> or <paramref name="context"/> is null.
    /// </exception>
    public static string Resolve(string template, StampContext context)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        StringBuilder sb = new StringBuilder(template.Length + 16);
        int i = 0;

        while (i < template.Length)
        {
            char c = template[i];

            if (c == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    sb.Append('{');
                    i += 2;
                    continue;
                }

                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    sb.Append(template, i, template.Length - i);
                    break;
                }

                string token = template.Substring(i + 1, close - i - 1);
                sb.Append(ResolveToken(token, context));
                i = close + 1;
                continue;
            }

            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                sb.Append('}');
                i += 2;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static string ResolveToken(string token, StampContext context)
    {
        int colon = token.IndexOf(':');
        string name = colon < 0 ? token : token.Substring(0, colon);
        string arg = colon < 0 ? string.Empty : token.Substring(colon + 1);

        switch (name)
        {
            case "page":
                return FormatPage(context.PageNumber, arg);

            case "total":
                return context.TotalPages.ToString(CultureInfo.InvariantCulture);

            case "filename":
                return context.FilePath is null
                    ? string.Empty
                    : System.IO.Path.GetFileName(context.FilePath);

            case "filepath":
                return context.FilePath ?? string.Empty;

            case "number":
                return context.Number ?? string.Empty;

            case "date":
            case "time":
            case "datetime":
                return FormatDate(context.Timestamp, arg, name);

            default:
                // Unknown token: leave verbatim (including braces).
                return "{" + token + "}";
        }
    }

    private static string FormatPage(int pageNumber, string style)
    {
        switch (style)
        {
            case "":
            case "arabic":
                return pageNumber.ToString(CultureInfo.InvariantCulture);

            case "roman":
                return ToRoman(pageNumber).ToLowerInvariant();

            case "ROMAN":
                return ToRoman(pageNumber);

            case "alpha":
                return ToAlpha(pageNumber).ToLowerInvariant();

            case "ALPHA":
                return ToAlpha(pageNumber);

            default:
                return pageNumber.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string FormatDate(DateTimeOffset? timestamp, string format, string kind)
    {
        if (timestamp is not DateTimeOffset ts)
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(format))
        {
            return ts.ToString(format, CultureInfo.InvariantCulture);
        }

        // Sensible defaults when no format is supplied.
        return kind switch
        {
            "date" => ts.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "time" => ts.ToString("HH:mm", CultureInfo.InvariantCulture),
            _ => ts.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        };
    }

    // Roman numerals. Values <= 0 fall back to the arabic form so page
    // numbering never throws on an unusual index.
    private static string ToRoman(int value)
    {
        if (value <= 0)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        StringBuilder sb = new StringBuilder();
        int remaining = value;
        for (int i = 0; i < values.Length; i++)
        {
            while (remaining >= values[i])
            {
                sb.Append(symbols[i]);
                remaining -= values[i];
            }
        }

        return sb.ToString();
    }

    // Bijective base-26: 1->A, 26->Z, 27->AA, 28->AB, ...
    private static string ToAlpha(int value)
    {
        if (value <= 0)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        StringBuilder sb = new StringBuilder();
        int n = value;
        while (n > 0)
        {
            n--;
            sb.Insert(0, (char)('A' + (n % 26)));
            n /= 26;
        }

        return sb.ToString();
    }
}
