# StrokePathOp

**Class** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

Strokes a path with the supplied `StrokeStyle`.

```csharp
public sealed class StrokePathOp : RenderOp
```

## Remarks

Emitted by the builder for the PDF operators S / s (stroke only) and for the stroke portion of B / B* / b / b* (fill-then-stroke operators emit a `FillPathOp` followed by a `StrokePathOp` sharing the same path data).  

 The path is in PDF user space with the current transformation matrix already applied. `StrokeStyle.Color` holds the stroke colour, while line width, cap, join, miter limit, and dash pattern are captured at emission time.

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

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/StrokePathOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/Raster/StrokePathOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
