// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  XFA 3.3 — <event activity="..."><script contentType="...">.
// PHASE: LA-23b Phase E — scripting.

namespace Chuvadi.Pdf.Xfa.Model;

/// <summary>
/// A script attached to a node through an XFA event. Captures the scripting
/// language, the triggering event, and the raw source text.
/// </summary>
public sealed class XfaScript
{
    /// <summary>Initializes a new instance of the <see cref="XfaScript"/> class.</summary>
    /// <param name="language">The scripting language.</param>
    /// <param name="event">The triggering event.</param>
    /// <param name="source">The raw script source text.</param>
    public XfaScript(XfaScriptLanguage language, XfaScriptEvent @event, string source)
    {
        Language = language;
        Event = @event;
        Source = source;
    }

    /// <summary>Gets the scripting language.</summary>
    public XfaScriptLanguage Language { get; }

    /// <summary>Gets the event that triggers this script.</summary>
    public XfaScriptEvent Event { get; }

    /// <summary>Gets the raw script source text.</summary>
    public string Source { get; }
}
