// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 (D-173) — the transparency groups of a page: a table with one
//        entry per group, and the group ids on every op inside one, so a
//        printer can composite groups itself.
// SPEC:  PDF 32000-1:2008 §11.4.7 (groups), §11.6.6 (transparency group
//        XObjects), §8.10 (form XObjects).

using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// One transparency group on a page (3.31): a form XObject with
/// <c>/Group /S /Transparency</c>, each time it is drawn. Its ops are one
/// unbroken run of the page's op list, <see cref="FirstOpIndex"/> to
/// <see cref="FirstOpIndex"/> + <see cref="OpCount"/> - 1, and a nested
/// group's run lies inside its parent's. Groups that draw nothing are left
/// out. Ids are 1, 2, 3 … in the order the groups start.
/// </summary>
public sealed record TransparencyGroup
{
    /// <summary>The group's id, as used in <see cref="RenderOp.GroupIds"/>.</summary>
    public required int Id { get; init; }

    /// <summary>The id of the group this one is drawn inside, or null for a top-level group.</summary>
    public int? ParentId { get; init; }

    /// <summary>How many groups enclose this one, plus one (1 for a top-level group).</summary>
    public required int Depth { get; init; }

    /// <summary>The <c>/I</c> (isolated) flag.</summary>
    public bool Isolated { get; init; }

    /// <summary>The <c>/K</c> (knockout) flag.</summary>
    public bool Knockout { get; init; }

    /// <summary>The <c>/CS</c> colour space family name (for example "DeviceRGB" or "ICCBased"), or null.</summary>
    public string? ColorSpace { get; init; }

    /// <summary>The form's <c>/BBox</c> mapped to page space: the bounding box of its four transformed corners.</summary>
    public PdfRectangle Box { get; init; }

    /// <summary>The form-to-page transform: the CTM when the form is drawn, with its <c>/Matrix</c>.</summary>
    public AffineMatrix Matrix { get; init; } = AffineMatrix.Identity;

    /// <summary>
    /// The constant alpha the group is composited with (<c>/ca</c> when the
    /// form is drawn, relative to the parent group or the page).
    /// </summary>
    public double Opacity { get; init; } = 1.0;

    /// <summary>The stroking constant alpha (<c>/CA</c>) in effect when the form is drawn.</summary>
    public double StrokeOpacity { get; init; } = 1.0;

    /// <summary>The blend mode the group is composited with.</summary>
    public PdfBlendMode BlendMode { get; init; } = PdfBlendMode.Normal;

    /// <summary>The soft mask the group is composited through, or null.</summary>
    public SoftMaskInfo? SoftMask { get; init; }

    /// <summary>The form XObject.</summary>
    public PdfStream? Form { get; init; }

    /// <summary>The index of the group's first op in the page's op list.</summary>
    public int FirstOpIndex { get; init; }

    /// <summary>The number of ops in the group's run, nested groups' ops included.</summary>
    public int OpCount { get; init; }
}

/// <summary>
/// An op's own transparency inside its innermost group (3.31): the values the
/// group's content set, starting from the defaults at the group's start
/// (PDF 32000-1:2008 §11.6.6).
/// </summary>
/// <param name="FillOpacity">The non-stroking constant alpha.</param>
/// <param name="StrokeOpacity">The stroking constant alpha.</param>
/// <param name="BlendMode">The blend mode.</param>
/// <param name="SoftMask">The soft mask, or null.</param>
public sealed record OpTransparency(double FillOpacity, double StrokeOpacity, PdfBlendMode BlendMode, SoftMaskInfo? SoftMask);
