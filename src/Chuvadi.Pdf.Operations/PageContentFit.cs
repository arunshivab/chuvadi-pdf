// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Document operations — header/footer fit strategy.

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// How header/footer bands interact with existing page content.
/// </summary>
public enum PageContentFit
{
    /// <summary>
    /// Draw the header/footer in the page margins without moving content. Fast,
    /// but may overlap content if the margins are not empty.
    /// </summary>
    Overlay,

    /// <summary>
    /// Always reserve the header and footer band heights, scaling existing
    /// content down uniformly and shifting it to fit the remaining height. Never
    /// overlaps; the trade-off is a slight, uniform "zoom out" of content.
    /// </summary>
    ReserveAndScale,

    /// <summary>
    /// Reserve and scale only when existing content actually reaches into a
    /// band; otherwise behave like <see cref="Overlay"/>. Closest to a word
    /// processor, but band intrusion is detected heuristically.
    /// </summary>
    ScaleIfIntruding,
}
