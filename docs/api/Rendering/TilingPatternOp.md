# TilingPatternOp

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Paints a tiling pattern (PatternType 1): the pattern cell's content, built once as `Cell` in pattern-cell space, is placed once per entry of `Tiles` (each mapping cell space to page space). The op is emitted inside a clip of the filled path, so the tiles are confined to the painted region. PDF 32000-1:2008 §8.7.3.

```csharp
public sealed class TilingPatternOp : RenderOp
```

## Constructors

### `TilingPatternOp(PageDisplayList cell, IReadOnlyList<AffineMatrix> tiles)`

Initialises a tiling-pattern op.

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

Gets the cell-to-page transform of every placed tile.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/TilingPatternOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/TilingPatternOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
