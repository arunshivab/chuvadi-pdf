// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 — Document outline (bookmarks)
// PHASE: Document operations — outline authoring input model. 3.33.0
//        (PDF-17, D-322): an exact destination, or the source bookmark.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Forms;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// A bookmark to write into a document outline: a title, the zero-based page it
/// targets, and optional nested children. Used as input to
/// <see cref="OutlineWriter"/>. The companion read-side type is
/// <c>Chuvadi.Pdf.Forms.OutlineItem</c>.
/// PDF 32000-1:2008 §12.3.3 — Document outline.
/// </summary>
public sealed class OutlineEntry
{
    /// <summary>
    /// Initialises a bookmark with no children.
    /// </summary>
    /// <param name="title">The bookmark's display title.</param>
    /// <param name="pageIndex">The zero-based destination page index.</param>
    public OutlineEntry(string title, int pageIndex)
        : this(title, pageIndex, Array.Empty<OutlineEntry>())
    {
    }

    /// <summary>
    /// Initialises a bookmark with nested children.
    /// </summary>
    /// <param name="title">The bookmark's display title.</param>
    /// <param name="pageIndex">The zero-based destination page index.</param>
    /// <param name="children">The nested child bookmarks.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="title"/> or <paramref name="children"/> is null.
    /// </exception>
    public OutlineEntry(string title, int pageIndex, IReadOnlyList<OutlineEntry> children)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        PageIndex = pageIndex;
        Children = children ?? throw new ArgumentNullException(nameof(children));
    }

    /// <summary>
    /// Initialises a bookmark that goes to an exact destination (3.33.0): the
    /// page, and how it is shown (<c>/XYZ</c> left, top and zoom, <c>/FitH</c>
    /// top and so on), written back as given. For a new or moved bookmark.
    /// </summary>
    /// <param name="title">The bookmark's display title.</param>
    /// <param name="destination">Where the bookmark goes; <see cref="PageIndex"/> is its page.</param>
    /// <param name="children">The nested child bookmarks.</param>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    public OutlineEntry(string title, PdfDestination destination, IReadOnlyList<OutlineEntry> children)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Destination = destination;
        PageIndex = destination.PageIndex;
        Children = children ?? throw new ArgumentNullException(nameof(children));
    }

    /// <summary>
    /// Initialises a bookmark kept from the document's outline (3.33.0), with
    /// a title and children that may be new. Its target (<c>/Dest</c>,
    /// including a named destination, or <c>/A</c>, any action), its colour
    /// and style, its structure element and whether it is open are written
    /// back exactly as they were. For an untouched or renamed bookmark.
    /// </summary>
    /// <param name="source">
    /// The bookmark as <c>OutlineReader.GetOutlines</c> read it from the same
    /// document that is passed to <see cref="OutlineWriter.Apply"/>.
    /// </param>
    /// <param name="title">The bookmark's display title.</param>
    /// <param name="children">The nested child bookmarks.</param>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    public OutlineEntry(OutlineItem source, string title, IReadOnlyList<OutlineEntry> children)
    {
        ArgumentNullException.ThrowIfNull(source);
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Source = source;
        PageIndex = source.DestinationPageIndex;
        Children = children ?? throw new ArgumentNullException(nameof(children));
    }

    /// <summary>Gets the bookmark's display title.</summary>
    public string Title { get; }

    /// <summary>
    /// Gets the exact destination the bookmark goes to, or null (3.33.0).
    /// Null for an entry made with a page index (it goes to the whole page,
    /// <c>/Fit</c>) or kept from a <see cref="Source"/>.
    /// </summary>
    public PdfDestination? Destination { get; }

    /// <summary>
    /// Gets the bookmark this entry was kept from, or null (3.33.0). Its
    /// target, colour, style, structure element and open state are written
    /// back as they were.
    /// </summary>
    public OutlineItem? Source { get; }

    /// <summary>Gets the zero-based destination page index.</summary>
    public int PageIndex { get; }

    /// <summary>Gets the nested child bookmarks, if any.</summary>
    public IReadOnlyList<OutlineEntry> Children { get; }
}
