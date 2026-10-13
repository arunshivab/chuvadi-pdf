# RenderOp

**Class** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

Abstract base for all operations in a `PageDisplayList`.

```csharp
public abstract class RenderOp
```

## Remarks

Each `RenderOp` describes one painting action in PDF user space (Y up, origin at the bottom-left of the MediaBox). The CTM in effect at the moment the op was emitted has already been applied to the op's geometry — consumers do not need to track a CTM stack.  

 Clipping is also pre-baked: `Clips` contains the list of clip paths active when this op was emitted. Empty when no clip is in effect (shares a single empty-array sentinel).  

 Subclasses are sealed; the hierarchy is closed.

## Properties

### `Clips`

```csharp
IReadOnlyList<ClipPath> Clips
```

Gets the clip paths active when this op was emitted. Empty when no clip is in effect.

### `BlendMode`

```csharp
PdfBlendMode BlendMode
```

Gets the separable blend mode for compositing this op against the backdrop (PDF §11.3.5). Normal is source-over.

### `SoftMask`

```csharp
RasterSoftMaskInfo? SoftMask
```

Gets the active soft mask (ExtGState `/SMask`) gating this op, or null when no soft mask is in effect.

---

_Source: [`src/Chuvadi.Pdf.Rendering.Raster/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.Raster/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
