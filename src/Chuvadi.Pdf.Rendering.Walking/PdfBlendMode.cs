// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — moved from Chuvadi.Pdf.Rendering.DisplayList (RenderOp.cs) to
//        Walking, so the raster list no longer depends on the structured list
//        (D-58 item b, D-143). The namespace is kept and the old assembly
//        forwards the type, so callers and compiled code do not change.

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>PDF blend modes (§11.3.5).</summary>
public enum PdfBlendMode
{
    /// <summary>Normal (default).</summary>
    Normal = 0,
    /// <summary>Multiply.</summary>
    Multiply = 1,
    /// <summary>Screen.</summary>
    Screen = 2,
    /// <summary>Overlay.</summary>
    Overlay = 3,
    /// <summary>Darken.</summary>
    Darken = 4,
    /// <summary>Lighten.</summary>
    Lighten = 5,
    /// <summary>Color dodge.</summary>
    ColorDodge = 6,
    /// <summary>Color burn.</summary>
    ColorBurn = 7,
    /// <summary>Hard light.</summary>
    HardLight = 8,
    /// <summary>Soft light.</summary>
    SoftLight = 9,
    /// <summary>Difference.</summary>
    Difference = 10,
    /// <summary>Exclusion.</summary>
    Exclusion = 11,
}
