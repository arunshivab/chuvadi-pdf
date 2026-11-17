# DecodedImage

**Class** in `Chuvadi.Pdf.Rendering` (Rendering)

The result of `ImageDecoding.Decode(ImageOp)`: the pixels the raster route paints for an image, or the reason it paints none.

```csharp
public sealed class DecodedImage
```

## Properties

### `Width`

```csharp
int Width
```

The width in pixels; 0 when the image is not drawn.

### `Height`

```csharp
int Height
```

The height in pixels; 0 when the image is not drawn.

### `Pixels`

```csharp
byte[]? Pixels
```

The pixels as 8-bit straight (not premultiplied) RGBA in sRGB, four bytes per pixel, top row first; null when the image is not drawn.

### `HasAlpha`

```csharp
bool HasAlpha
```

True when any pixel is less than opaque (after /SMask, /Mask and stencils).

### `CanPassThroughJpeg`

```csharp
bool CanPassThroughJpeg
```

True for a 1- or 3-component JPEG with no `/SMask`, no `/Mask` and a default or absent `/Decode`: the original JPEG bytes (`ImageOp.PixelData`) show the same image and can be kept.

### `SkipReason`

```csharp
string? SkipReason
```

Why the raster route does not draw the image (the text it reports), or null when `Pixels` is set.

---

_Source: [`src/Chuvadi.Pdf.Rendering/ImageDecoding.cs`](../../../src/Chuvadi.Pdf.Rendering/ImageDecoding.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
