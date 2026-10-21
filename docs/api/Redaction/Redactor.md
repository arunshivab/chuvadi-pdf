# Redactor

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

Applies true PHI-safe redactions to a PDF document. Text-showing operators (Tj, TJ, ', '') whose visible position falls inside any redaction rectangle are permanently removed from the content stream, then the area is overpainted with an opaque rectangle for visual indication.

```csharp
public static class Redactor
```

## Remarks

The principle: cover-up alone is not redaction. Drawing a black rectangle on top of text leaves the text in the content stream where Ctrl+A copy reveals it. `Redactor` removes the text from the content stream itself and only then paints the overlay rectangle. Conservative principle: when in doubt, REDACT. If a TJ array contains any string whose position is inside a redaction rectangle, the entire TJ is dropped. Over-redaction is preferred over leaking PHI. Limitations: 
 
- Phase 2 uses approximate font-metric width (Helvetica baseline). Exact metric width requires loading and parsing embedded font tables. 
- Image content is not redacted (Phase 3). 
- Form XObjects are not recursed into (Phase 3).

## Methods

### `Apply`

__static__

```csharp
static void Apply(Stream output, PdfDocument document, RedactionOptions options)
```

Applies the redactions in `options` to `document` and writes the result to `output`.

---

_Source: [`src/Chuvadi.Pdf.Redaction/Redactor.cs`](../../../src/Chuvadi.Pdf.Redaction/Redactor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
