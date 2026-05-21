// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.4 — Clipping path operators
// PHASE: v2.0.0 R1 D3c-1 — DisplayList types

using System;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// A clipping path applied to a single render operation.
/// </summary>
/// <remarks>
/// <para>
/// Chuvadi's display list represents clipping as per-operation data rather
/// than as a paired Push/Pop on the renderer's state stack. Each
/// <see cref="RenderOp"/> carries the list of clip paths active at the
/// moment the op was emitted by the builder. A point is painted only when
/// it lies inside every clip path in the list (intersection semantics).
/// </para>
/// <para>
/// This model has two advantages over a stack-of-pushes alternative: the
/// display list cannot end up in an inconsistent state from malformed
/// content streams, and consumers (rasterizer, SVG writer) handle clipping
/// uniformly without tracking nested clip state across ops.
/// </para>
/// <para>
/// PDF 32000-1:2008 §8.5.4 — Clipping path operators (W, W*).
/// </para>
/// </remarks>
public readonly struct ClipPath
{
    /// <summary>
    /// Initialises a <see cref="ClipPath"/> with a path and a fill rule.
    /// </summary>
    /// <param name="path">The clip path geometry in PDF user space.</param>
    /// <param name="rule">The fill rule used to evaluate the clip region.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="path"/> is null.
    /// </exception>
    public ClipPath(Path path, FillRule rule)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Rule = rule;
    }

    /// <summary>Gets the clip path geometry in PDF user space.</summary>
    public Path Path { get; }

    /// <summary>Gets the fill rule used to evaluate the clip region.</summary>
    public FillRule Rule { get; }
}
