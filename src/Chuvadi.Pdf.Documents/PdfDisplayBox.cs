// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.11.2 — Page boundaries
// PHASE: 3.32.0 (D-221 P2, D-230 Q8) — which page box the renderers show

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Which page box the raster, SVG and WPF renderers show (3.32.0).
/// </summary>
public enum PdfDisplayBox
{
    /// <summary>
    /// The CropBox, intersected with the MediaBox (PDF 32000-1:2008
    /// §14.11.2): what a viewer shows and a printer prints. The default since
    /// 3.32.0. A page without a CropBox shows its MediaBox, as before.
    /// </summary>
    CropBox = 0,

    /// <summary>
    /// The whole MediaBox, as every release before 3.32.0 rendered it.
    /// </summary>
    MediaBox = 1,
}
