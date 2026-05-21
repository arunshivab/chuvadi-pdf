// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8 — Graphics
// PHASE: v2.0.0 R1 D3c-1 — DisplayList types

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// An immutable, renderer-neutral representation of a PDF page's drawable content.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="PageDisplayList"/> is the output of the content-stream
/// builder (added in D3c-2) and the input of any renderer: the existing
/// pixel rasterizer, an SVG writer, a PDF/UA accessibility walker, etc.
/// </para>
/// <para>
/// Coordinate space: PDF user space (Y up, origin at the bottom-left of
/// the MediaBox, units of 1/72 inch). DPI scaling and Y-flipping happen
/// in the renderer, not the display list — which means the same list can
/// be re-rendered at any zoom level without rebuilding.
/// </para>
/// <para>
/// <see cref="PageWidth"/> and <see cref="PageHeight"/> are the MediaBox
/// dimensions in points. They are advisory information for the renderer
/// (e.g. for sizing the pixel buffer); the ops themselves are not clipped
/// to the page rectangle.
/// </para>
/// <para>
/// Page rotation (the PDF /Rotate entry) is NOT baked into the ops here.
/// A renderer that honours rotation applies an outer transform of the
/// appropriate multiple of 90°.
/// </para>
/// </remarks>
public sealed class PageDisplayList
{
    /// <summary>
    /// Initialises a <see cref="PageDisplayList"/> by defensively copying
    /// <paramref name="ops"/>.
    /// </summary>
    /// <param name="ops">The render operations, in paint order.</param>
    /// <param name="pageWidth">The MediaBox width in PDF user-space points. Must be non-negative.</param>
    /// <param name="pageHeight">The MediaBox height in PDF user-space points. Must be non-negative.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="ops"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="ops"/> contains a null entry.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pageWidth"/> or <paramref name="pageHeight"/> is negative.
    /// </exception>
    public PageDisplayList(IReadOnlyList<RenderOp> ops, double pageWidth, double pageHeight)
    {
        ArgumentNullException.ThrowIfNull(ops);

        if (pageWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageWidth), "Page width must be non-negative.");
        }

        if (pageHeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageHeight), "Page height must be non-negative.");
        }

        RenderOp[] copy = new RenderOp[ops.Count];

        for (int i = 0; i < ops.Count; i++)
        {
            copy[i] = ops[i] ?? throw new ArgumentException(
                $"Ops list contains a null entry at index {i}.",
                nameof(ops));
        }

        Ops = copy;
        PageWidth = pageWidth;
        PageHeight = pageHeight;
    }

    /// <summary>Gets the render operations in paint order.</summary>
    public IReadOnlyList<RenderOp> Ops { get; }

    /// <summary>
    /// Gets the number of operations in this display list. Convenience for
    /// <c>Ops.Count</c>.
    /// </summary>
    public int Count => Ops.Count;

    /// <summary>Gets the MediaBox width in PDF user-space points.</summary>
    public double PageWidth { get; }

    /// <summary>Gets the MediaBox height in PDF user-space points.</summary>
    public double PageHeight { get; }

    /// <summary>
    /// Returns an empty <see cref="PageDisplayList"/> with the given page dimensions.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pageWidth"/> or <paramref name="pageHeight"/> is negative.
    /// </exception>
    public static PageDisplayList Empty(double pageWidth, double pageHeight) =>
        new PageDisplayList(Array.Empty<RenderOp>(), pageWidth, pageHeight);
}
