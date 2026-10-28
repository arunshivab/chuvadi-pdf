// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.6.5 — Link annotations
// PHASE: Phase 1.3 — Authoring module

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Internal record of a link rectangle on a page. The authoring pipeline
/// emits a <c>/Link</c> annotation per record at document save time: a URI
/// action when <see cref="LinkUri"/> is set, otherwise a go-to destination
/// on page <see cref="TargetPage"/> with <see cref="TargetTop"/> (top-left
/// coordinates of that page) at the top of the window.
/// </summary>
internal sealed record HyperlinkRect(
    double XFromLeft,
    double YFromBottom,
    double Width,
    double Height,
    string? LinkUri,
    int TargetPage = -1,
    double TargetTop = 0);

/// <summary>
/// Result of a <see cref="PageBuilder.DrawTextBlock"/> call.
/// </summary>
public sealed class TextBlockResult
{
    /// <summary>True when the supplied bounds were too small to fit all the text.</summary>
    public bool HasOverflow { get; init; }

    /// <summary>
    /// The portion of the text that didn't fit. Empty string if everything was drawn.
    /// </summary>
    public string RemainingText { get; init; } = string.Empty;

    /// <summary>The Y position (top-left coords) immediately below the last drawn line.</summary>
    public double NextYFromTop { get; init; }
}
