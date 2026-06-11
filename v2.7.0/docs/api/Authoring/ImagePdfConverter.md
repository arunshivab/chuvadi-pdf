# ImagePdfConverter

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Converts images (JPEG, PNG, TIFF, BMP) into PDF documents — one page per image (and, optionally, one page per TIFF frame).

```csharp
public static class ImagePdfConverter
```

## Remarks

The converter is a thin layer over `PdfDocumentBuilder`: each image becomes a page whose size and placement follow `ImagePdfOptions`. Baseline JPEG and 8-bit truecolour PNG embed without recompression; other formats are decoded by the `Chuvadi.Pdf.Images` codecs and embedded as Flate-compressed samples. Alpha channels are preserved via PDF soft masks.

## Methods

### `Convert`

__static__

```csharp
static byte[] Convert(byte[] image, ImagePdfOptions? options = null)
```

Converts a single image to a single-page PDF (or one page per TIFF frame).

**Parameters**

- `image` — The encoded image bytes (JPEG, PNG, TIFF, or BMP).
- `options` — Conversion options; null uses `ImagePdfOptions.Default`.

**Returns:** The PDF file bytes. <exception cref="ArgumentNullException">`image` is null.</exception> <exception cref="ArgumentException">The bytes are not a recognised image format.</exception>

### `Convert`

__static__

```csharp
static byte[] Convert(IReadOnlyList<byte[]> images, ImagePdfOptions? options = null)
```

Converts several images to a multi-page PDF, one page per image.

**Parameters**

- `images` — The encoded images, in page order.
- `options` — Conversion options; null uses `ImagePdfOptions.Default`.

**Returns:** The PDF file bytes. <exception cref="ArgumentNullException">`images` is null.</exception> <exception cref="ArgumentException">No images were supplied, or a format was not recognised.</exception>

### `Convert`

__static__

```csharp
static void Convert(byte[] image, Stream output, ImagePdfOptions? options = null)
```

Converts a single image and writes the PDF to a stream.

---

_Source: [`src/Chuvadi.Pdf.Authoring/ImagePdfConverter.cs`](../../../src/Chuvadi.Pdf.Authoring/ImagePdfConverter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
