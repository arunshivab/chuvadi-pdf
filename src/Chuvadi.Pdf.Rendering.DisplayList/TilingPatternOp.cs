// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.3 (tiling patterns)
// PHASE: Display list — tiling-pattern fills for the SVG pipeline.

using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Paints a tiling pattern (PatternType 1): the pattern cell's content,
/// built once as <see cref="Cell"/> in pattern-cell space, repeated across the
/// filled region. The op is emitted inside a clip of the filled path, so the
/// repetition is confined to the painted region. PDF 32000-1:2008 §8.7.3.
/// </summary>
/// <remarks>
/// Two forms: with up to 4,096 tiles, <see cref="Tiles"/> lists one
/// cell-to-page transform per placed tile. Above that (3.21) the op is
/// <see cref="IsRepeating"/>: <see cref="Tiles"/> is empty and the pattern is
/// described by <see cref="PatternMatrix"/>, the steps, the cell origin and
/// the region to cover, so a consumer repeats the cell itself (an SVG
/// <c>&lt;pattern&gt;</c>, a bitmap tile) at a cost independent of the tile
/// count.
/// </remarks>
public sealed class TilingPatternOp : RenderOp
{
    /// <summary>Initialises a tiling-pattern op with explicit tile placements.</summary>
    /// <param name="cell">The pattern cell's display list, in cell space.</param>
    /// <param name="tiles">One cell-to-page transform per placed tile.</param>
    public TilingPatternOp(PageDisplayList cell, IReadOnlyList<AffineMatrix> tiles)
    {
        Cell = cell;
        Tiles = tiles;
        PatternMatrix = AffineMatrix.Identity;
    }

    /// <summary>Initialises a repeating tiling-pattern op (no explicit tile list).</summary>
    /// <param name="cell">The pattern cell's display list, in cell space.</param>
    /// <param name="patternMatrix">Maps pattern space to page space.</param>
    /// <param name="xStep">Horizontal repeat step, in pattern space.</param>
    /// <param name="yStep">Vertical repeat step, in pattern space.</param>
    /// <param name="cellOriginX">The cell's /BBox lower-left x, in pattern space.</param>
    /// <param name="cellOriginY">The cell's /BBox lower-left y, in pattern space.</param>
    /// <param name="region">The page-space bounds of the filled region.</param>
    public TilingPatternOp(
        PageDisplayList cell, AffineMatrix patternMatrix, double xStep, double yStep,
        double cellOriginX, double cellOriginY, Rect region)
    {
        Cell = cell;
        Tiles = System.Array.Empty<AffineMatrix>();
        IsRepeating = true;
        PatternMatrix = patternMatrix;
        XStep = xStep;
        YStep = yStep;
        CellOriginX = cellOriginX;
        CellOriginY = cellOriginY;
        Region = region;
    }

    /// <inheritdoc />
    public override RenderOpKind Kind => RenderOpKind.TilingPattern;

    /// <summary>Gets the pattern cell's display list (cell space).</summary>
    public PageDisplayList Cell { get; }

    /// <summary>Gets the cell-to-page transform of every placed tile (empty when <see cref="IsRepeating"/>).</summary>
    public IReadOnlyList<AffineMatrix> Tiles { get; }

    /// <summary>Gets whether the pattern is described for repetition rather than by explicit tiles.</summary>
    public bool IsRepeating { get; }

    /// <summary>Gets the pattern-to-page transform (repeating form).</summary>
    public AffineMatrix PatternMatrix { get; }

    /// <summary>Gets the horizontal repeat step in pattern space (repeating form).</summary>
    public double XStep { get; }

    /// <summary>Gets the vertical repeat step in pattern space (repeating form).</summary>
    public double YStep { get; }

    /// <summary>Gets the cell origin x in pattern space (repeating form).</summary>
    public double CellOriginX { get; }

    /// <summary>Gets the cell origin y in pattern space (repeating form).</summary>
    public double CellOriginY { get; }

    /// <summary>Gets the page-space bounds of the filled region (repeating form).</summary>
    public Rect Region { get; }
}
