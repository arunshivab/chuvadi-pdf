// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.2 (Annotation /Rect), §12.7.8 (XFA Forms)
// PHASE: Document introspection — XFA data-field geometry.

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Best-effort geometry for an <see cref="XfaDataField"/>, taken from a matching
/// AcroForm widget annotation's <c>/Rect</c>. Geometry is only available for
/// fields whose value is mirrored by a traditional AcroForm widget (typical of
/// hybrid XFA); for static or dynamic XFA whose layout is produced by an XFA
/// processor, no widget exists and <see cref="XfaDataField.Geometry"/> is null.
/// PDF 32000-1:2008 §12.5.2.
/// </summary>
public sealed class XfaGeometry
{
    internal XfaGeometry(int pageIndex, PdfRectangle rectangle)
    {
        PageIndex = pageIndex;
        Rectangle = rectangle;
    }

    /// <summary>
    /// Gets the zero-based index of the page carrying the matched widget, or
    /// <c>-1</c> when the page could not be determined from the page
    /// <c>/Annots</c> arrays.
    /// </summary>
    public int PageIndex { get; }

    /// <summary>
    /// Gets the widget rectangle in PDF user space (the AcroForm widget's
    /// <c>/Rect</c>), suitable for overlaying the field's value onto the
    /// rendered page. PDF 32000-1:2008 §12.5.2.
    /// </summary>
    public PdfRectangle Rectangle { get; }
}
