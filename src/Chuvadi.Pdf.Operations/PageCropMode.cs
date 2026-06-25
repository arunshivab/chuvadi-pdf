// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §14.11.2 (page boundaries); §8.5.4 (clipping)

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Selects how <see cref="PageCropper"/> confines a page to its crop rectangle.
/// </summary>
public enum PageCropMode
{
    /// <summary>
    /// Lossless visual crop: the page boxes are reset and existing content is
    /// wrapped in a hard clip. In-box content is preserved byte-for-byte; off-box
    /// bytes remain in the file but are clipped from view (not removed).
    /// </summary>
    ClipOnly = 0,

    /// <summary>
    /// Redaction-grade crop: off-box vector geometry is physically removed,
    /// boundary-crossing geometry is clipped to its in-box portion, off-box text
    /// glyphs are dropped, and boundary-crossing images are cropped to the in-box
    /// region. In-box content is preserved. Boundary-crossing geometry is
    /// flattened where it must be clipped.
    /// </summary>
    Scrub = 1,
}
