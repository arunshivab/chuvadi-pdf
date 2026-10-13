# CompressToTargetOptions

**Record** in `Chuvadi.Pdf.Operations` (Operations)

Options for `PdfCompressor.CompressToTarget`. The compressor binary-searches JPEG quality between `MinQuality` and `MaxQuality` for the highest quality whose output fits the target size; `BaseOptions` supplies all other compression knobs (stripping, rewrite hazards). Image recompression is always enabled during the search, since quality only affects size when images are re-encoded.

```csharp
public sealed record CompressToTargetOptions
```

## Properties

### `MinQuality`

```csharp
int MinQuality
```

The lowest JPEG quality (1-100) the search will try. Default 30.

### `MaxQuality`

```csharp
int MaxQuality
```

The highest JPEG quality (1-100) the search will try. Default 90.

### `BaseOptions`

```csharp
CompressionOptions BaseOptions
```

The base compression options (stripping flags, rewrite-hazard opt-ins). `CompressionOptions.RecompressImages` and `CompressionOptions.JpegQuality` are overridden per search step.

### `AllowRasterizeFallback`

```csharp
bool AllowRasterizeFallback
```

Opt-in: when even the lowest JPEG quality cannot meet the target (the vector-only floor — content streams and fonts dominate and there are no images left to shrink), rasterize every page to a JPEG image and write an image-only PDF, descending from `RasterizeDpi` to `MinRasterizeDpi` until the target is met. Default `false`: rasterizing destroys text selection, search and accessibility, so it never happens silently.

### `RasterizeDpi`

```csharp
double RasterizeDpi
```

The resolution of the rasterize fallback's first attempt. Default 150 DPI.

### `MinRasterizeDpi`

```csharp
double MinRasterizeDpi
```

The floor of the rasterize fallback's DPI descent. Default 72 DPI.

---

_Source: [`src/Chuvadi.Pdf.Operations/CompressToTargetOptions.cs`](../../../src/Chuvadi.Pdf.Operations/CompressToTargetOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
