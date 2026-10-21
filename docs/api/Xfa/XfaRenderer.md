# XfaRenderer

**Class** in `Chuvadi.Pdf.Xfa.Render` (Xfa)

Renders a document's XFA template to a new PDF. Positioned and flowed (top-to-bottom, left-right-top-to-bottom) layouts are supported; datasets values are merged into bound fields; flowed content paginates across content areas and pages per the page set's occurrence rules, honoring forced breaks.

```csharp
public static class XfaRenderer
```

## Methods

### `OpenComposed`

__static__

```csharp
static PdfDocument OpenComposed(Stream input, bool leaveOpen = false, XfaRenderOptions? options = null)
```

Opens a PDF so that XFA "just works": a dynamic XFA document (catalog `/NeedsRendering true`, whose page content is a placeholder) is composed through `Render` and the composed static document is returned; every other document — hybrid and static XFA included, whose real content already lives in pages and widget appearances — comes back exactly as `PdfDocument.Open(Stream, bool)` returns it. A dynamic document whose template cannot be composed falls back to the original document rather than failing the open.

**Parameters**

- `input` — The stream holding the PDF.
- `leaveOpen` — Whether `input` is left open when the returned document is disposed (or, for a composed document, after composition).
- `options` — Rendering options for the dynamic composition, or null for `XfaRenderOptions.Default` (scripts run in Full mode).

**Returns:** The opened document, composed when it was dynamic XFA.

### `Render`

__static__

```csharp
static void Render(Stream output, PdfDocument document, XfaRenderOptions options)
```

Renders the XFA template of `document` to a new PDF written to `output`.

**Parameters**

- `output` — The destination stream for the rendered PDF.
- `document` — The source document; must contain an XFA template.
- `options` — Rendering options. <exception cref="ArgumentNullException">A required argument is null.</exception> <exception cref="XfaRenderException">The document has no usable XFA template.</exception>

---

_Source: [`src/Chuvadi.Pdf.Xfa/Render/XfaRenderer.cs`](../../../src/Chuvadi.Pdf.Xfa/Render/XfaRenderer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
