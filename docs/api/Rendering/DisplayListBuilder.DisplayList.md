# DisplayListBuilder

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Builds a `PageDisplayList` by walking a page's content stream and translating each PDF operator to a `RenderOp`.

```csharp
public static class DisplayListBuilder
```

## Methods

### `Build`

__static__

```csharp
static PageDisplayList Build(PdfDocument document, int pageIndex, bool flattenTransforms = false)
```

Builds a display list for the given page.

**Parameters**

- `document` — The source document.
- `pageIndex` — Zero-based page index.
- `flattenTransforms` — When true, the returned list contains no `TransformOp`s: the current transformation matrix is already baked into every op's geometry (and exposed per-op, e.g. `PathOp.Ctm`), so a consumer walking the raw op list never has to track a transform stack. Renderers that interpret `TransformOp` should leave this false (the default), which preserves the full op stream.

### `Build`

__static__

```csharp
static PageDisplayList Build(PdfDocument document, int pageIndex, OptionalContentVisibility optionalContent, bool flattenTransforms = false)
```

Builds the display list for one page with an explicit layer visibility: content in hidden optional-content groups is left out (pass `OptionalContentVisibility.AllVisible` to keep everything, as redaction verification does).

**Parameters**

- `document` — The document.
- `pageIndex` — Zero-based page index.
- `optionalContent` — Which layers are visible.
- `flattenTransforms` — As for `Build(PdfDocument, int, bool)`.

**Returns:** The page's display list. <exception cref="ArgumentNullException">Thrown when an argument is null.</exception> <exception cref="ArgumentOutOfRangeException">Thrown when the page index is out of range.</exception>

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListBuilder.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
