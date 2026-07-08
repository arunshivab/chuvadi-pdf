// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.6.5.2 — Soft-mask dictionaries
// PHASE: Phase 2 — item 12, ExtGState soft masks (/SMask)
//
// Describes an active soft mask: a transparency group (rendered as its own
// display list) whose luminosity or alpha gates everything painted while the
// mask is in effect. The group is rendered to a mask buffer at rasterization
// time; this type only carries what the rasterizer needs to build and place it.

using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering.Raster;

/// <summary>
/// An active soft mask (ExtGState <c>/SMask</c>): the masking transparency
/// group plus how to derive and place its per-pixel coverage.
/// </summary>
public sealed class RasterSoftMaskInfo
{
    /// <summary>Initialises a <see cref="RasterSoftMaskInfo"/>.</summary>
    /// <param name="group">The masking group's display list, in group-local space.</param>
    /// <param name="composition">Group-local → page-space transform (the CTM in effect when the mask was set).</param>
    /// <param name="isLuminosity">True for a luminosity mask; false for an alpha mask.</param>
    /// <param name="backdrop">Backdrop luminosity for areas the group does not paint, in [0, 1].</param>
    public RasterSoftMaskInfo(
        PageDisplayList group, Transform composition, bool isLuminosity, double backdrop)
    {
        Group = group;
        Composition = composition;
        IsLuminosity = isLuminosity;
        Backdrop = backdrop;
    }

    /// <summary>Gets the masking group's display list, in group-local space.</summary>
    public PageDisplayList Group { get; }

    /// <summary>Gets the group-local → page-space transform.</summary>
    public Transform Composition { get; }

    /// <summary>Gets a value indicating whether this is a luminosity mask (else alpha).</summary>
    public bool IsLuminosity { get; }

    /// <summary>Gets the backdrop luminosity for unpainted areas, in [0, 1].</summary>
    public double Backdrop { get; }
}
