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

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
