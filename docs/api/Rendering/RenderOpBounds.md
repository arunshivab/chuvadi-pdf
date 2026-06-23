# RenderOpBounds

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Axis-aligned bounds accessors for `RenderOp` subtypes.

```csharp
public static class RenderOpBounds
```

## Methods

### `Bounds`

__static__

```csharp
static Rect Bounds(this PathOp op, double tolerance = DefaultTolerance)
```

The tight page-space bounds of a path's flattened geometry.

**Parameters**

- `op` — The path op.
- `tolerance` — Curve-flattening tolerance.

**Returns:** The bounding box.

### `Bounds`

__static__

```csharp
static Rect Bounds(this ClipOp op, double tolerance = DefaultTolerance)
```

The tight page-space bounds of a clip path's flattened geometry.

**Parameters**

- `op` — The clip op.
- `tolerance` — Curve-flattening tolerance.

**Returns:** The bounding box.

### `Bounds`

__static__

```csharp
static Rect Bounds(this ImageOp op)
```

The page-space bounds of an image: the AABB of the unit square mapped through `ImageOp.Transform`.

**Parameters**

- `op` — The image op.

**Returns:** The bounding box.

### `TryGetBounds`

__static__

```csharp
static Rect? TryGetBounds(this RenderOp op, double tolerance = DefaultTolerance)
```

The page-space bounds of any op that paints a bounded region (path, clip, or image), or null for ops that do not.

**Parameters**

- `op` — The op.
- `tolerance` — Curve-flattening tolerance for geometry ops.

**Returns:** The bounding box, or null.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOpBounds.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOpBounds.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
