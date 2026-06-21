// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §11.6.5.2 — Soft-mask dictionaries
// PHASE: Phase 2 — item 12, ExtGState soft masks (/SMask), SVG path
//
// Describes an active soft mask for the SVG/display-list path: the masking
// transparency group captured as its own display list, plus how to derive and
// place its coverage. The SVG renderer turns this into an SVG <mask> definition.

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// An active soft mask (ExtGState <c>/SMask</c>) for the SVG path: the masking
/// group's display list plus how its coverage is derived and placed.
/// </summary>
public sealed class SoftMaskInfo
{
    /// <summary>Initialises a <see cref="SoftMaskInfo"/>.</summary>
    /// <param name="group">The masking group's display list, in group-local space.</param>
    /// <param name="composition">Group-local → page-space transform (the CTM when the mask was set).</param>
    /// <param name="isLuminosity">True for a luminosity mask; false for an alpha mask.</param>
    /// <param name="backdrop">Backdrop luminosity for areas the group does not paint, in [0, 1].</param>
    public SoftMaskInfo(
        PageDisplayList group, AffineMatrix composition, bool isLuminosity, double backdrop)
    {
        Group = group;
        Composition = composition;
        IsLuminosity = isLuminosity;
        Backdrop = backdrop;
    }

    /// <summary>Gets the masking group's display list, in group-local space.</summary>
    public PageDisplayList Group { get; }

    /// <summary>Gets the group-local → page-space transform.</summary>
    public AffineMatrix Composition { get; }

    /// <summary>Gets a value indicating whether this is a luminosity mask (else alpha).</summary>
    public bool IsLuminosity { get; }

    /// <summary>Gets the backdrop luminosity for unpainted areas, in [0, 1].</summary>
    public double Backdrop { get; }
}
