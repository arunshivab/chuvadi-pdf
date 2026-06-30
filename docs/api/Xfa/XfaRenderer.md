# XfaRenderer

**Class** in `Chuvadi.Pdf.Xfa.Render` (Xfa)

Renders a document's XFA template to a new PDF. Phase B supports positioned layout: draws, fields, captions, borders, and check buttons placed by their explicit coordinates.

```csharp
public static class XfaRenderer
```

## Methods

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
