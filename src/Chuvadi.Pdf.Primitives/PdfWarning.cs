// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.27 — warnings for fallbacks (D-62, D-63)

using System;
using System.Globalization;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Reports a fallback taken while working on a document: something in the
/// file could not be used, and Chuvadi carried on without it instead of
/// failing. Receive warnings with <c>PdfOpenOptions.OnWarning</c>.
/// </summary>
public sealed class PdfWarning
{
    /// <summary>Initializes a new warning.</summary>
    /// <param name="kind">What happened.</param>
    /// <param name="pageNumber">The 1-based page being processed, or null when not known.</param>
    /// <param name="subject">What it happened to, for example <c>font /F1</c>.</param>
    /// <param name="reason">Why, in plain words.</param>
    public PdfWarning(PdfWarningKind kind, int? pageNumber, string subject, string reason)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(reason);
        Kind = kind;
        PageNumber = pageNumber;
        Subject = subject;
        Reason = reason;
    }

    /// <summary>Gets what happened.</summary>
    public PdfWarningKind Kind { get; }

    /// <summary>Gets the 1-based page being processed, or null when not known.</summary>
    public int? PageNumber { get; }

    /// <summary>Gets what it happened to, for example <c>font /F1</c> or <c>shading 12 0 R</c>.</summary>
    public string Subject { get; }

    /// <summary>Gets why it happened and what was done instead, in plain words.</summary>
    public string Reason { get; }

    /// <summary>Returns the warning as one line, for example for a log.</summary>
    /// <returns>Page, kind, subject and reason.</returns>
    public override string ToString()
    {
        string page = PageNumber is int p ? "page " + p.ToString(CultureInfo.InvariantCulture) : "no page";
        return $"{page}: {Kind}: {Subject}: {Reason}";
    }
}
