// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.9 — Images
// PHASE: v2.0.0 R1 D3c-1 — DisplayList types

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Images;

namespace Chuvadi.Pdf.Rendering.Raster;

/// <summary>
/// Paints a decoded image at the position specified by a transformation matrix.
/// </summary>
/// <remarks>
/// <para>
/// Emitted by the builder for the PDF Do operator when the named XObject
/// has Subtype /Image. Inline images (BI/ID/EI) emit the same op type.
/// </para>
/// <para>
/// In PDF, an image XObject is conceptually drawn in the unit square
/// (0,0)–(1,1) in image space. The transform that maps unit-square corners
/// to user-space corners is the CTM that was in effect at the Do operator.
/// <see cref="DeviceTransform"/> captures that CTM directly, so the painter
/// can place and rotate/skew/scale the image as the PDF intends without
/// needing to track CTM state.
/// </para>
/// <para>
/// Image colour conversion (CMYK to RGB, indexed colour expansion, etc.)
/// is performed at decode time. <see cref="ImageFrame.Pixels"/> is always
/// in <c>PixelBuffer</c> BGRA format regardless of the original source.
/// </para>
/// </remarks>
public sealed class DrawImageOp : RenderOp
{
    /// <summary>
    /// Initialises a <see cref="DrawImageOp"/>.
    /// </summary>
    /// <param name="image">The decoded image frame.</param>
    /// <param name="deviceTransform">
    /// The CTM at the moment of the Do operator. Maps the image's unit
    /// square (0,0)–(1,1) into PDF user space.
    /// </param>
    /// <param name="clips">
    /// Clip paths active when this op was emitted. Null or empty means no clip.
    /// </param>
    /// <param name="alpha">
    /// Constant image opacity from ExtGState /ca, 0..1. Default 1 (opaque).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="image"/> is null.
    /// </exception>
    public DrawImageOp(
        ImageFrame image,
        Transform deviceTransform,
        IReadOnlyList<ClipPath>? clips = null,
        double alpha = 1.0)
        : base(clips)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        DeviceTransform = deviceTransform;
        Alpha = alpha < 0.0 ? 0.0 : (alpha > 1.0 ? 1.0 : alpha);
    }

    /// <summary>Gets the decoded image frame (BGRA pixels regardless of source format).</summary>
    public ImageFrame Image { get; }

    /// <summary>
    /// Gets the device-placement transform that maps the image's unit
    /// square (0,0)–(1,1) into PDF user space.
    /// </summary>
    public Transform DeviceTransform { get; }

    /// <summary>
    /// Gets the constant image opacity from ExtGState /ca (0..1). Multiplies
    /// the per-pixel alpha (including any /SMask) during compositing.
    /// </summary>
    public double Alpha { get; }
}
