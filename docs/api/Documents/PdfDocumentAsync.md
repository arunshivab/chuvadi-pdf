# PdfDocumentAsync

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Async-capable entry points for `PdfDocument`.

```csharp
public static class PdfDocumentAsync
```

## Remarks

PDF parsing is inherently random-access (cross-reference table at file tail, indirect objects pointed to by absolute byte offsets) so the underlying `Chuvadi.Pdf.IO.PdfReader` needs a seekable stream. When the input stream is not seekable (e.g. a network response stream in Blazor WebAssembly), the bytes are first buffered into a `MemoryStream`.  

 Cancellation is checked at the buffering boundary. Once parsing begins, it runs to completion synchronously on the calling thread.

## Methods

### `OpenAsync`

__static__

```csharp
static async Task<PdfDocument> OpenAsync(Stream stream, CancellationToken cancellationToken = default)
```

Opens a PDF from `stream` asynchronously. Buffers non-seekable streams into memory before parsing.

### `OpenAsync`

__static__

```csharp
static async Task<PdfDocument> OpenAsync(Stream stream, string password, CancellationToken cancellationToken = default)
```

Opens an encrypted PDF asynchronously. Buffers non-seekable streams into memory before parsing.

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfDocumentAsync.cs`](../../../src/Chuvadi.Pdf.Documents/PdfDocumentAsync.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
