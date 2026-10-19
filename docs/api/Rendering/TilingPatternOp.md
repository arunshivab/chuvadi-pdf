# TilingPatternOp

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Paints a tiling pattern (PatternType 1): the pattern cell's content, built once as `Cell` in pattern-cell space, repeated across the filled region. The op is emitted inside a clip of the filled path, so the repetition is confined to the painted region. PDF 32000-1:2008 §8.7.3.

```csharp
public sealed class TilingPatternOp : RenderOp
```

## Remarks

Two forms: with up to 4,096 tiles, `Tiles` lists one cell-to-page transform per placed tile. Above that (3.21) the op is `IsRepeating`: `Tiles` is empty and the pattern is described by `PatternMatrix`, the steps, the cell origin and the region to cover, so a consumer repeats the cell itself (an SVG `&lt;pattern&gt;`, a bitmap tile) at a cost independent of the tile count.

## Constructors

### `TilingPatternOp(PageDisplayList cell, IReadOnlyList<AffineMatrix> tiles)`

Initialises a tiling-pattern op with explicit tile placements.

**Parameters**

- `cell` — The pattern cell's display list, in cell space.
- `tiles` — One cell-to-page transform per placed tile.

## Properties

### `Kind`

```csharp
override RenderOpKind Kind => RenderOpKind.TilingPattern
```

<inheritdoc />

### `Cell`

```csharp
PageDisplayList Cell
```

Gets the pattern cell's display list (cell space).

### `Tiles`

```csharp
IReadOnlyList<AffineMatrix> Tiles
```

Gets the cell-to-page transform of every placed tile (empty when `IsRepeating`).

### `IsRepeating`

```csharp
bool IsRepeating
```

Gets whether the pattern is described for repetition rather than by explicit tiles.

### `PatternMatrix`

```csharp
AffineMatrix PatternMatrix
```

Gets the pattern-to-page transform (repeating form).

### `XStep`

```csharp
double XStep
```

Gets the horizontal repeat step in pattern space (repeating form).

### `YStep`

```csharp
double YStep
```

Gets the vertical repeat step in pattern space (repeating form).

### `CellOriginX`

```csharp
double CellOriginX
```

Gets the cell origin x in pattern space (repeating form).

### `CellOriginY`

```csharp
double CellOriginY
```

Gets the cell origin y in pattern space (repeating form).

### `Region`

```csharp
Rect Region
```

Gets the page-space bounds of the filled region (repeating form).

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/TilingPatternOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/TilingPatternOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
