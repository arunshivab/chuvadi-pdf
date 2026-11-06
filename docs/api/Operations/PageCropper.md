# PageCropper

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Crops pages to a rectangle by setting their `/MediaBox` and `/CropBox` to the crop rectangle and wrapping the existing content in a hard clip (`q &lt;rect&gt; re W n &#8230; Q`) so nothing outside the rectangle is painted.

```csharp
public static class PageCropper
```

## Remarks

Each `PageCrop` chooses its mode. `PageCropMode.ClipOnly` (the default) is a lossless, visual crop: in-box content is preserved byte-for-byte, but the bytes of off-box content remain in the file (clipped from view, not removed). `PageCropMode.Scrub` is the redaction-grade crop: off-box text, images and paths are removed from the content, and paths that cross the edge are cut at it. Use Scrub whenever off-box content must not be recoverable.  

 `PageCrop.CropBox` is in the page's default user space: PDF points, origin at the bottom left, the same space as the page's `/MediaBox` (which need not start at 0,0), before the page's `/Rotate` is applied. Coordinates taken from a rendered or rotated view (top-left origin, pixels, rotation applied) must be converted back first, or a different region is cropped.  

 A cropped page's `/MediaBox` starts at the crop rectangle's corner, not at 0,0. Chuvadi's raster and SVG output draw such pages correctly from 3.31.2; earlier versions drew them shifted, often blank.

## Methods

### `Crop`

__static__

```csharp
static void Crop(Stream output, PdfDocument document, IReadOnlyList<PageCrop> crops)
```

Crops the requested pages of `document` and writes the result to `output`.

**Parameters**

- `output` — The stream the cropped document is written to.
- `document` — The source document.
- `crops` — The pages to crop and the rectangle each is confined to. <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Operations/PageCropper.cs`](../../../src/Chuvadi.Pdf.Operations/PageCropper.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
