# PageContentInventory

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Counts what a page actually paints, from the same display list the SVG and raster renderers draw — page content, nested form XObjects, Type 3 glyphs and visible annotation appearances included. Clipping, invisible text (render mode 3 and clip-only mode 7), fully transparent paint and empty geometry are not counted, so `HasDrawableContent` is false for a page that renders blank (for example the placeholder pages of a dynamic XFA form whose real content needs composing).

```csharp
public sealed class PageContentInventory
```

## Remarks

Content inside optional-content groups is counted whether or not the group is currently visible. A white fill over white paper counts as drawn.

## Properties

### `TextRuns`

```csharp
int TextRuns
```

Gets the number of visible text-show operations (with at least one glyph).

### `Paths`

```csharp
int Paths
```

Gets the number of painted (filled and/or stroked) paths.

### `Images`

```csharp
int Images
```

Gets the number of painted images (including stencil masks).

### `Shadings`

```csharp
int Shadings
```

Gets the number of painted shadings (gradients, shading patterns).

### `HasVisibleText`

```csharp
bool HasVisibleText => TextRuns > 0
```

Gets whether the page paints any visible text.

### `HasDrawableContent`

```csharp
bool HasDrawableContent => TextRuns + Paths + Images + Shadings > 0
```

Gets whether the page paints anything at all.

## Methods

### `Inspect`

__static__

```csharp
static PageContentInventory Inspect(PdfDocument document, int pageIndex)
```

Builds the inventory for one page of a document.

**Parameters**

- `document` — The document.
- `pageIndex` — Zero-based page index.

**Returns:** The page's content inventory. <exception cref="ArgumentNullException">Thrown when `document` is null.</exception> <exception cref="ArgumentOutOfRangeException">Thrown when the page index is out of range.</exception>

### `FromDisplayList`

__static__

```csharp
static PageContentInventory FromDisplayList(PageDisplayList list)
```

Builds the inventory from an already-built display list.

**Parameters**

- `list` — The page display list.

**Returns:** The content inventory. <exception cref="ArgumentNullException">Thrown when `list` is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/PageContentInventory.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/PageContentInventory.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
