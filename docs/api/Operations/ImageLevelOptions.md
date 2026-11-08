# ImageLevelOptions

**Record** in `Chuvadi.Pdf.Operations` (Operations)

Settings for the image levels (`PdfCompressionLevel.ReducedColor`, `PdfCompressionLevel.BlackAndWhiteWithColor` and `PdfCompressionLevel.BlackAndWhite`), 3.32.0 (D-270). The defaults suit most uses; change them only for a file just over a limit.

```csharp
public sealed record ImageLevelOptions
```

## Properties

### `Dpi`

```csharp
double Dpi
```

The resolution of the page images. Default 150 dpi. With `PdfCompressor.Compress(Chuvadi.Pdf.Documents.PdfDocument, System.IO.Stream, PdfCompressionLevel, ImageLevelOptions?, CompressionOptions?)` it is the resolution used. With `PdfCompressor.CompressToTarget(Chuvadi.Pdf.Documents.PdfDocument, System.IO.Stream, long, CompressToTargetOptions?)` the image levels are tried at 150 dpi and then at 130 dpi, and this value is used only when it is below 130 dpi and `AllowBelowReadableResolution` is true: black and white then steps down (by 15% each time) to it.

### `Fills`

```csharp
BlackAndWhiteFills Fills
```

How large grey fills are drawn. Default `BlackAndWhiteFills.DotPattern`.

### `AllowBelowReadableResolution`

```csharp
bool AllowBelowReadableResolution
```

Allows a resolution below `ReadableDpi`. Default false: a lower `Dpi` is rejected. When an output below it is written, the result says so (`CompressionResult.BelowReadableResolution`, `CompressToTargetResult.BelowReadableResolution`) so an app can warn that small text may be hard to read.

## Fields

### `ReadableDpi`

```csharp
const double ReadableDpi = 130
```

The lowest resolution at which small text on large drawings stays readable (measured on A1 drawings, D-269): 130 dpi. Resolutions below it need `AllowBelowReadableResolution`.

### `MinimumDpi`

```csharp
const double MinimumDpi = 50
```

The lowest resolution accepted at all: 50 dpi.

### `MaximumDpi`

```csharp
const double MaximumDpi = 300
```

The highest resolution accepted: 300 dpi. An A1 page at 300 dpi needs about 280 MB of memory while it is drawn.

---

_Source: [`src/Chuvadi.Pdf.Operations/PdfCompressionLevel.cs`](../../../src/Chuvadi.Pdf.Operations/PdfCompressionLevel.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
