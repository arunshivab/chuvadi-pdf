// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 — Text-showing operators
// PHASE: Phase 2.1 — display-list intermediate

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// A grouped text-showing operation that retains glyph-level structure for
/// reading-order extraction.
/// </summary>
/// <remarks>
/// <para>
/// In contrast to <see cref="DrawGlyphOp"/>, which emits one outline per
/// rendered glyph (suitable for vector painting), <see cref="TextOp"/>
/// groups the glyphs produced by a single text-showing operator (<c>Tj</c>,
/// <c>TJ</c>, <c>'</c>, <c>"</c>) so a later pass can recover the original
/// Unicode string, baseline, and direction.
/// </para>
/// <para>
/// The <see cref="Transform"/> is the combined text-matrix-times-CTM at the
/// moment the operator was issued; per-glyph positions in <see cref="Glyphs"/>
/// are in text-local space and must be mapped through this transform to
/// reach PDF user space.
/// </para>
/// <para>
/// <b>Builder status (v2.0.0):</b> the current <c>DisplayListBuilder</c>
/// emits per-glyph <see cref="DrawGlyphOp"/>s; it does not yet emit
/// <see cref="TextOp"/>s. The type is shipped so that the Phase 2.1
/// builder pass and downstream <see cref="TextRunExtractor"/> can be
/// implemented in subsequent releases without a public-API churn.
/// </para>
/// </remarks>
public sealed class TextOp : RenderOp
{
    /// <summary>Initialises a <see cref="TextOp"/>.</summary>
    /// <param name="transform">
    /// The text-matrix-times-CTM in effect at the moment the text-showing
    /// operator was issued.
    /// </param>
    /// <param name="fontSize">
    /// The font size in PDF user-space points at the time the operator
    /// was issued.
    /// </param>
    /// <param name="glyphs">
    /// Per-glyph entries in text-local space, in stream order.
    /// </param>
    /// <param name="clips">
    /// Clip paths active when this op was emitted. Null or empty means no clip.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="glyphs"/> is null.
    /// </exception>
    public TextOp(
        Transform transform,
        double fontSize,
        IReadOnlyList<DisplayListGlyph> glyphs,
        IReadOnlyList<ClipPath>? clips = null)
        : base(clips)
    {
        ArgumentNullException.ThrowIfNull(glyphs);

        Transform = transform;
        FontSize = fontSize;
        Glyphs = glyphs;
    }

    /// <summary>
    /// Gets the text-matrix-times-CTM in effect when the operator was issued.
    /// Maps text-local-space glyph positions into PDF user space.
    /// </summary>
    public Transform Transform { get; }

    /// <summary>Gets the font size in PDF user-space points.</summary>
    public double FontSize { get; }

    /// <summary>Gets the per-glyph entries in text-local space, in stream order.</summary>
    public IReadOnlyList<DisplayListGlyph> Glyphs { get; }
}
