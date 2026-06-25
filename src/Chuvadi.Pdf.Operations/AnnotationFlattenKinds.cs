// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5 (annotations), §12.7 (interactive forms)
// PHASE: Document operations — annotation/form flattening.

using System;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Selects which annotation kinds <see cref="AnnotationFlattener"/> bakes into
/// page content. An annotation's kind is decided by its <c>/Subtype</c>:
/// <c>/Widget</c> annotations (AcroForm fields) are <see cref="FormFields"/>;
/// every other subtype (markup, stamps, ink, links, …) is <see cref="Markup"/>.
/// </summary>
[Flags]
public enum AnnotationFlattenKinds
{
    /// <summary>Flatten nothing.</summary>
    None = 0,

    /// <summary>Flatten AcroForm field widgets (<c>/Subtype /Widget</c>).</summary>
    FormFields = 1,

    /// <summary>Flatten every non-widget annotation subtype (markup, stamp, ink, …).</summary>
    Markup = 2,

    /// <summary>Flatten both form-field widgets and markup annotations.</summary>
    All = FormFields | Markup,
}
