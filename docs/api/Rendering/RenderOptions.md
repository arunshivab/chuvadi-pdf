# RenderOptions

**Class** in `Chuvadi.Pdf.Rendering` (Rendering)

Options that control how a PDF page is rasterized.

```csharp
public sealed class RenderOptions
```

## Constructors

### `RenderOptions()`

Initialises `RenderOptions` with default values.

## Properties

### `Default`

__static__

```csharp
static RenderOptions Default
```

Default options: 150 DPI, opaque white background.

### `Dpi`

```csharp
double Dpi
```

Gets or initialises the output resolution in dots per inch. Higher values produce larger, sharper images. Typical values: 72 (screen), 96 (Windows default), 150, 300 (print). Default: 150.

### `Background`

```csharp
ColorF Background
```

Gets or initialises the background colour painted before page content. Default: opaque white.

### `FlatnessTolerance`

```csharp
double FlatnessTolerance
```

Gets or initialises the flatness tolerance for Bezier curve flattening in device pixels. Smaller = smoother curves, more segments. Default: 0.25 pixels.

### `SuperSample`

```csharp
int SuperSample
```

Computes the scale factor from PDF points to device pixels for this DPI. Gets or initialises the supersampling factor for anti-aliasing. The page is rendered at this multiple of the target resolution and box-filtered down, smoothing glyph and path edges. 1 disables supersampling (pixel-identical to the single-sample rasterizer). Typical quality value: 3 or 4. Default: 1.

### `AntiAlias`

```csharp
bool AntiAlias
```

Gets or initialises whether the scanline fill computes fractional pixel coverage (anti-aliasing). When false, fills are binary (pixel-identical to the original rasterizer). Default: true.

### `GammaCorrect`

```csharp
bool GammaCorrect
```

Gets or initialises whether anti-aliased fills blend colour channels in linear light (gamma-correct). When false, channels are blended directly in sRGB space (the legacy behaviour, which renders edges slightly lighter). Has no effect when `AntiAlias` is false. Default: true.

### `Scale`

```csharp
double Scale => Dpi / 72.0
```

Computes the scale factor from PDF points to device pixels for this DPI.

---

_Source: [`src/Chuvadi.Pdf.Rendering/RenderOptions.cs`](../../../src/Chuvadi.Pdf.Rendering/RenderOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
