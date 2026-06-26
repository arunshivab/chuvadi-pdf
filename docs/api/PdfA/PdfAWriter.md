# PdfAWriter

**Class** in `Chuvadi.Pdf.PdfA` (PdfA)

Writes PDF/A-1b and PDF/A-2b conforming documents.

```csharp
public static class PdfAWriter
```

## Methods

### `Write`

__static__

```csharp
static PdfAResult Write(Stream output, PdfDocument document, PdfAOptions options)
```

Writes `document` to `output` as a PDF/A file at the requested conformance level. When the document cannot be made conforming, nothing is written and the returned result reports why.

**Parameters**

- `output` — The destination stream.
- `document` — The source document (mutated in place during embedding).
- `options` — The conformance options.

**Returns:** The write result. <exception cref="ArgumentNullException">A parameter is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.PdfA/PdfAWriter.cs`](../../../src/Chuvadi.Pdf.PdfA/PdfAWriter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
