# PdfCompressor

**Class** in `Chuvadi.Pdf.Operations` (Operations)

```csharp
public static class PdfCompressor
```

## Methods

### `CompressToTarget`

__static__

```csharp
static CompressToTargetResult CompressToTarget(PdfDocument document, Stream output, long targetBytes, CompressToTargetOptions? options = null)
```

Compresses `document` to `output`, binary-searching JPEG quality for the highest setting whose output is at or below `targetBytes`. When no quality meets the target, the smallest achievable output is written and `CompressToTargetResult.TargetMet` is `false`. Signed or encrypted documents that are not opted in for rewrite are re-serialized unchanged and reported via `CompressToTargetResult.SkipReason`.

**Parameters**

- `document` — The document to compress.
- `output` — The stream the compressed document is written to.
- `targetBytes` — The desired maximum output size, in bytes.
- `options` — The search bounds and base compression options, or null for defaults.

**Returns:** The size, quality, and target outcome of the written document.

---

_Source: [`src/Chuvadi.Pdf.Operations/PdfCompressorTarget.cs`](../../../src/Chuvadi.Pdf.Operations/PdfCompressorTarget.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
