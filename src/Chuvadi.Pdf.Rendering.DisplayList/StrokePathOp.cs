// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.3 — Path-painting operators (S, s, B, b, B*, b*)
// PHASE: v2.0.0 R1 D3c-1 — DisplayList types

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Strokes a path with the supplied <see cref="StrokeStyle"/>.
/// </summary>
/// <remarks>
/// <para>
/// Emitted by the builder for the PDF operators S / s (stroke only) and
/// for the stroke portion of B / B* / b / b* (fill-then-stroke operators
/// emit a <see cref="FillPathOp"/> followed by a <see cref="StrokePathOp"/>
/// sharing the same path data).
/// </para>
/// <para>
/// The path is in PDF user space with the current transformation matrix
/// already applied. <see cref="StrokeStyle.Color"/> holds the stroke
/// colour, while line width, cap, join, miter limit, and dash pattern are
/// captured at emission time.
/// </para>
/// </remarks>
public sealed class StrokePathOp : RenderOp
{
    /// <summary>
    /// Initialises a <see cref="StrokePathOp"/>.
    /// </summary>
    /// <param name="path">The path to stroke, with CTM already applied.</param>
    /// <param name="style">The stroke parameters including colour, width, cap, join, miter, dash.</param>
    /// <param name="clips">
    /// Clip paths active when this op was emitted. Null or empty means no clip.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="path"/> or <paramref name="style"/> is null.
    /// </exception>
    public StrokePathOp(
        Path path,
        StrokeStyle style,
        IReadOnlyList<ClipPath>? clips = null)
        : base(clips)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Style = style ?? throw new ArgumentNullException(nameof(style));
    }

    /// <summary>Gets the path to stroke.</summary>
    public Path Path { get; }

    /// <summary>Gets the stroke parameters (colour, width, cap, join, miter limit, dash).</summary>
    public StrokeStyle Style { get; }
}
