# CompressionResult

**Record** in `Chuvadi.Pdf.Operations` (Operations)

Statistics describing what `PdfCompressor.Compress(Chuvadi.Pdf.Documents.PdfDocument, System.IO.Stream, CompressionOptions?)` did.

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

### `DuplicatesRemoved`

```csharp
int DuplicatesRemoved
```

Byte-identical indirect objects merged into one.

### `ContentStreamsMinified`

```csharp
int ContentStreamsMinified
```

Content streams shrunk by whitespace/comment minification.

### `SkipReason`

```csharp
CompressionSkipReason SkipReason
```

Why the rewrite was skipped, or `CompressionSkipReason.None` when the document was rewritten normally.

### `Skipped`

```csharp
bool Skipped => SkipReason != CompressionSkipReason.None
```

True when the document was left untouched and nothing was written to the output stream because a safety guard fired (see `SkipReason`).

### `Level`

```csharp
PdfCompressionLevel? Level
```

The level used when the document was compressed by level (3.32.0), or null for the options-based `PdfCompressor.Compress(PdfDocument, Stream, CompressionOptions?)`.

### `ImageDpi`

```csharp
double ImageDpi
```

The resolution of the page images at an image level (3.32.0), or zero at the other levels.

### `BelowReadableResolution`

```csharp
bool BelowReadableResolution
```

True when the page images are below `ImageLevelOptions.ReadableDpi` (only possible with `ImageLevelOptions.AllowBelowReadableResolution`), so small text may be hard to read (3.32.0, D-265).

### `OriginalBytes`

```csharp
long OriginalBytes
```

Gets the source document's size in bytes (3.36.0).

### `CompressedBytes`

```csharp
long CompressedBytes
```

Gets the number of bytes written to the output (3.36.0): the rewrite, or the original when the rewrite was not smaller and was kept.

### `PercentReduced`

```csharp
double PercentReduced
```

Gets the size reduction as a percentage of the original (3.36.0): the honest figure for what was actually written — zero when the original was kept, and never negative.

### `KeptOriginal`

```csharp
bool KeptOriginal
```

Gets a value indicating whether the rewrite came out no smaller than the source, so the original bytes were written unchanged (3.36.0). The output is then never larger than the input, and the operation counts are zero.

---

_Source: [`src/Chuvadi.Pdf.Operations/PdfCompressor.cs`](../../../src/Chuvadi.Pdf.Operations/PdfCompressor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
