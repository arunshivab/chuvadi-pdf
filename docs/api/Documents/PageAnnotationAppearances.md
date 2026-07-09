# PageAnnotationAppearances

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Collects the drawable annotation appearances of a page: every annotation with a resolvable normal appearance stream that is not hidden and not a popup, together with its §12.5.5 placement.

```csharp
public static class PageAnnotationAppearances
```

## Methods

### `Collect`

__static__

```csharp
static IReadOnlyList<AnnotationAppearance> Collect(PdfPage page, PdfObjectStore objects)
```

Collects the drawable annotation appearances of `page`.

**Parameters**

- `page` — The page whose annotations are collected.
- `objects` — The object store for resolving indirect references.

**Returns:** The drawable appearances in `/Annots` order. <exception cref="ArgumentNullException"> Thrown when `page` or `objects` is null. </exception>

**Remarks:** Skipped: annotations without a resolvable `/AP /N` stream, annotations whose `/F` flags include Hidden or NoView, popup annotations (drawn only via their parent markup), and annotations whose `/Rect` or appearance `/BBox` is degenerate. This mirrors how interactive viewers decide what to paint, so hybrid XFA/AcroForm documents — whose field values live in widget appearance streams — render, extract, and flatten with their values visible.

---

_Source: [`src/Chuvadi.Pdf.Documents/AnnotationAppearance.cs`](../../../src/Chuvadi.Pdf.Documents/AnnotationAppearance.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
