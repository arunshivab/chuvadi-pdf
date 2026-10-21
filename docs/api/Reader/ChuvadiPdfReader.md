# ChuvadiPdfReader

**Class** in `Chuvadi.Pdf.Reader` (Reader)

Production implementation of `IPdfReader` backed by the Chuvadi PDF library. Register as a singleton in DI; methods are stateless apart from the cached `SvgRenderer` instances (full-page and thumbnail), which are thread-safe to share.

```csharp
public sealed class ChuvadiPdfReader : IPdfReader
```

## Constructors

### `ChuvadiPdfReader()`

Initialises a new `ChuvadiPdfReader`.

## Methods

### `OpenAsync`

```csharp
Task<PdfDocument> OpenAsync(Stream stream, string fileName, string? password = null, CancellationToken cancellationToken = default)
```

<inheritdoc />

### `RenderPageSvgAsync`

```csharp
Task<string> RenderPageSvgAsync(PdfDocument document, int pageIndex, CancellationToken cancellationToken = default)
```

<inheritdoc />

### `RenderThumbnailAsync`

```csharp
Task<string> RenderThumbnailAsync(PdfDocument document, int pageIndex, CancellationToken cancellationToken = default)
```

<inheritdoc />

### `GetOutlinesAsync`

```csharp
Task<IReadOnlyList<OutlineItem>> GetOutlinesAsync(PdfDocument document, CancellationToken cancellationToken = default)
```

<inheritdoc />

### `SearchAsync`

```csharp
async IAsyncEnumerable<SearchMatch> SearchAsync(PdfDocument document, string query, SearchOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
```

<inheritdoc />

### `GetTextRunsAsync`

```csharp
Task<IReadOnlyList<TextRun>> GetTextRunsAsync(PdfDocument document, int pageIndex, CancellationToken cancellationToken = default)
```

<inheritdoc />

---

_Source: [`src/Chuvadi.Pdf.Reader/ChuvadiPdfReader.cs`](../../../src/Chuvadi.Pdf.Reader/ChuvadiPdfReader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
