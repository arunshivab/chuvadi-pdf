// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.7.3 (tiling patterns)
// PHASE: Display list — tiling-pattern fills for the SVG pipeline.

using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Paints a tiling pattern (PatternType 1): the pattern cell's content,
/// built once as <see cref="Cell"/> in pattern-cell space, is placed once per
/// entry of <see cref="Tiles"/> (each mapping cell space to page space). The
/// op is emitted inside a clip of the filled path, so the tiles are confined
/// to the painted region. PDF 32000-1:2008 §8.7.3.
/// </summary>
public sealed class TilingPatternOp : RenderOp
{
    /// <summary>Initialises a tiling-pattern op.</summary>
    /// <param name="cell">The pattern cell's display list, in cell space.</param>
    /// <param name="tiles">One cell-to-page transform per placed tile.</param>
    public TilingPatternOp(PageDisplayList cell, IReadOnlyList<AffineMatrix> tiles)
    {
        Cell = cell;
        Tiles = tiles;
    }

    /// <inheritdoc />
    public override RenderOpKind Kind => RenderOpKind.TilingPattern;

    /// <summary>Gets the pattern cell's display list (cell space).</summary>
    public PageDisplayList Cell { get; }

    /// <summary>Gets the cell-to-page transform of every placed tile.</summary>
    public IReadOnlyList<AffineMatrix> Tiles { get; }
}
