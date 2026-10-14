# PdfPageExtensions

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Extensions on `PdfDocument` and `PdfPage` for the display-list and text-run APIs.

```csharp
public static class PdfPageExtensions
```

## Methods

### `BuildDisplayList`

__static__

```csharp
static PageDisplayList BuildDisplayList(this PdfDocument document, int pageIndex)
```

Builds a `PageDisplayList` for the given page index.

### `HasDrawableContent`

__static__

```csharp
static bool HasDrawableContent(this PdfDocument document, int pageIndex)
```

Returns whether page `pageIndex` paints anything visible (text, paths, images or shadings, including form XObjects and annotation appearances). See `PageContentInventory`.

### `GetContentInventory`

__static__

```csharp
static PageContentInventory GetContentInventory(this PdfDocument document, int pageIndex)
```

Returns what page `pageIndex` paints, by category. See `PageContentInventory`.

### `GetTextRuns`

__static__

```csharp
static IReadOnlyList<TextRun> GetTextRuns(this PdfDocument document, int pageIndex)
```

Returns the text runs of `pageIndex` in reading order.

**Remarks:** Glyph-level positions are derived from font-metric data (PDF /Widths or /W tables); selection-overlay consumers can use these for native browser text selection.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/PdfPageExtensions.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/PdfPageExtensions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
