# PageRasterizer

**Class** in `Chuvadi.Pdf.Rendering` (Rendering)

Rasterizes a PDF page to a `PixelBuffer`.

```csharp
public sealed class PageRasterizer
```

## Remarks

`PageRasterizer` is the top-level public API for page rendering. Since v2.0.0, the pipeline is two-stage:  
 
-  `DisplayListBuilder` interprets the page's content stream and produces an immutable `PageDisplayList`. CTM and text matrices are baked into each op's geometry; the list is renderer-neutral.  
-  `PageRasterizer` walks the display list and paints each op into a `PixelBuffer`. The painter handles scale and Y-flip only; it does not interpret PDF operators.   

 Clipping recorded by the display list is honoured: each op's `RenderOp.Clips` are transformed to device space and applied as an intersection region by the `ScanlineRasterizer`. Axis-aligned rectangular clips (the common `re W n` case) take a fast path; arbitrary clip paths are evaluated per scanline against their fill rule. Image painting honours the same region per pixel.  

 PDF 32000-1:2008 §8 — Graphics model.

## Constructors

### `PageRasterizer(PdfObjectStore objects, RenderOptions? options = null)`

Initialises a `PageRasterizer` for a document's object store.

**Parameters**

- `objects` — The document's object store.
- `options` — Rendering options. Uses `RenderOptions.Default` when null. <exception cref="ArgumentNullException"> Thrown when `objects` is null. </exception>

## Methods

### `Rasterize`

```csharp
PixelBuffer Rasterize(PdfPage page)
```

Rasterizes a PDF page to a `PixelBuffer`.

**Parameters**

- `page` — The page to rasterize.

**Returns:** A pixel buffer in BGRA format containing the rendered page. <exception cref="ArgumentNullException"> Thrown when `page` is null. </exception>

### `RasterizeToPng`

```csharp
byte[] RasterizeToPng(PdfPage page)
```

Rasterizes a page and encodes the result as PNG bytes.

### `RasterizeToCmykTiff`

```csharp
byte[] RasterizeToCmykTiff(PdfPage page)
```

Rasterizes a page and encodes the result as a single-page CMYK TIFF (Photometric=5, 4 samples per pixel, PackBits compression).

**Remarks:** The pixel buffer is rendered in RGB and converted to CMYK using the standard subtractive formula. This is NOT a colour-managed transform; for press-accurate output, layer an ICC transform on the `CmykImage` returned by `RasterizeToCmyk`.

### `RasterizeToCmyk`

```csharp
CmykImage RasterizeToCmyk(PdfPage page)
```

Rasterizes a page and returns the result as a `CmykImage`.

**Remarks:** Uses the standard subtractive RGB→CMYK conversion. For press-accurate output, apply an ICC transform externally.

### `RenderRegion`

```csharp
PixelBuffer RenderRegion(PdfPage page, Rect region, double dpi)
```

Renders a rectangular sub-region of a page to a `PixelBuffer` at the given resolution. The region is given in page (PDF user) space; the result is sized `region.Width * dpi/72` by `region.Height * dpi/72` pixels, with the region's top-left corner at the buffer origin. Lighter than rasterizing the whole page and cropping.

**Parameters**

- `page` — The page to render.
- `region` — The sub-region in page space (PDF points).
- `dpi` — Output resolution in dots per inch. Must be positive.

**Returns:** The rendered region. Never null. <exception cref="ArgumentNullException">`page` is null.</exception> <exception cref="ArgumentOutOfRangeException"> `dpi` is not positive, or `region` has a non-positive dimension. </exception>

### `RenderClipped`

```csharp
PixelBuffer RenderClipped(PdfPage page, PathGeometry clipPageSpace, double dpi)
```

Renders the content inside a clip path to a `PixelBuffer` at the given resolution. The buffer is sized to the clip path's page-space bounding box (at `dpi`); pixels outside the clip path are left fully transparent. The clip path is given in page (PDF user) space.

**Parameters**

- `page` — The page to render.
- `clipPageSpace` — The clip path in page space.
- `dpi` — Output resolution in dots per inch. Must be positive.

**Returns:** The rendered, clipped region, sized to the clip's bounding box. Pixels outside the clip are transparent. Never null. <exception cref="ArgumentNullException"> `page` or `clipPageSpace` is null. </exception> <exception cref="ArgumentOutOfRangeException">`dpi` is not positive.</exception> <exception cref="ArgumentException">`clipPageSpace` has an empty bounding box.</exception>

### `RenderRegionToPng`

```csharp
byte[] RenderRegionToPng(PdfPage page, Rect region, double dpi)
```

Renders a sub-region (see `RenderRegion`) and encodes it as 24-bit RGB PNG bytes.

### `RenderClippedToPng`

```csharp
byte[] RenderClippedToPng(PdfPage page, PathGeometry clipPageSpace, double dpi)
```

Renders a clipped region (see `RenderClipped`) and encodes it as 32-bit RGBA PNG bytes, preserving the transparency outside the clip.

---

_Source: [`src/Chuvadi.Pdf.Rendering/PageRasterizer.cs`](../../../src/Chuvadi.Pdf.Rendering/PageRasterizer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
