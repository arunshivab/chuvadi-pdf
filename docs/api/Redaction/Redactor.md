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
- Form XObjects and tiling-pattern cells that a rectangle reaches are rewritten too. A form is shared: what is removed from it is removed wherever the form is drawn, including copies outside the rectangle (over-redaction, never under-redaction). 
- Shadings painted with `sh` that a rectangle reaches are clipped out of it; a mesh shading (types 4 to 7), and a path filled with one, is removed whole (3.35.0). 
- Text patterns (3.35.0): each pattern is matched on the text a viewer shows, joined across text operators and found inside form XObjects, tiling-pattern cells, Type 3 glyphs and annotation appearances. Each match becomes boxes inside the matched glyphs (one per line), which are then removed as rectangles are, in the page and in every form and cell drawn there; an annotation that holds a match (in its appearance, text entries or actions) is removed, and replacement text that matches is removed with the text it stands for. 
- Annotations whose rectangle meets a redaction are removed with the objects only they use; a form widget there has its field value and appearance cleared. 
- Bookmarks and XMP metadata (3.34.0): the matches of patterns that apply to the whole document (no page list) are removed from bookmark titles and from the XMP packet. The redacted file has no document information dictionary (`/Info`). 
- Alternate text that describes removed content (`/ActualText`, `/Alt`, `/E`, in marked content and in the structure tree) is removed. 
- Outside the pages (3.35.0): named destinations, document JavaScript, the open action and document actions are kept; scripts, URIs and alternate texts that match a pattern lose that text; in a pattern redaction attachments are kept only when they can be checked against the patterns and do not match (`RedactionOptions.KeepAttachments`); with rectangles only they are kept and listed as not scanned.  

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

**Remarks:** Patterns (3.35.0, D-396) match the page's text joined across text operators, inside form XObjects, tiling-pattern cells and Type 3 glyphs, and in annotations (their appearances, text entries and actions): an SSN drawn as two pieces is found, its glyphs are removed, and an annotation that holds a match is removed (a widget's value is cleared).  

 The result is checked before anything is written: the redacted document is read back and every pattern is matched again. A match that survives is redacted in a further pass; if any survives that, or a match cannot be placed on the page (a pattern cell repeated thousands of times), a `RedactionException` names the pages and the number of matches (not their text), and nothing is written to `output` (fail closed). `RedactionVerifier.VerifyPatterns` checks a result independently.  <exception cref="RedactionException">A pattern still matches in the redacted output.</exception>

---

_Source: [`src/Chuvadi.Pdf.Redaction/Redactor.cs`](../../../src/Chuvadi.Pdf.Redaction/Redactor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
