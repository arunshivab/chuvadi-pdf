// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.24 — embedding API (A4)

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Thrown when an operation would go past one of the <see cref="PdfLimits"/>
/// in effect (input size, page count, memory, decoded stream size, page
/// complexity or output pixels). The work is abandoned; nothing partial is
/// returned.
/// </summary>
public sealed class PdfLimitExceededException : PdfException
{
    /// <summary>Initialises a new instance with no message.</summary>
    public PdfLimitExceededException()
    {
        LimitName = string.Empty;
    }

    /// <summary>Initialises a new instance with the given message.</summary>
    /// <param name="message">A human-readable description of the failure.</param>
    public PdfLimitExceededException(string message)
        : base(message)
    {
        LimitName = string.Empty;
    }

    /// <summary>Initialises a new instance with the given message and inner exception.</summary>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="innerException">The exception that triggered this one.</param>
    public PdfLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
        LimitName = string.Empty;
    }

    /// <summary>Initialises a new instance for a named limit.</summary>
    /// <param name="limitName">The <see cref="PdfLimits"/> property that was exceeded.</param>
    /// <param name="limit">The configured limit.</param>
    /// <param name="actual">The value that went past it (at least this much).</param>
    public PdfLimitExceededException(string limitName, long limit, long actual)
        : base($"{limitName} exceeded: {actual} > {limit}.")
    {
        LimitName = limitName ?? string.Empty;
        Limit = limit;
        Actual = actual;
    }

    /// <summary>Gets the name of the <see cref="PdfLimits"/> property that was exceeded.</summary>
    public string LimitName { get; }

    /// <summary>Gets the configured limit.</summary>
    public long Limit { get; }

    /// <summary>Gets the value that went past the limit (at least this much).</summary>
    public long Actual { get; }
}
