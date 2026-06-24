// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3 — Page tree
// PHASE: Document operations — ordered page-assembly selector.

using System;
using Chuvadi.Pdf.Documents;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Identifies a single source page for
/// <see cref="PageOperations.Assemble(System.IO.Stream, System.Collections.Generic.IReadOnlyList{PageSelector})"/>:
/// a source document paired with a zero-based page index. The same selector — or
/// the same document with different indices — may appear any number of times in
/// an assembly list, which is how duplicate and interleaved output pages are
/// expressed.
/// </summary>
public readonly struct PageSelector : IEquatable<PageSelector>
{
    /// <summary>
    /// Initialises a selector for one page of a source document.
    /// </summary>
    /// <param name="document">The source document the page is drawn from.</param>
    /// <param name="pageIndex">The zero-based index of the page within <paramref name="document"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document"/> is null.</exception>
    public PageSelector(PdfDocument document, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        PageIndex = pageIndex;
    }

    /// <summary>Gets the source document the page is drawn from.</summary>
    public PdfDocument Document { get; }

    /// <summary>Gets the zero-based index of the page within <see cref="Document"/>.</summary>
    public int PageIndex { get; }

    /// <summary>
    /// Determines whether this selector equals <paramref name="other"/>: the same
    /// document instance (by reference) and the same page index.
    /// </summary>
    /// <param name="other">The selector to compare with.</param>
    /// <returns>True when both refer to the same document and page index.</returns>
    public bool Equals(PageSelector other)
    {
        return ReferenceEquals(Document, other.Document) && PageIndex == other.PageIndex;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is PageSelector other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(Document, PageIndex);
    }

    /// <summary>Determines whether two selectors are equal.</summary>
    /// <param name="left">The first selector.</param>
    /// <param name="right">The second selector.</param>
    /// <returns>True when the selectors are equal.</returns>
    public static bool operator ==(PageSelector left, PageSelector right)
    {
        return left.Equals(right);
    }

    /// <summary>Determines whether two selectors are unequal.</summary>
    /// <param name="left">The first selector.</param>
    /// <param name="right">The second selector.</param>
    /// <returns>True when the selectors are unequal.</returns>
    public static bool operator !=(PageSelector left, PageSelector right)
    {
        return !left.Equals(right);
    }
}
