// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.4 — Graphics state
// PHASE: v2.0.0 R1 D3c-2 — DisplayList builder

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Mutable graphics state used by <see cref="DisplayListBuilder"/> while
/// interpreting a content stream.
/// </summary>
/// <remarks>
/// <para>
/// This is distinct from <c>Chuvadi.Pdf.Content.GraphicsState</c> which is
/// scoped to text extraction. The builder needs the full graphics state
/// (CTM, fill and stroke colour, stroke parameters, text state, active
/// clipping paths) and its own stack semantics for q/Q.
/// </para>
/// <para>
/// The text matrix and text line matrix are NOT stored here. Per PDF
/// 32000-1:2008 §9.4.1, text matrices are not part of the graphics state
/// and are not saved or restored by q/Q. They live directly on the
/// builder's worker.
/// </para>
/// </remarks>
internal sealed class BuilderGraphicsState
{
    /// <summary>Current transformation matrix.</summary>
    public Transform Ctm { get; set; } = Transform.Identity;

    /// <summary>Non-stroking (fill) colour.</summary>
    public ColorF FillColor { get; set; } = ColorF.Black;

    /// <summary>Stroking colour.</summary>
    public ColorF StrokeColor { get; set; } = ColorF.Black;

    /// <summary>
    /// True when <see cref="FillColor"/> reflects a representable device
    /// colour (gray, RGB, or CMYK). False when the fill source is a
    /// pattern or shading we cannot represent today, in which case the
    /// builder suppresses fill emission.
    /// </summary>
    public bool FillValid { get; set; } = true;

    /// <summary>
    /// True when <see cref="StrokeColor"/> reflects a representable device
    /// colour. False when the stroke source is a pattern or shading.
    /// </summary>
    public bool StrokeValid { get; set; } = true;

    /// <summary>Line width in user-space units.</summary>
    public double LineWidth { get; set; } = 1.0;

    /// <summary>Line cap style.</summary>
    public LineCap LineCap { get; set; } = LineCap.Butt;

    /// <summary>Line join style.</summary>
    public LineJoin LineJoin { get; set; } = LineJoin.Miter;

    /// <summary>Miter join cutoff ratio.</summary>
    public double MiterLimit { get; set; } = 10.0;

    /// <summary>Dash pattern (empty for a solid line).</summary>
    public double[] DashPattern { get; set; } = Array.Empty<double>();

    /// <summary>Dash phase offset.</summary>
    public double DashOffset { get; set; } = 0.0;

    // ── Text state ────────────────────────────────────────────────────────
    // Text state IS saved/restored by q/Q (per §8.4.1 Table 52).

    /// <summary>Name of the currently selected font (the /F key in resources).</summary>
    public string FontName { get; set; } = string.Empty;

    /// <summary>
    /// Resource dictionary that contains the font reference for
    /// <see cref="FontName"/>. Captured at the time of Tf because Form
    /// XObjects may switch the active resources.
    /// </summary>
    public PdfDictionary? FontResources { get; set; }

    /// <summary>Font size in user-space units (set by Tf).</summary>
    public double FontSize { get; set; } = 0.0;

    /// <summary>Character spacing in unscaled text-space units (Tc).</summary>
    public double CharacterSpacing { get; set; } = 0.0;

    /// <summary>Word spacing in unscaled text-space units (Tw).</summary>
    public double WordSpacing { get; set; } = 0.0;

    /// <summary>Horizontal scaling as a percentage (Tz). Default 100.</summary>
    public double HorizontalScaling { get; set; } = 100.0;

    /// <summary>Leading in unscaled text-space units (TL).</summary>
    public double TextLeading { get; set; } = 0.0;

    /// <summary>Text rise in unscaled text-space units (Ts).</summary>
    public double TextRise { get; set; } = 0.0;

    /// <summary>Text rendering mode (Tr); 0 = fill (default).</summary>
    public int TextRenderingMode { get; set; } = 0;

    // ── Clipping ──────────────────────────────────────────────────────────

    /// <summary>
    /// Clipping paths active at this point in the content stream. Each
    /// path is intersected with the previous to form the effective clip
    /// region. Saved and restored by q/Q.
    /// </summary>
    public List<ClipPath> ActiveClips { get; set; } = new List<ClipPath>();

    /// <summary>
    /// Returns a deep copy of this state suitable for pushing onto the
    /// q/Q stack. The clip list is defensively copied; primitive fields
    /// are value-copied; the dash pattern array reference is shared
    /// (treated as immutable by the builder).
    /// </summary>
    public BuilderGraphicsState Clone()
    {
        return new BuilderGraphicsState
        {
            Ctm = Ctm,
            FillColor = FillColor,
            StrokeColor = StrokeColor,
            FillValid = FillValid,
            StrokeValid = StrokeValid,
            LineWidth = LineWidth,
            LineCap = LineCap,
            LineJoin = LineJoin,
            MiterLimit = MiterLimit,
            DashPattern = DashPattern,
            DashOffset = DashOffset,
            FontName = FontName,
            FontResources = FontResources,
            FontSize = FontSize,
            CharacterSpacing = CharacterSpacing,
            WordSpacing = WordSpacing,
            HorizontalScaling = HorizontalScaling,
            TextLeading = TextLeading,
            TextRise = TextRise,
            TextRenderingMode = TextRenderingMode,
            ActiveClips = new List<ClipPath>(ActiveClips),
        };
    }
}
