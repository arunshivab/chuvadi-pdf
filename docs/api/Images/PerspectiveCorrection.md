# PerspectiveCorrection

**Class** in `Chuvadi.Pdf.Images` (Images)

Four-point perspective correction ("deskew"): maps a quadrilateral marked on a photo of a page — its four corners — onto an upright rectangle, the way a scanning app straightens a page shot at an angle.

```csharp
public static class PerspectiveCorrection
```

## Remarks

Corner coordinates are in source-image pixels with the origin at the top-left and Y pointing down; a pixel's centre is at (x + 0.5, y + 0.5), so the image's full extent runs from (0, 0) to (Width, Height). Corners may lie outside the image; samples falling outside are clamped to the nearest edge pixel.  

 Each output pixel is mapped back through the homography and sampled bilinearly on alpha-premultiplied values, so transparent regions do not bleed dark fringes. The result is a new BGRA frame; the source is not modified.

## Methods

### `Correct`

__static__

```csharp
static ImageFrame Correct(ImageFrame source, PointF topLeft, PointF topRight, PointF bottomRight, PointF bottomLeft)
```

Straightens the quadrilateral to a rectangle sized from its edges: the width is the longer of the top and bottom edges, the height the longer of the left and right edges (each rounded, at least one pixel).

**Parameters**

- `source` — The source image.
- `topLeft` — The quadrilateral's top-left corner.
- `topRight` — The top-right corner.
- `bottomRight` — The bottom-right corner.
- `bottomLeft` — The bottom-left corner.

**Returns:** The corrected image. <exception cref="ArgumentNullException">Thrown when `source` is null.</exception> <exception cref="ArgumentException">Thrown when the corners do not form a convex quadrilateral.</exception>

### `Correct`

__static__

```csharp
static ImageFrame Correct(ImageFrame source, PointF topLeft, PointF topRight, PointF bottomRight, PointF bottomLeft, int outputWidth, int outputHeight)
```

Straightens the quadrilateral to a rectangle of the given size.

**Parameters**

- `source` — The source image.
- `topLeft` — The quadrilateral's top-left corner.
- `topRight` — The top-right corner.
- `bottomRight` — The bottom-right corner.
- `bottomLeft` — The bottom-left corner.
- `outputWidth` — Output width in pixels (1 to `MaxDimension`).
- `outputHeight` — Output height in pixels (1 to `MaxDimension`).

**Returns:** The corrected image. <exception cref="ArgumentNullException">Thrown when `source` is null.</exception> <exception cref="ArgumentOutOfRangeException">Thrown when an output dimension is out of range.</exception> <exception cref="ArgumentException">Thrown when the corners do not form a convex quadrilateral.</exception>

## Fields

### `MaxDimension`

```csharp
const int MaxDimension = 32768
```

The largest output width or height accepted.

---

_Source: [`src/Chuvadi.Pdf.Images/PerspectiveCorrection.cs`](../../../src/Chuvadi.Pdf.Images/PerspectiveCorrection.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
