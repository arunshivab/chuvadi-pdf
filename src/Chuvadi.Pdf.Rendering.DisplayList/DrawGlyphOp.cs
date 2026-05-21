// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 — Text objects and operators
// PHASE: v2.0.0 R1 D3c-1 — DisplayList types

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Paints a single glyph outline.
/// </summary>
/// <remarks>
/// <para>
/// Emitted by the builder once per visible glyph in a text-showing operator
/// (Tj, TJ, ', "). Each call to a text-showing operator produces a sequence
/// of <see cref="DrawGlyphOp"/>s — one per glyph — with the path already
/// transformed into PDF user space by the combination of the text matrix,
/// font size, and CTM in effect at emission time.
/// </para>
/// <para>
/// The outline is filled (not stroked) by default; PDF supports stroked
/// and outline-only text rendering modes which a later op type may model.
/// In v2.0.0 R1 the rendering-mode-3 (invisible text) case is handled by
/// the builder simply not emitting glyph ops.
/// </para>
/// </remarks>
public sealed class DrawGlyphOp : RenderOp
{
    /// <summary>
    /// Initialises a <see cref="DrawGlyphOp"/>.
    /// </summary>
    /// <param name="path">The glyph outline in user space (CTM + text matrix applied).</param>
    /// <param name="color">The glyph fill colour.</param>
    /// <param name="clips">
    /// Clip paths active when this op was emitted. Null or empty means no clip.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="path"/> is null.
    /// </exception>
    public DrawGlyphOp(
        Path path,
        ColorF color,
        IReadOnlyList<ClipPath>? clips = null)
        : base(clips)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Color = color;
    }

    /// <summary>Gets the glyph outline path in user space.</summary>
    public Path Path { get; }

    /// <summary>Gets the fill colour.</summary>
    public ColorF Color { get; }
}
