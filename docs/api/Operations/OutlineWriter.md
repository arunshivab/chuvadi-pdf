# OutlineWriter

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Writes a document outline (bookmark tree) onto an existing document, replacing any existing outline. Each entry targets a page by zero-based index using an explicit `/Fit` destination. Nested children are supported to any depth. The rest of the document is preserved unchanged. PDF 32000-1:2008 §12.3.3 — Document outline.

```csharp
public static class OutlineWriter
```

## Methods

### `Apply`

__static__

```csharp
static void Apply(Stream output, PdfDocument document, IReadOnlyList<OutlineEntry> entries)
```

Writes `entries` as the document outline and emits the result to `output`. An empty list writes no outline (and removes any existing one from the catalog).

**Parameters**

- `output` — The stream to write the updated PDF to.
- `document` — The source document.
- `entries` — The top-level bookmarks, in order. <exception cref="ArgumentNullException"> Thrown when any argument is null. </exception>

---

_Source: [`src/Chuvadi.Pdf.Operations/OutlineWriter.cs`](../../../src/Chuvadi.Pdf.Operations/OutlineWriter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
