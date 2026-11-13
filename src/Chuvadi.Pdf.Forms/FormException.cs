// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Forms
//        3.34.0 (D-358, D-362) — reason, field name, valid states, value and
//        maximum length as properties, so that apps can show their own
//        message in the reader's language.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms;

/// <summary>Thrown when an AcroForm or outline operation fails.</summary>
/// <remarks>
/// Since 3.34.0 an exception thrown by <see cref="FormFiller"/> for a value
/// it refuses carries the details as properties beside the English message:
/// <see cref="Reason"/>, <see cref="FieldName"/>, <see cref="Value"/>,
/// <see cref="ValidStates"/> and <see cref="MaxLength"/>.
/// </remarks>
public sealed class FormException : PdfException
{
    /// <summary>Initialises a new <see cref="FormException"/> with no message.</summary>
    public FormException()
        : base("A form operation error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="FormException"/> with a message.</summary>
    public FormException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="FormException"/> with a message and inner exception.
    /// </summary>
    public FormException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets why <see cref="FormFiller"/> refused a value, or null for any other
    /// form error (3.34.0).
    /// </summary>
    public FormErrorReason? Reason { get; internal init; }

    /// <summary>
    /// Gets the fully qualified name of the field the error is about, or null
    /// (3.34.0).
    /// </summary>
    public string? FieldName { get; internal init; }

    /// <summary>Gets the value that was refused, or null (3.34.0).</summary>
    public string? Value { get; internal init; }

    /// <summary>
    /// Gets the values the field accepts, for an app to list in its own
    /// message (3.34.0): for <see cref="FormErrorReason.UnknownState"/> each
    /// button's on-state, its export value when that differs, and <c>Off</c>;
    /// for <see cref="FormErrorReason.NotInOptions"/> the export values of the
    /// field's options, in order. Empty for the other reasons.
    /// </summary>
    public IReadOnlyList<string> ValidStates { get; internal init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the field's maximum length in Unicode code points, for
    /// <see cref="FormErrorReason.TooLong"/>; otherwise null (3.34.0).
    /// </summary>
    public int? MaxLength { get; internal init; }
}
