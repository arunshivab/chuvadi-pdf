# PdfRenderExtensions

**Class** in `Chuvadi.Pdf.Reader` (Reader)

One-call rendering of PDF pages to common output formats. These extensions are the simplest correct way to turn an open `PdfDocument` into SVG, PNG, JPEG, BMP, or TIFF — open a document, call one method, get the result. They wrap the full rendering pipeline (display list → renderer / rasterizer → encoder), so an application never has to assemble it by hand.

```csharp
public static class PdfRenderExtensions
```

## Remarks

Vector output (`RenderPageToSvg(PdfDocument, int, SvgExportOptions)`) preserves selectable text and embedded fonts. Raster output rasterizes the page at a chosen DPI and encodes the pixels; 150 DPI is a good screen default, 300 DPI is print quality.

## Methods

### `RenderPageToSvg`

__static__

```csharp
static string RenderPageToSvg(this PdfDocument document, int pageIndex, SvgExportOptions? options = null)
```

Renders one page to a self-contained SVG string.

**Parameters**

- `document` — The open PDF document.
- `pageIndex` — Zero-based page index.
- `options` — Optional SVG export options; defaults are used when null.

**Returns:** The page as an SVG document string. <exception cref="ArgumentNullException">When `document` is null.</exception> <exception cref="ArgumentOutOfRangeException">When `pageIndex` is out of range.</exception>

### `RenderPageToSvgBytes`

__static__

```csharp
static byte[] RenderPageToSvgBytes(this PdfDocument document, int pageIndex, SvgExportOptions? options = null)
```

Renders one page to SVG encoded as UTF-8 bytes.

**Parameters**

- `document` — The open PDF document.
- `pageIndex` — Zero-based page index.
- `options` — Optional SVG export options; defaults are used when null.

**Returns:** The page as UTF-8 encoded SVG bytes. <exception cref="ArgumentNullException">When `document` is null.</exception> <exception cref="ArgumentOutOfRangeException">When `pageIndex` is out of range.</exception>

### `RenderPageToPng`

__static__

```csharp
static byte[] RenderPageToPng(this PdfDocument document, int pageIndex, double dpi = 150)
```

Renders one page to PNG bytes at the given DPI.

**Parameters**

- `document` — The open PDF document.
- `pageIndex` — Zero-based page index.
- `dpi` — Rasterization resolution in dots per inch. Default: 150.

**Returns:** The page encoded as a PNG image. <exception cref="ArgumentNullException">When `document` is null.</exception> <exception cref="ArgumentOutOfRangeException">When `pageIndex` is out of range.</exception>

### `RenderPageToPng`

__static__

```csharp
static void RenderPageToPng(this PdfDocument document, int pageIndex, Stream output, double dpi = 150)
```

Renders one page to PNG, writing to `output`.

**Parameters**

- `document` — The open PDF document.
- `pageIndex` — Zero-based page index.
- `output` — Destination stream.
- `dpi` — Rasterization resolution in dots per inch. Default: 150. <exception cref="ArgumentNullException">When `document` or `output` is null.</exception> <exception cref="ArgumentOutOfRangeException">When `pageIndex` is out of range.</exception>

### `RenderPageToJpeg`

__static__

```csharp
static byte[] RenderPageToJpeg(this PdfDocument document, int pageIndex, double dpi = 150, int quality = 85)
```

Renders one page to JPEG bytes at the given DPI and quality.

**Parameters**

- `document` — The open PDF document.
- `pageIndex` — Zero-based page index.
- `dpi` — Rasterization resolution in dots per inch. Default: 150.
- `quality` — JPEG quality, 1–100. Default: 85.

**Returns:** The page encoded as a JPEG image. <exception cref="ArgumentNullException">When `document` is null.</exception> <exception cref="ArgumentOutOfRangeException">When `pageIndex` is out of range.</exception>

### `RenderPageToJpeg`

__static__

```csharp
static void RenderPageToJpeg(this PdfDocument document, int pageIndex, Stream output, double dpi = 150, int quality = 85)
```

Renders one page to JPEG, writing to `output`.

**Parameters**

- `document` — The open PDF document.
- `pageIndex` — Zero-based page index.
- `output` — Destination stream.
- `dpi` — Rasterization resolution in dots per inch. Default: 150.
- `quality` — JPEG quality, 1–100. Default: 85. <exception cref="ArgumentNullException">When `document` or `output` is null.</exception> <exception cref="ArgumentOutOfRangeException">When `pageIndex` is out of range.</exception>

### `RenderPageToBmp`

__static__

```csharp
static byte[] RenderPageToBmp(this PdfDocument document, int pageIndex, double dpi = 150)
```

Renders one page to BMP bytes at the given DPI.

**Parameters**

- `document` — The open PDF document.
- `pageIndex` — Zero-based page index.
- `dpi` — Rasterization resolution in dots per inch. Default: 150.

**Returns:** The page encoded as a BMP image. <exception cref="ArgumentNullException">When `document` is null.</exception> <exception cref="ArgumentOutOfRangeException">When `pageIndex` is out of range.</exception>

### `RenderPageToBmp`

__static__

```csharp
static void RenderPageToBmp(this PdfDocument document, int pageIndex, Stream output, double dpi = 150)
```

Renders one page to BMP, writing to `output`.

**Parameters**

- `document` — The open PDF document.
- `pageIndex` — Zero-based page index.
- `output` — Destination stream.
- `dpi` — Rasterization resolution in dots per inch. Default: 150. <exception cref="ArgumentNullException">When `document` or `output` is null.</exception> <exception cref="ArgumentOutOfRangeException">When `pageIndex` is out of range.</exception>

### `RenderPageToTiff`

__static__

```csharp
static byte[] RenderPageToTiff(this PdfDocument document, int pageIndex, double dpi = 150)
```

Renders one page to a single-page TIFF at the given DPI.

**Parameters**

- `document` — The open PDF document.
- `pageIndex` — Zero-based page index.
- `dpi` — Rasterization resolution in dots per inch. Default: 150.

**Returns:** The page encoded as a TIFF image. <exception cref="ArgumentNullException">When `document` is null.</exception> <exception cref="ArgumentOutOfRangeException">When `pageIndex` is out of range.</exception>

### `RenderPageToTiff`

__static__

```csharp
static void RenderPageToTiff(this PdfDocument document, int pageIndex, Stream output, double dpi = 150)
```

Renders one page to TIFF, writing to `output`.

**Parameters**

- `document` — The open PDF document.
- `pageIndex` — Zero-based page index.
- `output` — Destination stream.
- `dpi` — Rasterization resolution in dots per inch. Default: 150. <exception cref="ArgumentNullException">When `document` or `output` is null.</exception> <exception cref="ArgumentOutOfRangeException">When `pageIndex` is out of range.</exception>

### `RenderToTiff`

__static__

```csharp
static byte[] RenderToTiff(this PdfDocument document, double dpi = 150)
```

Renders every page to a single multi-page TIFF at the given DPI.

**Parameters**

- `document` — The open PDF document.
- `dpi` — Rasterization resolution in dots per inch. Default: 150.

**Returns:** A multi-page TIFF containing one frame per page. <exception cref="ArgumentNullException">When `document` is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Reader/PdfRenderExtensions.cs`](../../../src/Chuvadi.Pdf.Reader/PdfRenderExtensions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
