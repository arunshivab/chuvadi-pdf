# Placement

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Builds the affine transforms most often needed when placing a page: scale a source box to fit a destination, centre it, or rotate it by an arbitrary angle. Each returns a `Transform` suitable for `PageComposer.PlacePage` or `PageStamper`; callers who need full control can always supply their own transform instead.

```csharp
public static class Placement
```

## Methods

### `RotateIntoBox`

__static__

```csharp
static Transform RotateIntoBox(double degrees, double width, double height)
```

Rotates a `width` × `height` box by `degrees` and shifts it so its rotated bounding box sits at the origin — ready to place on a sheet sized via `RotatedSize`.

### `RotateAboutCenter`

__static__

```csharp
static Transform RotateAboutCenter(double degrees, double width, double height)
```

Rotates a box by `degrees` about its own centre, keeping the centre fixed (useful for an in-place rotated stamp).

---

_Source: [`src/Chuvadi.Pdf.Operations/Placement.cs`](../../../src/Chuvadi.Pdf.Operations/Placement.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
