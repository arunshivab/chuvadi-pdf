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

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
