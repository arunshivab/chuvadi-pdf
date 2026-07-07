// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: LA-23b Phase B — public API.

namespace Chuvadi.Pdf.Xfa.Render;

/// <summary>How the renderer treats embedded XFA scripts.</summary>
public enum XfaScriptMode
{
    /// <summary>Do not execute scripts; render the merged/last-saved values.</summary>
    None,

    /// <summary>Execute only calculation scripts, not validations or events.</summary>
    CalculationsOnly,

    /// <summary>Execute calculation, validation, and initialization scripts.</summary>
    Full,
}

/// <summary>Options controlling XFA rendering.</summary>
public sealed class XfaRenderOptions
{
    /// <summary>Gets the default options: full scripting, best-effort rendering.</summary>
    public static XfaRenderOptions Default { get; } = new XfaRenderOptions();

    /// <summary>
    /// Gets or sets a value indicating whether to throw on unsupported template
    /// constructs rather than skipping them. Defaults to <see langword="false"/>.
    /// </summary>
    public bool FailOnUnsupported { get; init; }

    /// <summary>Gets or sets how embedded scripts are handled. Defaults to <see cref="XfaScriptMode.Full"/>.</summary>
    public XfaScriptMode ScriptMode { get; init; } = XfaScriptMode.Full;
}
