// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.4.5 — Axial and radial shadings (sh operator)
// PHASE: Phase 2 — item 11, raster shading support
//
// Carries an axial or radial gradient, baked to page space, for the rasterizer
// to paint within the active clip. The gradient is described by its geometry
// (two points and, for radial, two radii) plus a small set of pre-sampled
// colour stops along the parametric axis, mirroring the SVG display-list path.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering.Raster;

/// <summary>A single sampled gradient stop: an offset in [0, 1] and its colour.</summary>
public readonly struct GradientStop
{
    /// <summary>Initialises a <see cref="GradientStop"/>.</summary>
    /// <param name="offset">The normalised offset along the axis, in [0, 1].</param>
    /// <param name="color">The colour at this offset.</param>
    public GradientStop(double offset, ColorF color)
    {
        Offset = offset;
        Color = color;
    }

    /// <summary>Gets the normalised offset along the axis, in [0, 1].</summary>
    public double Offset { get; }

    /// <summary>Gets the colour at this offset.</summary>
    public ColorF Color { get; }
}

/// <summary>
/// Paints an axial or radial shading (the <c>sh</c> operator) across the active
/// clip region. Geometry is in page space with the CTM already applied.
/// </summary>
public sealed class ShadeOp : RenderOp
{
    /// <summary>Initialises a <see cref="ShadeOp"/>.</summary>
    /// <param name="isRadial">True for a radial shading; false for axial.</param>
    /// <param name="x0">Start point / inner-circle centre x, page space.</param>
    /// <param name="y0">Start point / inner-circle centre y, page space.</param>
    /// <param name="x1">End point / outer-circle centre x, page space.</param>
    /// <param name="y1">End point / outer-circle centre y, page space.</param>
    /// <param name="r0">Inner circle radius in page space (radial only).</param>
    /// <param name="r1">Outer circle radius in page space (radial only).</param>
    /// <param name="extendStart">Whether the shading extends before the axis start.</param>
    /// <param name="extendEnd">Whether the shading extends past the axis end.</param>
    /// <param name="stops">Sampled colour stops in increasing offset order.</param>
    /// <param name="clips">Clip paths active when this op was emitted.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stops"/> is null.</exception>
    public ShadeOp(
        bool isRadial,
        double x0,
        double y0,
        double x1,
        double y1,
        double r0,
        double r1,
        bool extendStart,
        bool extendEnd,
        IReadOnlyList<GradientStop> stops,
        IReadOnlyList<ClipPath>? clips = null)
        : base(clips)
    {
        Stops = stops ?? throw new ArgumentNullException(nameof(stops));
        IsRadial = isRadial;
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        R0 = r0;
        R1 = r1;
        ExtendStart = extendStart;
        ExtendEnd = extendEnd;
    }

    /// <summary>Gets a value indicating whether this is a radial shading.</summary>
    public bool IsRadial { get; }

    /// <summary>Gets the start point / inner-circle centre x in page space.</summary>
    public double X0 { get; }

    /// <summary>Gets the start point / inner-circle centre y in page space.</summary>
    public double Y0 { get; }

    /// <summary>Gets the end point / outer-circle centre x in page space.</summary>
    public double X1 { get; }

    /// <summary>Gets the end point / outer-circle centre y in page space.</summary>
    public double Y1 { get; }

    /// <summary>Gets the inner circle radius in page space (radial only).</summary>
    public double R0 { get; }

    /// <summary>Gets the outer circle radius in page space (radial only).</summary>
    public double R1 { get; }

    /// <summary>Gets whether the shading extends before the axis start.</summary>
    public bool ExtendStart { get; }

    /// <summary>Gets whether the shading extends past the axis end.</summary>
    public bool ExtendEnd { get; }

    /// <summary>Gets the sampled colour stops in increasing offset order.</summary>
    public IReadOnlyList<GradientStop> Stops { get; }
}
