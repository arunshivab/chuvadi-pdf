// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: LA-23b Phase B — public API.

using System;

namespace Chuvadi.Pdf.Xfa.Render;

/// <summary>The exception thrown when an XFA template cannot be rendered.</summary>
public sealed class XfaRenderException : Exception
{
    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The error message.</param>
    public XfaRenderException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public XfaRenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance with no message.</summary>
    public XfaRenderException()
    {
    }
}
