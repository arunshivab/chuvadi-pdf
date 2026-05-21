// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R2 — display-list convenience surface

using System;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Convenience extensions that let callers build a <see cref="PageDisplayList"/>
/// directly from a <see cref="PdfDocument"/> or <see cref="PdfPage"/> without
/// hand-wiring the object store.
/// </summary>
/// <remarks>
/// <para>
/// These extensions are discovered through <c>using Chuvadi.Pdf.Rendering.DisplayList;</c>.
/// Callers that import the namespace can write:
/// </para>
/// <code>
///   using Chuvadi.Pdf.Rendering.DisplayList;
///   PageDisplayList list = document.BuildDisplayList(pageIndex: 0);
/// </code>
/// <para>
/// The extension form (rather than an instance method on <see cref="PdfDocument"/>)
/// is used so that <see cref="Chuvadi.Pdf.Documents"/> does not have to take a
/// reverse dependency on this project.
/// </para>
/// </remarks>
public static class PdfDocumentDisplayListExtensions
{
    /// <summary>
    /// Builds a <see cref="PageDisplayList"/> for the page at <paramref name="pageIndex"/>
    /// in <paramref name="document"/>.
    /// </summary>
    /// <param name="document">The document to read from.</param>
    /// <param name="pageIndex">The zero-based page index.</param>
    /// <returns>The display list for the page.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="document"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pageIndex"/> is outside
    /// <c>[0, document.PageCount)</c>.
    /// </exception>
    public static PageDisplayList BuildDisplayList(this PdfDocument document, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (pageIndex < 0 || pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                pageIndex,
                $"Page index must be in [0, {document.PageCount}).");
        }

        PdfPage page = document.Pages[pageIndex];
        return DisplayListBuilder.Build(page, document.Objects);
    }

    /// <summary>
    /// Builds a <see cref="PageDisplayList"/> for <paramref name="page"/>, resolving
    /// indirect references through <paramref name="objects"/>.
    /// </summary>
    /// <param name="page">The page to interpret.</param>
    /// <param name="objects">The document's object store.</param>
    /// <returns>The display list for the page.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="page"/> or <paramref name="objects"/> is null.
    /// </exception>
    public static PageDisplayList BuildDisplayList(this PdfPage page, PdfObjectStore objects)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(objects);

        return DisplayListBuilder.Build(page, objects);
    }
}
