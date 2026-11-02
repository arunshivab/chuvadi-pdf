# PageLayout

**Class** in `Chuvadi.Pdf.Text` (Text)

The reading order of one page for layout-aware text extraction: where it comes from and, for `ReadingOrderSource.Columns`, the bands and columns in the order they are read.

```csharp
public sealed class PageLayout
```

## Remarks

`TextExtractor.AnalyzeLayout(PdfPage)` returns what extraction would use, so an application can show it; an application can also build its own and pass it to `TextExtractor.ExtractText(PdfPage, PageLayout)` to override it. Rectangles are in PDF user space (origin bottom-left).

## Constructors

### `PageLayout(ReadingOrderSource source, IReadOnlyList<LayoutBand> bands)`

Initialises a page layout.

**Parameters**

- `source` — Where the reading order comes from.
- `bands` — The bands in reading order; used only when `source` is `ReadingOrderSource.Columns`. <exception cref="ArgumentNullException">`bands` is null.</exception>

## Properties

### `Rows`

__static__

```csharp
static PageLayout Rows
```

Gets a layout that reads plain rows, top to bottom (3.28 order).

### `Tagged`

__static__

```csharp
static PageLayout Tagged
```

Gets a layout that uses the structure tree's order when the page has one.

### `Source`

```csharp
ReadingOrderSource Source
```

Gets where the reading order comes from.

### `Bands`

```csharp
IReadOnlyList<LayoutBand> Bands
```

Gets the bands in reading order (empty unless `Source` is `ReadingOrderSource.Columns`). Text outside every band is read as rows, placed by its height among the bands.

---

_Source: [`src/Chuvadi.Pdf.Text/PageLayout.cs`](../../../src/Chuvadi.Pdf.Text/PageLayout.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
