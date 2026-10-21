# FillPathOp

**Class** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

Fills a path with a flat colour, applying the configured fill rule.

```csharp
public sealed class FillPathOp : RenderOp
```

## Remarks

Emitted by the builder for the PDF path-painting operators f / F / f* (fill only) and the fill portion of B / B* / b / b* (fill-then-stroke operators emit a `FillPathOp` followed by a `StrokePathOp` sharing the same path data).  

 The path is in PDF user space with the current transformation matrix already applied.

## Constructors

### `FillPathOp(Path path, ColorF color, FillRule rule, IReadOnlyList<ClipPath>? clips = null)`

Initialises a `FillPathOp`.

**Parameters**

- `path` — The path to fill, with CTM already applied.
- `color` — The fill colour.
- `rule` — The fill rule (non-zero winding or even-odd).
- `clips` — Clip paths active when this op was emitted. Null or empty means no clip. <exception cref="ArgumentNullException"> Thrown when `path` is null. </exception>

## Properties

### `Path`

```csharp
Path Path
```

Gets the path to fill.

### `Color`

```csharp
ColorF Color
```

Gets the fill colour.

### `Rule`

```csharp
FillRule Rule
```

Gets the fill rule.

---

_Source: [`src/Chuvadi.Pdf.Rendering.Raster/FillPathOp.cs`](../../../src/Chuvadi.Pdf.Rendering.Raster/FillPathOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
