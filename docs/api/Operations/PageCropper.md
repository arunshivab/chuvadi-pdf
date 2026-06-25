# PageCropper

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Crops pages to a rectangle by setting their `/MediaBox` and `/CropBox` to the crop rectangle and wrapping the existing content in a hard clip (`q &lt;rect&gt; re W n &#8230; Q`) so nothing outside the rectangle is painted.

```csharp
public static class PageCropper
```

## Remarks

This is a lossless, visual crop: in-box content is preserved byte-for-byte and the page is resized to the crop rectangle, but the bytes of off-box content remain in the file (clipped from view, not removed). For a redaction-grade crop that physically removes off-box content, a future scrubbing mode is required.

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
