# NestedDisplayListOp

**Class** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

Paints another `PageDisplayList` with a composing transform.

```csharp
public sealed class NestedDisplayListOp : RenderOp
```

## Remarks

Emitted by the builder for the PDF Do operator when the named XObject has Subtype /Form. Form XObjects are reusable content blocks (logos, page-number stamps, repeating headers) defined in their own coordinate space; `CtmComposition` maps that inner space into the outer user space.  

 Resolving Form XObjects to a sub-display-list happens once at build time. The painter recurses into `Inner` exactly the same way it walks the top-level page list — there is no separate Form XObject code path on the rendering side.

## Constructors

### `NestedDisplayListOp(PageDisplayList inner, Transform ctmComposition, IReadOnlyList<ClipPath>? clips = null)`

Initialises a `NestedDisplayListOp`.

**Parameters**

- `inner` — The sub-display-list to paint.
- `ctmComposition` — The CTM contribution of the Form XObject (the Matrix entry on the XObject dictionary composed with the CTM at the Do operator).
- `clips` — Clip paths active when this op was emitted. Null or empty means no clip. <exception cref="ArgumentNullException"> Thrown when `inner` is null. </exception>

## Properties

### `Inner`

```csharp
PageDisplayList Inner
```

Gets the sub-display-list.

### `CtmComposition`

```csharp
Transform CtmComposition
```

Gets the transform composing inner-space into the parent's user space.

### `Layered`

```csharp
bool Layered
```

Gets whether the sub-list is a transparency group drawn into a layer of its own and then composited as one object, with `LayerAlpha`, `RenderOp.BlendMode` and `RenderOp.SoftMask` (PDF 32000-1:2008 §11.4). When false (the default), the group's alpha, blend mode and soft mask were given to each object it paints. Since 3.35.0.

### `LayerAlpha`

```csharp
double LayerAlpha
```

Gets the constant alpha the layer is composited with (1 when not `Layered`). Since 3.35.0.

### `Knockout`

```csharp
bool Knockout
```

Gets whether the group is a knockout group (`/K true`): each object it paints replaces, rather than composites over, what earlier objects of the group painted (PDF 32000-1:2008 §11.4.6). A knockout group is always `Layered`. Since 3.35.0.

### `Isolated`

```csharp
bool Isolated
```

Gets whether the group is isolated (`/I true`): its layer starts transparent rather than from what is under it (PDF 32000-1:2008 §11.4.5). Meaningful only when `Layered`. Since 3.35.0.

---

_Source: [`src/Chuvadi.Pdf.Rendering.Raster/NestedDisplayListOp.cs`](../../../src/Chuvadi.Pdf.Rendering.Raster/NestedDisplayListOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
