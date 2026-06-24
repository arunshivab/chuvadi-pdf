// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.8 (XFA Forms), §7.7.2 (/NeedsRendering)
// PHASE: Document introspection — classify XFA usage.

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Classifies how a document uses XFA (XML Forms Architecture), so a consumer
/// can tell forms that render from the page content apart from dynamic XFA that
/// needs a dedicated processor and may otherwise appear blank.
/// PDF 32000-1:2008 §12.7.8 (XFA), §7.7.2 (catalog <c>/NeedsRendering</c>).
/// </summary>
public enum XfaKind
{
    /// <summary>The document has no XFA form.</summary>
    None = 0,

    /// <summary>
    /// Static XFA: an <c>/XFA</c> entry is present and the form has a fixed
    /// layout that renders from the page content, with no traditional AcroForm
    /// fields alongside it.
    /// </summary>
    Static = 1,

    /// <summary>
    /// Hybrid XFA: an <c>/XFA</c> entry is present alongside traditional AcroForm
    /// fields, so the form also renders in viewers that do not process XFA.
    /// </summary>
    Hybrid = 2,

    /// <summary>
    /// Dynamic XFA: the catalog requests rendering (<c>/NeedsRendering true</c>);
    /// the form's layout is produced by an XFA processor and the page content may
    /// be blank without it.
    /// </summary>
    Dynamic = 3,
}
