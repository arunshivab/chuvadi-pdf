// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 — Document outline (bookmarks)
// PHASE: Phase 2 — Chuvadi.Pdf.Forms

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// A single bookmark in the document outline tree.
/// PDF 32000-1:2008 §12.3.3 — Document outline.
/// </summary>
public sealed class OutlineItem
{
    /// <summary>Initialises a new <see cref="OutlineItem"/>.</summary>
    public OutlineItem(
        string title,
        int destinationPageIndex,
        IReadOnlyList<OutlineItem> children)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        DestinationPageIndex = destinationPageIndex;
        Children = children ?? throw new ArgumentNullException(nameof(children));
    }

    /// <summary>Gets the bookmark's display title.</summary>
    public string Title { get; }

    /// <summary>
    /// Gets the zero-based page index the bookmark points to,
    /// or -1 when the destination cannot be resolved to a page.
    /// </summary>
    public int DestinationPageIndex { get; }

    /// <summary>
    /// Gets where the bookmark goes (3.32.0): the page, and how it is shown,
    /// or null when the destination cannot be resolved to a page.
    /// <see cref="PdfDestination.PageIndex"/> equals
    /// <see cref="DestinationPageIndex"/> whenever this is set.
    /// </summary>
    public PdfDestination? Destination { get; init; }

    /// <summary>Gets the nested child bookmarks, if any.</summary>
    public IReadOnlyList<OutlineItem> Children { get; }
}
