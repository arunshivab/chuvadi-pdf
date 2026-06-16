// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 — Document outline (bookmarks)
// PHASE: Document operations — outline authoring input model.

using System;
using System.Collections.Generic;

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

    /// <summary>Gets the bookmark's display title.</summary>
    public string Title { get; }

    /// <summary>Gets the zero-based destination page index.</summary>
    public int PageIndex { get; }

    /// <summary>Gets the nested child bookmarks, if any.</summary>
    public IReadOnlyList<OutlineEntry> Children { get; }
}
