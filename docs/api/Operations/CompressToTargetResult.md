# CompressToTargetResult

**Record** in `Chuvadi.Pdf.Operations` (Operations)

The outcome of a `PdfCompressor.CompressToTarget` call.

```csharp
public sealed record CompressToTargetResult
```

## Properties

### `FinalSize`

```csharp
long FinalSize
```

The size, in bytes, of the document written to the output stream.

### `QualityUsed`

```csharp
int QualityUsed
```

The JPEG quality used for the written output. Zero when the document was not recompressed (for example a skipped signed or encrypted document).

### `TargetMet`

```csharp
bool TargetMet
```

`true` when the written output is at or below the target size; `false` when even the lowest quality exceeded it (the smallest achievable output is written regardless).

### `SkipReason`

```csharp
CompressionSkipReason SkipReason
```

The reason compression was skipped, or `CompressionSkipReason.None`. When skipped, the original document is re-serialized to the output unchanged.

---

_Source: [`src/Chuvadi.Pdf.Operations/CompressToTargetResult.cs`](../../../src/Chuvadi.Pdf.Operations/CompressToTargetResult.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
