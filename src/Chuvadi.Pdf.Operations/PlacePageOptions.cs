// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.4 (clipping), §8.10.1 (form XObject BBox)
// PHASE: Page composition — per-placement crop/clip options.

using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Optional per-placement controls for <see cref="PageComposer.PlacePage(Chuvadi.Pdf.Documents.PdfDocument, int, Transform, PlacePageOptions)"/>.
/// </summary>
/// <remarks>
/// Both rectangles are in PDF user space (points, bottom-left origin) and are
/// independent: <see cref="SourceClip"/> selects the region of the source page
/// to import; <see cref="DestinationClip"/> confines the placed result on the
/// target sheet. Either or both may be left <see langword="null"/>.
/// </remarks>
public sealed class PlacePageOptions
{
    /// <summary>
    /// Gets or sets the clip rectangle applied on the target sheet, in target
    /// (destination) user space. When set, the placed page is hard-clipped to
    /// this rectangle (a <c>re W n</c> clip outside the placement transform), so
    /// a page placed into an N-up cell cannot bleed into neighbouring cells.
    /// <see langword="null"/> places without a destination clip.
    /// </summary>
    public RectangleF? DestinationClip { get; set; }

    /// <summary>
    /// Gets or sets the crop rectangle applied to the source page, in source
    /// user space. When set, only this region of the source is imported: the
    /// placed form XObject's <c>BBox</c> is set to this rectangle (rather than
    /// the source crop box), so content outside it is clipped at import time.
    /// <see langword="null"/> imports the full source crop box.
    /// </summary>
    public RectangleF? SourceClip { get; set; }
}
