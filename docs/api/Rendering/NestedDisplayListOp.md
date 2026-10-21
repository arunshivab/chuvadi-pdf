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

---

_Source: [`src/Chuvadi.Pdf.Rendering.Raster/NestedDisplayListOp.cs`](../../../src/Chuvadi.Pdf.Rendering.Raster/NestedDisplayListOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
