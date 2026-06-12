# CompressionResult

**Record** in `Chuvadi.Pdf.Operations` (Operations)

Statistics describing what `PdfCompressor.Compress` did.

```csharp
public sealed record CompressionResult
```

## Properties

### `ObjectsRemoved`

```csharp
int ObjectsRemoved
```

Indirect objects dropped as unreachable from the trailer.

### `StreamsCompressed`

```csharp
int StreamsCompressed
```

Previously-uncompressed streams that were Flate-compressed.

### `ImagesRecompressed`

```csharp
int ImagesRecompressed
```

Images re-encoded as JPEG.

---

_Source: [`src/Chuvadi.Pdf.Operations/PdfCompressor.cs`](../../../src/Chuvadi.Pdf.Operations/PdfCompressor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
