# RenderOp

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Abstract base for all display-list operations.

```csharp
public abstract class RenderOp
```

## Properties

### `Kind`

```csharp
abstract RenderOpKind Kind
```

Discriminator for switch-pattern dispatch.

### `BlendMode`

```csharp
PdfBlendMode BlendMode
```

Blend mode for compositing this op against the backdrop (PDF §11.3.5).

### `SoftMask`

```csharp
SoftMaskInfo? SoftMask
```

Active soft mask (ExtGState /SMask) gating this op, or null.

### `Layers`

```csharp
IReadOnlyList<string> Layers
```

The optional-content-group (layer) names that this op belongs to, from the enclosing marked-content sequences (`/OC … BDC … EMC`), ordered outermost-first. Empty when the op is not inside any optional-content layer. Never null. PDF 32000-1:2008 §8.11.3.2.

### `Clips`

```csharp
IReadOnlyList<PathGeometry> Clips
```

The clipping paths in effect when this op was emitted, ordered outermost-first (draw order), each already in page space (the CTM is applied, matching `PathOp.Geometry` and `ClipOp.Geometry`). The effective clip region is the intersection of all paths in the list. Empty when the op is not clipped. Never null. PDF 32000-1:2008 §8.5.4.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
