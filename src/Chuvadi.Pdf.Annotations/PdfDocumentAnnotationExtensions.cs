// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R3 — Page-scoped annotation accessor

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Documents;

namespace Chuvadi.Pdf.Annotations;

/// <summary>
/// Extension methods that surface annotation reading directly on
/// <see cref="PdfDocument"/> and <see cref="PdfPage"/>.
/// </summary>
/// <remarks>
/// <para>
/// Imported via <c>using Chuvadi.Pdf.Annotations;</c>:
/// </para>
/// <code>
///   using Chuvadi.Pdf.Annotations;
///   IReadOnlyList&lt;PdfAnnotation&gt; annots = document.GetAnnotations(pageIndex: 0);
/// </code>
/// <para>
/// These are thin wrappers over
/// <see cref="AnnotationReader.GetAnnotations(PdfDocument, int)"/> for callers who
/// prefer the fluent <c>doc.GetAnnotations(i)</c> shape.
/// </para>
/// </remarks>
public static class PdfDocumentAnnotationExtensions
{
    /// <summary>
    /// Returns all annotations on the page at <paramref name="pageIndex"/>.
    /// </summary>
    /// <param name="document">The document to read from.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <returns>
    /// The annotations in PDF <c>/Annots</c> order, or an empty list when the
    /// page has no annotations.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="document"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="pageIndex"/> is outside
    /// <c>[0, document.PageCount)</c>.
    /// </exception>
    public static IReadOnlyList<PdfAnnotation> GetAnnotations(
        this PdfDocument document, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        return AnnotationReader.GetAnnotations(document, pageIndex);
    }

    /// <summary>
    /// Returns annotations from every page in the document, in page order.
    /// </summary>
    /// <param name="document">The document to read from.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="document"/> is null.
    /// </exception>
    public static IReadOnlyList<PdfAnnotation> GetAllAnnotations(this PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return AnnotationReader.GetAllAnnotations(document);
    }
}
