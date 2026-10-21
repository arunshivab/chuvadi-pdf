# StrokePathOp

**Class** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

Strokes a path with the supplied `StrokeStyle`.

```csharp
public sealed class StrokePathOp : RenderOp
```

## Remarks

Emitted by the builder for the PDF operators S / s (stroke only) and for the stroke portion of B / B* / b / b* (fill-then-stroke operators emit a `FillPathOp` followed by a `StrokePathOp` sharing the same path data).  

 The path is in PDF user space with the current transformation matrix already applied. `StrokeStyle.Color` holds the stroke colour, while line width, cap, join, miter limit, and dash pattern are captured at emission time.

## Constructors

### `StrokePathOp(Path path, StrokeStyle style, IReadOnlyList<ClipPath>? clips = null)`

Initialises a `StrokePathOp`.

**Parameters**

- `path` — The path to stroke, with CTM already applied.
- `style` — The stroke parameters including colour, width, cap, join, miter, dash.
- `clips` — Clip paths active when this op was emitted. Null or empty means no clip. <exception cref="ArgumentNullException"> Thrown when `path` or `style` is null. </exception>

## Properties

### `Path`

```csharp
Path Path
```

Gets the path to stroke.

### `Style`

```csharp
StrokeStyle Style
```

Gets the stroke parameters (colour, width, cap, join, miter limit, dash).

---

_Source: [`src/Chuvadi.Pdf.Rendering.Raster/StrokePathOp.cs`](../../../src/Chuvadi.Pdf.Rendering.Raster/StrokePathOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
