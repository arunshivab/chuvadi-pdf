// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.10 — Form XObjects
// PHASE: v2.0.0 R1 D3c-1 — DisplayList types

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Paints another <see cref="PageDisplayList"/> with a composing transform.
/// </summary>
/// <remarks>
/// <para>
/// Emitted by the builder for the PDF Do operator when the named XObject
/// has Subtype /Form. Form XObjects are reusable content blocks (logos,
/// page-number stamps, repeating headers) defined in their own coordinate
/// space; <see cref="CtmComposition"/> maps that inner space into the
/// outer user space.
/// </para>
/// <para>
/// Resolving Form XObjects to a sub-display-list happens once at build
/// time. The painter recurses into <see cref="Inner"/> exactly the same
/// way it walks the top-level page list — there is no separate Form
/// XObject code path on the rendering side.
/// </para>
/// </remarks>
public sealed class NestedDisplayListOp : RenderOp
{
    /// <summary>
    /// Initialises a <see cref="NestedDisplayListOp"/>.
    /// </summary>
    /// <param name="inner">The sub-display-list to paint.</param>
    /// <param name="ctmComposition">
    /// The CTM contribution of the Form XObject (the Matrix entry on the
    /// XObject dictionary composed with the CTM at the Do operator).
    /// </param>
    /// <param name="clips">
    /// Clip paths active when this op was emitted. Null or empty means no clip.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="inner"/> is null.
    /// </exception>
    public NestedDisplayListOp(
        PageDisplayList inner,
        Transform ctmComposition,
        IReadOnlyList<ClipPath>? clips = null)
        : base(clips)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        CtmComposition = ctmComposition;
    }

    /// <summary>Gets the sub-display-list.</summary>
    public PageDisplayList Inner { get; }

    /// <summary>Gets the transform composing inner-space into the parent's user space.</summary>
    public Transform CtmComposition { get; }
}
