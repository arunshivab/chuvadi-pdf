// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Chuvadi v2.0.0 reader library plan §4.3
// PHASE: Phase 2.0 — exception hierarchy

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Thrown when the bytes of a PDF cannot be parsed because they violate the
/// PDF syntax: malformed tokens, structural errors in dictionaries or arrays,
/// invalid integer or real literals, missing required keywords.
/// </summary>
/// <remarks>
/// Distinguished from <see cref="PdfCorruptionException"/> — that signals a
/// document that <em>parses</em> but is semantically inconsistent (e.g. a
/// cyclic page tree, a missing required catalog entry). A parse error means
/// the bytes themselves are wrong; a corruption error means the bytes are
/// fine but the document they describe is broken.
///
/// In v2.0.0 this type replaces the v1.x <c>PdfReaderException</c>,
/// <c>PdfTokenizerException</c>, and the structural-shape subset of
/// <c>PdfObjectException</c>.
/// </remarks>
public class PdfParseException : PdfException
{
    /// <summary>Initialises a new instance with no message.</summary>
    public PdfParseException()
    {
    }

    /// <summary>Initialises a new instance with the given message.</summary>
    /// <param name="message">A human-readable description of the failure.</param>
    public PdfParseException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance with the given message and an inner exception
    /// that caused the failure (e.g. an <see cref="OverflowException"/> from a
    /// malformed integer literal).
    /// </summary>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="innerException">The exception that triggered this one.</param>
    public PdfParseException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance with the given message and a byte offset into
    /// the PDF input where the failure was detected.
    /// </summary>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="offset">Zero-based byte offset into the input stream.</param>
    public PdfParseException(string message, long offset) : base(message)
    {
        Offset = offset;
    }

    /// <summary>
    /// Initialises a new instance with the given message, byte offset, and an
    /// inner exception that caused the failure.
    /// </summary>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="offset">Zero-based byte offset into the input stream.</param>
    /// <param name="innerException">The exception that triggered this one.</param>
    public PdfParseException(string message, long offset, Exception innerException) : base(message, innerException)
    {
        Offset = offset;
    }

    /// <summary>
    /// Zero-based byte offset into the source stream where the failure was
    /// detected, or <c>null</c> if the offset is not known.
    /// </summary>
    public long? Offset { get; }
}
