# ImageOp

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Renders a raster image.

```csharp
public sealed class ImageOp : RenderOp
```

## Properties

### `Kind`

```csharp
override RenderOpKind Kind => RenderOpKind.Image
```

<inheritdoc />

### `PixelData`

```csharp
required byte[] PixelData
```

Pixel data (interpretation depends on `Format` and `ColorSpace`).

### `IndexSamples`

```csharp
byte[]? IndexSamples
```

For a palette (`/Indexed`) image whose samples were expanded to colour in `PixelData`: the original palette indices, one byte per pixel, row by row; otherwise null.

### `Format`

```csharp
required ImageFormat Format
```

Encoding format of `PixelData`.

### `Width`

```csharp
required int Width
```

Pixel width.

### `Height`

```csharp
required int Height
```

Pixel height.

### `BitsPerComponent`

```csharp
int BitsPerComponent
```

Bits per component (typically 8).

### `ColorSpace`

```csharp
PdfColorSpace ColorSpace
```

Color space of raw pixel data.

### `SoftMaskAlpha`

```csharp
byte[]? SoftMaskAlpha
```

Optional soft-mask alpha (one 8-bit sample per pixel) from the image's `/SMask`. When present, it is applied as the image's alpha channel.

### `SoftMaskWidth`

```csharp
int SoftMaskWidth
```

Width of `SoftMaskAlpha` in samples.

### `SoftMaskHeight`

```csharp
int SoftMaskHeight
```

Height of `SoftMaskAlpha` in samples.

### `Alpha`

```csharp
double Alpha
```

Constant image opacity (ExtGState /ca), 0..1. Default 1 (opaque). Composes with `SoftMaskAlpha` when both are present.

### `Transform`

```csharp
required AffineMatrix Transform
```

Transformation matrix placing the image. The unit-square at (0,0)-(1,1) is mapped to the image's destination rectangle.

### `Source`

```csharp
PdfStream? Source
```

The image XObject this op was drawn from (3.31), or null for an inline image.

### `Decode`

```csharp
double[]? Decode
```

The image's `/Decode` array (3.31), or null when absent.

### `IsStencilMask`

```csharp
bool IsStencilMask
```

True for a stencil mask (`/ImageMask true`, §8.9.6.2): the 1-bit samples paint `StencilColor` (3.31). `PixelData` then holds that colour with the mask as `SoftMaskAlpha`, and `Source` the original 1-bit samples.

### `StencilColor`

```csharp
PdfColor StencilColor
```

For a stencil mask, the fill colour it paints (3.31).

### `JpegColorTransform`

```csharp
JpegColorTransform JpegColorTransform
```

For a JPEG image, its colour transform from the Adobe APP14 marker (3.31).

### `JpegComponents`

```csharp
int JpegComponents
```

For a JPEG image, its number of colour components from the frame header (3.31); 0 otherwise.

### `IsCmykJpeg`

```csharp
bool IsCmykJpeg => Format == ImageFormat.Jpeg && JpegComponents == 4
```

True for a CMYK or YCCK JPEG (four components, 3.31).

### `IccProfileStream`

```csharp
PdfStream? IccProfileStream
```

The ICC profile stream of an `/ICCBased` colour space (3.31), or null.

## Methods

### `GetIccProfile`

```csharp
byte[]? GetIccProfile()
```

Returns the decoded ICC profile bytes, or null when there is none or it cannot be decoded (3.31).

**Returns:** The profile, or null.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
