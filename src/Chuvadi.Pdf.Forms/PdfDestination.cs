// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.2.2, Table 151 — Explicit destinations
// PHASE: 3.32.0 (D-208 item 4, D-230 Q5) — destination kind and coordinates

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// How a destination positions the page in the window (PDF 32000-1:2008
/// Table 151).
/// </summary>
public enum PdfDestinationKind
{
    /// <summary>
    /// <c>/XYZ</c>: <see cref="PdfDestination.Left"/> and
    /// <see cref="PdfDestination.Top"/> at the window's upper-left corner,
    /// magnified by <see cref="PdfDestination.Zoom"/>. Also used for a
    /// destination whose kind is missing or not recognised, with every value
    /// null ("keep the current view").
    /// </summary>
    XYZ = 0,

    /// <summary><c>/Fit</c>: the whole page fits the window.</summary>
    Fit,

    /// <summary><c>/FitH</c>: the page width fits, with <see cref="PdfDestination.Top"/> at the top.</summary>
    FitH,

    /// <summary><c>/FitV</c>: the page height fits, with <see cref="PdfDestination.Left"/> at the left.</summary>
    FitV,

    /// <summary><c>/FitR</c>: the rectangle Left, Bottom, Right, Top fits the window.</summary>
    FitR,

    /// <summary><c>/FitB</c>: the page's bounding box fits the window.</summary>
    FitB,

    /// <summary><c>/FitBH</c>: the bounding box width fits, with <see cref="PdfDestination.Top"/> at the top.</summary>
    FitBH,

    /// <summary><c>/FitBV</c>: the bounding box height fits, with <see cref="PdfDestination.Left"/> at the left.</summary>
    FitBV,
}

/// <summary>
/// A resolved destination: the target page, and how the page is shown
/// (3.32.0).
/// </summary>
/// <remarks>
/// Coordinates are in the target page's default user space: points, origin
/// at the bottom left, the space of its MediaBox, before <c>/Rotate</c>. A
/// null coordinate or zoom means "leave unchanged" (PDF 32000-1:2008
/// §12.3.2.2), or that <see cref="Kind"/> has no such value.
/// </remarks>
public sealed class PdfDestination
{
    /// <summary>Initialises a destination.</summary>
    /// <param name="pageIndex">The zero-based target page index.</param>
    /// <param name="kind">How the page is positioned.</param>
    public PdfDestination(int pageIndex, PdfDestinationKind kind)
    {
        PageIndex = pageIndex;
        Kind = kind;
    }

    /// <summary>
    /// Gets the zero-based target page index. For a remote (<c>/GoToR</c>)
    /// destination it is the page number as written in the other file.
    /// </summary>
    public int PageIndex { get; }

    /// <summary>Gets how the page is positioned.</summary>
    public PdfDestinationKind Kind { get; }

    /// <summary>Gets the left edge, or null.</summary>
    public double? Left { get; init; }

    /// <summary>Gets the top edge, or null.</summary>
    public double? Top { get; init; }

    /// <summary>Gets the right edge (<c>/FitR</c> only), or null.</summary>
    public double? Right { get; init; }

    /// <summary>Gets the bottom edge (<c>/FitR</c> only), or null.</summary>
    public double? Bottom { get; init; }

    /// <summary>
    /// Gets the magnification (<c>/XYZ</c> only), or null. Zero also means
    /// "unchanged" in the specification and is reported as null.
    /// </summary>
    public double? Zoom { get; init; }
}
