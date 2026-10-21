# FormFiller

**Class** in `Chuvadi.Pdf.Forms` (Forms)

Fills AcroForm field values in a PDF document and writes the result.

```csharp
public static class FormFiller
```

## Remarks

For each fully-qualified field name in `values`, locates the field's indirect object in the document, replaces its `/V` entry, and writes a new PDF with the updated objects. Also sets `/AcroForm/NeedAppearances=true` so that PDF viewers regenerate the visible appearance streams from the new values. PDF 32000-1:2008 §12.7.2 — Interactive form dictionary.

## Methods

### `Fill`

__static__

```csharp
static void Fill(Stream output, PdfDocument document, IReadOnlyDictionary<string, string> values)
```

Fills the given field values and writes the resulting PDF to `output`.

**Parameters**

- `output` — The stream to write the filled PDF to.
- `document` — The source document.
- `values` — Map of fully-qualified field name → value. For text and choice fields the value is the string content. For checkboxes use "Yes" or "Off" (or the appearance state names defined by the field).

---

_Source: [`src/Chuvadi.Pdf.Forms/FormFiller.cs`](../../../src/Chuvadi.Pdf.Forms/FormFiller.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
