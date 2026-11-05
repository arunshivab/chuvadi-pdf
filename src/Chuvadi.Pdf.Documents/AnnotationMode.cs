// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — annotation modes for viewing and printing (D-108 item c, D-146)
// SPEC:  PDF 32000-1:2008 §12.5.3 — annotation flags (Hidden, Print, NoView)

namespace Chuvadi.Pdf.Documents;

/// <summary>Which annotations a renderer or text extractor includes with a page.</summary>
public enum AnnotationMode
{
    /// <summary>
    /// As a viewer shows the page on screen (the default): every annotation
    /// except Hidden, NoView and Popup annotations.
    /// </summary>
    View = 0,

    /// <summary>
    /// As the page prints: only annotations with the Print flag, except Hidden
    /// and Popup annotations. NoView is ignored, so print-only stamps appear.
    /// </summary>
    Print = 1,

    /// <summary>
    /// Form fields only, as a print dialog's "form fields only": widget
    /// annotations under the <see cref="Print"/> rules, without the page's own
    /// content and without other annotations.
    /// </summary>
    FormFieldsOnly = 2,

    /// <summary>The page content without any annotations.</summary>
    None = 3,
}
