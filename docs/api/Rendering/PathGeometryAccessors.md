# PathGeometryAccessors

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Query accessors over `PathGeometry`: flattening, bounds, signed area, and point containment.

```csharp
public static class PathGeometryAccessors
```

## Methods

### `Bounds`

__static__

```csharp
static Rect Bounds(this PathGeometry geometry, double tolerance = DefaultTolerance)
```

Computes the tight axis-aligned bounding box of the flattened geometry, or an empty rectangle at the origin when the path has no points.

**Parameters**

- `geometry` — The path to measure.
- `tolerance` — Curve-flattening tolerance.

**Returns:** The bounding box.

### `SignedArea`

__static__

```csharp
static double SignedArea(this PathGeometry geometry, double tolerance = DefaultTolerance)
```

Computes the signed area enclosed by the flattened subpaths (positive for counter-clockwise winding in a y-up frame). Each subpath is treated as closed.

**Parameters**

- `geometry` — The path to measure.
- `tolerance` — Curve-flattening tolerance.

**Returns:** The summed signed area.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/PathGeometryAccessors.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/PathGeometryAccessors.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
