# ClipPath

**Struct** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

A clipping path applied to a single render operation.

```csharp
public readonly struct ClipPath
```

## Remarks

Chuvadi's display list represents clipping as per-operation data rather than as a paired Push/Pop on the renderer's state stack. Each `RenderOp` carries the list of clip paths active at the moment the op was emitted by the builder. A point is painted only when it lies inside every clip path in the list (intersection semantics).  

 This model has two advantages over a stack-of-pushes alternative: the display list cannot end up in an inconsistent state from malformed content streams, and consumers (rasterizer, SVG writer) handle clipping uniformly without tracking nested clip state across ops.  

 PDF 32000-1:2008 §8.5.4 — Clipping path operators (W, W*).

## Constructors

### `ClipPath(Path path, FillRule rule)`

Initialises a `ClipPath` with a path and a fill rule.

**Parameters**

- `path` — The clip path geometry in PDF user space.
- `rule` — The fill rule used to evaluate the clip region. <exception cref="ArgumentNullException"> Thrown when `path` is null. </exception>

## Properties

### `Path`

```csharp
Path Path
```

Gets the clip path geometry in PDF user space.

### `Rule`

```csharp
FillRule Rule
```

Gets the fill rule used to evaluate the clip region.

---

_Source: [`src/Chuvadi.Pdf.Rendering.Raster/ClipPath.cs`](../../../src/Chuvadi.Pdf.Rendering.Raster/ClipPath.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
