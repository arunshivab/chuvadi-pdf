// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Document operations — header/footer options.

using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// The left, centre, and right text segments of a header or footer band. Each
/// segment is an independent template (may contain tokens); a null segment is
/// omitted.
/// </summary>
public sealed class BandText
{
    /// <summary>Initialises a band with optional left/centre/right segments.</summary>
    /// <param name="left">Left-aligned segment template, or null.</param>
    /// <param name="center">Centre-aligned segment template, or null.</param>
    /// <param name="right">Right-aligned segment template, or null.</param>
    public BandText(string? left = null, string? center = null, string? right = null)
    {
        Left = left;
        Center = center;
        Right = right;
    }

    /// <summary>Gets the left-aligned segment template, or null.</summary>
    public string? Left { get; }

    /// <summary>Gets the centre-aligned segment template, or null.</summary>
    public string? Center { get; }

    /// <summary>Gets the right-aligned segment template, or null.</summary>
    public string? Right { get; }
}

/// <summary>
/// Options controlling header/footer content, geometry, and the content-fit
/// strategy. Header and footer are independent; either may be null.
/// </summary>
public sealed class HeaderFooterOptions
{
    /// <summary>Gets or initialises the header band, or null for no header.</summary>
    public BandText? Header { get; init; }

    /// <summary>Gets or initialises the footer band, or null for no footer.</summary>
    public BandText? Footer { get; init; }

    /// <summary>
    /// Gets or initialises the reserved header band height in points
    /// (used when <see cref="Fit"/> scales content). Default: 36.
    /// </summary>
    public double HeaderHeight { get; init; } = 36.0;

    /// <summary>
    /// Gets or initialises the reserved footer band height in points
    /// (used when <see cref="Fit"/> scales content). Default: 36.
    /// </summary>
    public double FooterHeight { get; init; } = 36.0;

    /// <summary>
    /// Gets or initialises the header baseline offset measured downward from the
    /// top of the reserved band, in points. Default: -24 (24 pt below the top).
    /// </summary>
    public double HeaderBaselineOffset { get; init; } = -24.0;

    /// <summary>
    /// Gets or initialises the footer baseline offset measured upward from the
    /// bottom of the page, in points. Default: 18.
    /// </summary>
    public double FooterBaselineOffset { get; init; } = 18.0;

    /// <summary>
    /// Gets or initialises the horizontal margin for left/right segments, in
    /// points. Default: 36.
    /// </summary>
    public double MarginX { get; init; } = 36.0;

    /// <summary>Gets or initialises the font size in points. Default: 9.</summary>
    public double FontSize { get; init; } = 9.0;

    /// <summary>Gets or initialises the text colour. Default: black.</summary>
    public ColorF Color { get; init; } = ColorF.Black;

    /// <summary>
    /// Gets or initialises a background fill drawn behind page content when a
    /// reflow strategy is used, or null for none.
    /// </summary>
    public ColorF? Background { get; init; }

    /// <summary>
    /// Gets or initialises how header/footer bands interact with existing
    /// content. Default: <see cref="PageContentFit.ReserveAndScale"/>.
    /// </summary>
    public PageContentFit Fit { get; init; } = PageContentFit.ReserveAndScale;

    /// <summary>
    /// Gets or initialises which pages receive the header/footer. Null means all
    /// pages; otherwise a zero-based page index set.
    /// </summary>
    public IReadOnlyList<int>? PageIndices { get; init; }

    /// <summary>
    /// Gets or initialises the source file path for the <c>{filename}</c> and
    /// <c>{filepath}</c> tokens, or null.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Gets or initialises the caller-supplied timestamp for the date/time
    /// tokens, or null.
    /// </summary>
    public System.DateTimeOffset? Timestamp { get; init; }
}
