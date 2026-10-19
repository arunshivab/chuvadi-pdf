// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.3 — Tiling patterns
// PHASE: 3.21 — tiling patterns too fine to place tile by tile.

using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering.Raster;

/// <summary>
/// Fills a region with a tiling pattern described for repetition rather than
/// tile by tile: emitted when a fill needs more than 4,096 tiles (a fine hatch
/// over a whole page). The painter renders <see cref="Cell"/> once into a
/// bitmap at device resolution and repeats it across the region, so the cost
/// depends on the output size, not on the tile count. Fills of 4,096 tiles or
/// fewer keep the exact per-tile <see cref="NestedDisplayListOp"/> path.
/// </summary>
public sealed class TilingFillOp : RenderOp
{
    /// <summary>Initialises a tiling fill.</summary>
    /// <param name="cell">The pattern cell's display list, in pattern space.</param>
    /// <param name="patternMatrix">Maps pattern space to the op's user space.</param>
    /// <param name="xStep">Horizontal repeat step, in pattern space (positive).</param>
    /// <param name="yStep">Vertical repeat step, in pattern space (positive).</param>
    /// <param name="cellOriginX">The cell's /BBox lower-left x.</param>
    /// <param name="cellOriginY">The cell's /BBox lower-left y.</param>
    /// <param name="cellWidth">The cell's /BBox width.</param>
    /// <param name="cellHeight">The cell's /BBox height.</param>
    /// <param name="region">Bounds of the filled region, in user space.</param>
    /// <param name="clips">Clips active for the fill (including the filled path).</param>
    public TilingFillOp(
        PageDisplayList cell, Transform patternMatrix, double xStep, double yStep,
        double cellOriginX, double cellOriginY, double cellWidth, double cellHeight,
        RectangleF region, IReadOnlyList<ClipPath>? clips)
        : base(clips)
    {
        Cell = cell;
        PatternMatrix = patternMatrix;
        XStep = xStep;
        YStep = yStep;
        CellOriginX = cellOriginX;
        CellOriginY = cellOriginY;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        Region = region;
    }

    /// <summary>Gets the pattern cell's display list (pattern space).</summary>
    public PageDisplayList Cell { get; }

    /// <summary>Gets the pattern-to-user-space transform.</summary>
    public Transform PatternMatrix { get; }

    /// <summary>Gets the horizontal repeat step.</summary>
    public double XStep { get; }

    /// <summary>Gets the vertical repeat step.</summary>
    public double YStep { get; }

    /// <summary>Gets the cell origin x.</summary>
    public double CellOriginX { get; }

    /// <summary>Gets the cell origin y.</summary>
    public double CellOriginY { get; }

    /// <summary>Gets the cell width.</summary>
    public double CellWidth { get; }

    /// <summary>Gets the cell height.</summary>
    public double CellHeight { get; }

    /// <summary>Gets the user-space bounds of the filled region.</summary>
    public RectangleF Region { get; }
}
