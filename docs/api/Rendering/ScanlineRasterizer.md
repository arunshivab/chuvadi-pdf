# ScanlineRasterizer

**Class** in `Chuvadi.Pdf.Rendering` (Rendering)

Fills vector paths into a `PixelBuffer` using a scanline edge-crossing algorithm.

```csharp
public sealed class ScanlineRasterizer
```

## Remarks

Supports both PDF fill rules: 
 
- Non-zero winding number — PDF operators f, F, B, b 
- Even-odd — PDF operators f*, B*, b*  Input is a list of sub-paths from `PathFlattener`, each being a closed list of `PointF` vertices in device space. When `AntiAlias` is false (the default), the fill is binary: a pixel is either painted or not, sampled at the pixel centre. This is the original behaviour and is preserved byte-for-byte. When `AntiAlias` is true, each pixel row is sampled at several sub-scanlines and exact fractional horizontal coverage is accumulated per pixel, then blended once at the corresponding alpha. This produces smooth, properly-weighted edges. PDF 32000-1:2008 §8.5.3.3 — Filling.

## Properties

### `AntiAlias`

```csharp
bool AntiAlias
```

Gets or sets whether the fill computes fractional pixel coverage (anti-aliasing). Default: false (binary fill, pixel-identical to the original rasterizer).

### `GammaCorrect`

```csharp
bool GammaCorrect
```

Gets or sets whether anti-aliased fills blend colour channels in linear light (gamma-correct). Forwarded to `PixelBuffer.BlendPixel(int, int, ColorF, bool)`.

---

_Source: [`src/Chuvadi.Pdf.Rendering/ScanlineRasterizer.cs`](../../../src/Chuvadi.Pdf.Rendering/ScanlineRasterizer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
