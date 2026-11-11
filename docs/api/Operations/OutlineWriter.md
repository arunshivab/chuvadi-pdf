# OutlineWriter

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Writes a document outline (bookmark tree) onto an existing document, replacing any existing outline. Nested children are supported to any depth. The rest of the document is preserved unchanged. PDF 32000-1:2008 §12.3.3 — Document outline.

```csharp
public static class OutlineWriter
```

## Remarks

Where each bookmark goes depends on how its `OutlineEntry` was made (3.33.0): 
 
- with a page index: the whole page (`[page /Fit]`), closed, as before 3.33.0; 
- with a `PdfDestination`: exactly that destination; 
- with a source `OutlineItem`: the source bookmark's `/Dest` or `/A` (named destinations and every kind of action), colour `/C`, style `/F`, structure element `/SE` and open or closed state, unchanged.

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
- `entries` — The top-level bookmarks, in order. <exception cref="ArgumentNullException"> Thrown when any argument is null. </exception> <exception cref="ArgumentException"> An entry's `OutlineEntry.Source` was not read from `document`. </exception>

---

_Source: [`src/Chuvadi.Pdf.Operations/OutlineWriter.cs`](../../../src/Chuvadi.Pdf.Operations/OutlineWriter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
