// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5.6 — Annotation types
// PHASE: Phase 1.1 — Chuvadi.Pdf.Annotations (v2.0.0 R3 adds shape subtypes)

namespace Chuvadi.Pdf.Annotations;

/// <summary>
/// PDF annotation subtype. PDF 32000-1:2008 §12.5.6 defines a larger list;
/// Chuvadi models the subtypes most relevant to clinical and document-review
/// workflows. Other subtypes load as <see cref="Unknown"/> but preserve their
/// basic geometry and contents.
/// </summary>
public enum AnnotationType
{
    /// <summary>An annotation subtype not modelled by Chuvadi.</summary>
    Unknown,

    /// <summary>Sticky-note text annotation (§12.5.6.4).</summary>
    Text,

    /// <summary>Hyperlink annotation (§12.5.6.5).</summary>
    Link,

    /// <summary>Free-text annotation drawn directly on the page (§12.5.6.6).</summary>
    FreeText,

    /// <summary>Highlight markup annotation (§12.5.6.10).</summary>
    Highlight,

    /// <summary>Underline markup annotation (§12.5.6.10).</summary>
    Underline,

    /// <summary>Squiggly underline markup annotation (§12.5.6.10).</summary>
    Squiggly,

    /// <summary>Strike-out markup annotation (§12.5.6.10).</summary>
    StrikeOut,

    /// <summary>Rubber-stamp annotation, e.g., "Approved" (§12.5.6.12).</summary>
    Stamp,

    /// <summary>Free-hand ink annotation (§12.5.6.13).</summary>
    Ink,

    /// <summary>Square (axis-aligned rectangle outline) shape annotation (§12.5.6.8).</summary>
    Square,

    /// <summary>Circle (ellipse) shape annotation (§12.5.6.8).</summary>
    Circle,

    /// <summary>Straight-line shape annotation (§12.5.6.7).</summary>
    Line,

    /// <summary>Open polyline shape annotation (§12.5.6.9).</summary>
    Polyline,

    /// <summary>Closed polygon shape annotation (§12.5.6.9).</summary>
    Polygon,
}
