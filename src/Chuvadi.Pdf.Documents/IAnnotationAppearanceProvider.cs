// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — appearances made from an annotation's own entries (D-146)

using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Supplies an appearance stream for an annotation when the file's own is
/// missing or out of date (a form field when <c>/NeedAppearances</c> is true,
/// or an annotation with no <c>/AP</c>). The stream lives in memory only; the
/// file is never changed. <c>Chuvadi.Pdf.Annotations</c> provides the
/// implementation the renderers use.
/// </summary>
public interface IAnnotationAppearanceProvider
{
    /// <summary>
    /// Returns the appearance to draw for <paramref name="annotation"/>:
    /// a generated form XObject, or <paramref name="stored"/> to keep the
    /// file's own, or null to draw nothing.
    /// </summary>
    /// <param name="annotation">The annotation dictionary.</param>
    /// <param name="stored">The file's normal appearance (with <c>/AS</c> applied), or null.</param>
    /// <param name="page">The page the annotation is on.</param>
    /// <param name="objects">The document's objects.</param>
    /// <returns>The appearance to use, or null.</returns>
    PdfStream? GetAppearance(PdfDictionary annotation, PdfStream? stored, PdfPage page, PdfObjectStore objects);
}
