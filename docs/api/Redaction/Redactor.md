# Redactor

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

Applies true PHI-safe redactions to a PDF document: what lies under a redaction rectangle (or matches a text pattern) is removed from the file, not just covered, and the area is then overpainted with an opaque rectangle (`RedactionOptions.DrawOverlay`).

```csharp
public static class Redactor
```

## Remarks

The principle: cover-up alone is not redaction. A black rectangle drawn on top of text leaves the text in the content stream, where copying or text extraction reveals it. `Redactor` removes the content itself:  
 
- Text: each glyph whose box meets a rectangle is removed, using the font's own glyph widths; the glyphs around it stay, with a gap of the removed width (or `RedactionRect.ReplacementText`). 
- Images: the covered pixels are blanked in a copy of the image and the rest is kept; an image that cannot be blanked (or is wholly covered) is removed, and an image no longer drawn leaves the file. 
- Vector paths: the covered area is cut out of a painted path (`VectorRedactionMode.SubtractPrecise`, the default); a path that cannot be cut exactly is removed whole. 
- Form XObjects and tiling patterns are rewritten the same way, in every place they are drawn. 
- Annotations whose rectangle meets a redaction are removed with the objects only they use; a form widget there has its field value and appearance cleared. 
- Bookmarks and XMP metadata (3.34.0): the matches of patterns that apply to the whole document (no page list) are removed from bookmark titles and from the XMP packet. The redacted file has no document information dictionary (`/Info`). 
- Alternate text that describes removed content (`/ActualText`, `/Alt`, `/E`, in marked content and in the structure tree) is removed.  

 Conservative principle: when in doubt, remove. Over-redaction is preferred over leaking PHI. `ApplyWithReport` returns what was removed on each page; `RedactionVerifier` checks an output independently.

## Methods

### `Apply`

__static__

```csharp
static void Apply(Stream output, PdfDocument document, RedactionOptions options)
```

Applies the redactions in `options` to `document` and writes the result to `output`.

### `ApplyWithReport`

__static__

```csharp
static RedactionReport ApplyWithReport(Stream output, PdfDocument document, RedactionOptions options)
```

Applies the redactions in `options` to `document`, writes the result to `output`, and reports what was removed on each page (3.34.0, D-335). The output is the same as `Apply` writes.

**Parameters**

- `output` — The stream to write the redacted PDF to.
- `document` — The source document.
- `options` — The rectangles and patterns to redact.

**Returns:** One entry per rewritten page, in page order.

---

_Source: [`src/Chuvadi.Pdf.Redaction/Redactor.cs`](../../../src/Chuvadi.Pdf.Redaction/Redactor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
