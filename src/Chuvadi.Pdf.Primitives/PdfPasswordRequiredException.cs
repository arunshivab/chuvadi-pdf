// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6.3.3 — Standard security handler passwords
// PHASE: 3.24 — embedding API (A1)

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Thrown when an encrypted PDF cannot be opened without a password: none was
/// given and the empty user password does not open it, or the password given
/// is neither the user nor the owner password.
/// </summary>
/// <remarks>
/// Derives from <see cref="PdfParseException"/>, which open methods threw for
/// this case before 3.24, so existing <c>catch (PdfParseException)</c> blocks
/// still catch it. Check <see cref="PasswordSupplied"/> to tell "ask the user
/// for a password" from "the password was wrong". To find out before opening,
/// use <c>PdfDocument.RequiresPassword</c>.
/// </remarks>
public sealed class PdfPasswordRequiredException : PdfParseException
{
    /// <summary>Initialises a new instance for a document opened without a password.</summary>
    public PdfPasswordRequiredException()
        : this(passwordSupplied: false)
    {
    }

    /// <summary>Initialises a new instance with the given message.</summary>
    /// <param name="message">A human-readable description of the failure.</param>
    public PdfPasswordRequiredException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance with the given message and inner exception.</summary>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="innerException">The exception that triggered this one.</param>
    public PdfPasswordRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initialises a new instance.</summary>
    /// <param name="passwordSupplied">
    /// True when a non-empty password was given and rejected; false when none
    /// was given (or the empty password was tried).
    /// </param>
    public PdfPasswordRequiredException(bool passwordSupplied)
        : base(passwordSupplied
            ? "The password is not correct for this encrypted PDF."
            : "This PDF is encrypted and needs a password to open.")
    {
        PasswordSupplied = passwordSupplied;
    }

    /// <summary>
    /// Gets a value indicating whether a non-empty password was given and
    /// rejected (true), or no password was given (false).
    /// </summary>
    public bool PasswordSupplied { get; }
}
