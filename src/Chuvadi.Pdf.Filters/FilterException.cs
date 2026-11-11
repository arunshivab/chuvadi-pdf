// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4 — Filters
// PHASE: Phase 1 — Chuvadi.Pdf.Filters
// Exception raised when a stream filter encounters malformed or invalid data.

using System;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Filters;

/// <summary>
/// Thrown when a PDF stream filter encounters data it cannot decode or encode.
/// </summary>
/// <remarks>
/// Covers malformed compressed data, invalid encoding sequences,
/// checksum failures, and truncated streams.
/// </remarks>
public sealed class FilterException : PdfException
{
    /// <summary>Initialises a new <see cref="FilterException"/> with no message.</summary>
    public FilterException()
        : base("A PDF filter error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="FilterException"/> with a message.</summary>
    public FilterException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="FilterException"/> with a message
    /// and an inner exception.
    /// </summary>
    public FilterException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="FilterException"/> with a message
    /// and the name of the filter that failed.
    /// </summary>
    /// <param name="filterName">The PDF filter name, e.g. "FlateDecode".</param>
    /// <param name="message">A description of the error.</param>
    public FilterException(string filterName, string message)
        : base($"{filterName}: {message}")
    {
        FilterName = filterName;
    }

    /// <summary>
    /// Initialises a new <see cref="FilterException"/> with a filter name,
    /// message, and inner exception.
    /// </summary>
    public FilterException(string filterName, string message, Exception innerException)
        : base($"{filterName}: {message}", innerException)
    {
        FilterName = filterName;
    }

    /// <summary>
    /// Gets the name of the filter that failed, if known.
    /// Returns null when the filter name was not provided.
    /// </summary>
    public string? FilterName { get; }

    // 3.33.0 (ticket 39, D-325): the bytes a filter decoded before it found
    // the damage, when it can tell (FlateDecode without a predictor); null
    // otherwise. Used to draw what a damaged page content stream holds.
    internal byte[]? PartialOutput { get; set; }
}
