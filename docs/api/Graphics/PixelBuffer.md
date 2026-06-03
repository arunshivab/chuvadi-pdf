# PixelBuffer

**Class** in `Chuvadi.Pdf.Graphics` (Graphics)

A packed BGRA (Blue, Green, Red, Alpha) pixel buffer. The rasterizer writes rendered pages into a `PixelBuffer`, which is then encoded to PNG or BMP by Chuvadi.Pdf.Images.

```csharp
public sealed class PixelBuffer
```

## Remarks

Pixel layout: 4 bytes per pixel in memory order (B, G, R, A). Row order: top-to-bottom (first byte = top-left pixel's B channel). This matches Windows BMP and common graphics conventions. Note: PDF coordinate space is bottom-left origin; the rasterizer flips Y when writing to the pixel buffer.

## Constructors

### `PixelBuffer(int width, int height)`

Initialises a new `PixelBuffer` with all pixels transparent black.

**Parameters**

- `width` — Width in pixels. Must be positive.
- `height` — Height in pixels. Must be positive.

## Properties

### `Width`

```csharp
int Width
```

Gets the width of the buffer in pixels.

### `Height`

```csharp
int Height
```

Gets the height of the buffer in pixels.

### `ByteCount`

```csharp
int ByteCount => _pixels.Length
```

Gets the total number of bytes (Width × Height × 4).

### `Stride`

```csharp
int Stride => Width * 4
```

Gets the row stride in bytes (Width × 4).

### `Pixels`

```csharp
ReadOnlySpan<byte> Pixels => _pixels
```

Gets a read-only span over the raw pixel bytes.

## Methods

### `SetPixel`

```csharp
void SetPixel(int x, int y, ColorF color)
```

Sets a pixel at (x, y) from a `ColorF` value. Out-of-range coordinates are silently ignored.

### `SetPixelBgra`

```csharp
void SetPixelBgra(int x, int y, byte b, byte g, byte r, byte a)
```

Sets a pixel at (x, y) from packed BGRA bytes. Out-of-range coordinates are silently ignored.

### `BlendPixel`

```csharp
void BlendPixel(int x, int y, ColorF color)
```

Blends a colour over the existing pixel using standard alpha compositing (Porter-Duff "over" operation) with gamma-correct (linear-light) mixing. PDF 32000-1:2008 -11.3 - Basic compositing formula.

### `BlendPixel`

```csharp
void BlendPixel(int x, int y, ColorF color, bool gammaCorrect)
```

Blends a colour over the existing pixel using standard alpha compositing (Porter-Duff "over" operation). When `gammaCorrect` is true the colour channels are mixed in linear light (sRGB decoded before and re-encoded after the blend), which gives anti-aliased edges their correct perceptual weight; when false the channels are mixed directly in sRGB space (the legacy behaviour). PDF 32000-1:2008 -11.3 - Basic compositing formula.

**Parameters**

- `x` — Destination pixel X coordinate.
- `y` — Destination pixel Y coordinate.
- `color` — Source colour to blend over the destination.
- `gammaCorrect` — Whether to mix channels in linear light.

### `Clear`

```csharp
void Clear(ColorF color)
```

Fills the entire buffer with the given colour.

### `ClearWhite`

```csharp
void ClearWhite()
```

Fills the entire buffer with opaque white.

### `GetRow`

```csharp
ReadOnlySpan<byte> GetRow(int y)
```

Gets a row of pixels as a byte span (for efficient encoding).

---

_Source: [`src/Chuvadi.Pdf.Graphics/PixelBuffer.cs`](../../../src/Chuvadi.Pdf.Graphics/PixelBuffer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
