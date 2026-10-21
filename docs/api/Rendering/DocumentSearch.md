# DocumentSearch

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Searches the text of a `PdfDocument` by page, streaming matches asynchronously.

```csharp
public static class DocumentSearch
```

## Remarks

Builds on `PdfPageExtensions.GetTextRuns`. Concatenates the page's text runs in reading order, performs a sliding-window string search, and emits matches as they're found. Cancellation is checked between pages and between matches.

## Methods

### `SearchAsync`

__static__

```csharp
static async IAsyncEnumerable<SearchMatch> SearchAsync(PdfDocument document, string query, SearchOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
```

Searches the document for occurrences of `query`.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/DocumentSearch.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/DocumentSearch.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
