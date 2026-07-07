// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: LA-23b Phase E — scripting.

using System;

namespace Chuvadi.Pdf.Xfa.Scripting;

/// <summary>
/// Thrown when a script cannot be parsed or evaluated. The script runner catches
/// this and fails soft, leaving form state untouched.
/// </summary>
public sealed class XfaScriptException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="XfaScriptException"/> class.</summary>
    public XfaScriptException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XfaScriptException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public XfaScriptException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XfaScriptException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public XfaScriptException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
