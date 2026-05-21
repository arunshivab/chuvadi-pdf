// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.3 — Path-painting operators (f, F, f*, B, b, B*, b*)
// PHASE: v2.0.0 R1 D3c-1 — DisplayList types

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Fills a path with a flat colour, applying the configured fill rule.
/// </summary>
/// <remarks>
/// <para>
/// Emitted by the builder for the PDF path-painting operators f / F / f*
/// (fill only) and the fill portion of B / B* / b / b* (fill-then-stroke
/// operators emit a <see cref="FillPathOp"/> followed by a
/// <see cref="StrokePathOp"/> sharing the same path data).
/// </para>
/// <para>
/// The path is in PDF user space with the current transformation matrix
/// already applied.
/// </para>
/// </remarks>
public sealed class FillPathOp : RenderOp
{
    /// <summary>
    /// Initialises a <see cref="FillPathOp"/>.
    /// </summary>
    /// <param name="path">The path to fill, with CTM already applied.</param>
    /// <param name="color">The fill colour.</param>
    /// <param name="rule">The fill rule (non-zero winding or even-odd).</param>
    /// <param name="clips">
    /// Clip paths active when this op was emitted. Null or empty means no clip.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="path"/> is null.
    /// </exception>
    public FillPathOp(
        Path path,
        ColorF color,
        FillRule rule,
        IReadOnlyList<ClipPath>? clips = null)
        : base(clips)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Color = color;
        Rule = rule;
    }

    /// <summary>Gets the path to fill.</summary>
    public Path Path { get; }

    /// <summary>Gets the fill colour.</summary>
    public ColorF Color { get; }

    /// <summary>Gets the fill rule.</summary>
    public FillRule Rule { get; }
}
