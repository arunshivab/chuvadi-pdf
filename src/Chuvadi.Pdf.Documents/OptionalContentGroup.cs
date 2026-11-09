// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.11 — Optional content (layers)
// PHASE: Phase 1.1.7 — Chuvadi.Pdf.Documents optional content

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// An Optional Content Group (OCG) — a named, toggleable layer in a PDF.
/// </summary>
/// <remarks>
/// PDF 32000 §8.11 defines optional content as graphics that can be selectively
/// shown or hidden by the viewer. Common uses: anatomical overlays in medical
/// imaging, engineering drawing layers, multi-language annotation sets.
/// </remarks>
public sealed class OptionalContentGroup
{
    /// <summary>Initialises a new <see cref="OptionalContentGroup"/>.</summary>
    public OptionalContentGroup(
        string name,
        bool isVisibleByDefault,
        IReadOnlyList<string> intents)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IsVisibleByDefault = isVisibleByDefault;
        Intents = intents ?? throw new ArgumentNullException(nameof(intents));
    }

    /// <summary>Gets the human-readable layer name (from /Name).</summary>
    public string Name { get; }

    /// <summary>
    /// Gets whether the layer is visible in the default configuration.
    /// Computed from the document's /D/ON and /D/OFF arrays.
    /// </summary>
    public bool IsVisibleByDefault { get; }

    /// <summary>
    /// Gets the layer's intents (e.g., "View", "Design"). Empty when unspecified.
    /// </summary>
    public IReadOnlyList<string> Intents { get; }

    /// <summary>
    /// Gets the layer's id (3.32.0): its object number, unique within the
    /// document, so layers with the same name can be told apart; -1 for a
    /// group written directly in <c>/OCGs</c> instead of by reference. Use it
    /// with <see cref="OptionalContentVisibility.WithOverridesById"/>. It
    /// identifies the layer in this opened document; another tool rewriting
    /// the file may renumber it.
    /// </summary>
    public int Id { get; init; } = -1;

    /// <summary>
    /// Gets whether the default configuration locks the layer (3.32.0): it is
    /// in <c>/OCProperties /D /Locked</c>, so a viewer does not let its user
    /// change the layer's state (PDF 32000-1:2008 §8.11.4.3). Chuvadi's
    /// overrides still apply to it.
    /// </summary>
    public bool IsLocked { get; init; }
}
