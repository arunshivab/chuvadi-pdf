# PerspectiveCorrection

**Class** in `Chuvadi.Pdf.Images` (Images)

Four-point perspective correction ("deskew"): maps a quadrilateral marked on a photo of a page — its four corners — onto an upright rectangle, the way a scanning app straightens a page shot at an angle.

```csharp
public static class PerspectiveCorrection
```

## Remarks

Corner coordinates are in source-image pixels with the origin at the top-left and Y pointing down; a pixel's centre is at (x + 0.5, y + 0.5), so the image's full extent runs from (0, 0) to (Width, Height). Corners may lie outside the image; samples falling outside are clamped to the nearest edge pixel.  

 Each output pixel is mapped back through the homography and sampled bilinearly on alpha-premultiplied values, so transparent regions do not bleed dark fringes. The result is a new BGRA frame; the source is not modified.

## Fields

### `MaxDimension`

```csharp
const int MaxDimension = 32768
```

The largest output width or height accepted.

---

_Source: [`src/Chuvadi.Pdf.Images/PerspectiveCorrection.cs`](../../../src/Chuvadi.Pdf.Images/PerspectiveCorrection.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
