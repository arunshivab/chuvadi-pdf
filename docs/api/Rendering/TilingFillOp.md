# TilingFillOp

**Class** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

Fills a region with a tiling pattern described for repetition rather than tile by tile: emitted when a fill needs more than 4,096 tiles (a fine hatch over a whole page). The painter renders `Cell` once into a bitmap at device resolution and repeats it across the region, so the cost depends on the output size, not on the tile count. Fills of 4,096 tiles or fewer keep the exact per-tile `NestedDisplayListOp` path.

```csharp
public sealed class TilingFillOp : RenderOp
```

## Constructors

### `TilingFillOp(PageDisplayList cell, Transform patternMatrix, double xStep, double yStep, double cellOriginX, double cellOriginY, double cellWidth, double cellHeight, RectangleF region, IReadOnlyList<ClipPath>? clips)`

Initialises a tiling fill.

**Parameters**

- `cell` — The pattern cell's display list, in pattern space.
- `patternMatrix` — Maps pattern space to the op's user space.
- `xStep` — Horizontal repeat step, in pattern space (positive).
- `yStep` — Vertical repeat step, in pattern space (positive).
- `cellOriginX` — The cell's /BBox lower-left x.
- `cellOriginY` — The cell's /BBox lower-left y.
- `cellWidth` — The cell's /BBox width.
- `cellHeight` — The cell's /BBox height.
- `region` — Bounds of the filled region, in user space.
- `clips` — Clips active for the fill (including the filled path).

## Properties

### `Cell`

```csharp
PageDisplayList Cell
```

Gets the pattern cell's display list (pattern space).

### `PatternMatrix`

```csharp
Transform PatternMatrix
```

Gets the pattern-to-user-space transform.

### `XStep`

```csharp
double XStep
```

Gets the horizontal repeat step.

### `YStep`

```csharp
double YStep
```

Gets the vertical repeat step.

### `CellOriginX`

```csharp
double CellOriginX
```

Gets the cell origin x.

### `CellOriginY`

```csharp
double CellOriginY
```

Gets the cell origin y.

### `CellWidth`

```csharp
double CellWidth
```

Gets the cell width.

### `CellHeight`

```csharp
double CellHeight
```

Gets the cell height.

### `Region`

```csharp
RectangleF Region
```

Gets the user-space bounds of the filled region.

---

_Source: [`src/Chuvadi.Pdf.Rendering.Raster/TilingFillOp.cs`](../../../src/Chuvadi.Pdf.Rendering.Raster/TilingFillOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
