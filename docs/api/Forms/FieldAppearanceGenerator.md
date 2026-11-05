# FieldAppearanceGenerator

**Class** in `Chuvadi.Pdf.Forms` (Forms)

Makes a widget's normal appearance stream from the field's value and its `/DA`, `/Q`, `/MK` and flags: text fields (single line, multiline, comb, password), check boxes and radio buttons, combo and list boxes, and push-button captions. Signature fields are never generated.

```csharp
public static class FieldAppearanceGenerator
```

## Methods

### `Generate`

__static__

```csharp
static PdfStream? Generate(PdfDictionary widget, PdfObjectStore objects, PdfDictionary? acroForm, Action<PdfWarning>? onWarning, int? pageNumber)
```

Returns a generated normal appearance for `widget`, or null when the field is a signature, has no drawable size, or its type is unknown. Characters the `/DA` font cannot show are drawn with the LiPi Sans face of their script and reported as `PdfWarningKind.FontSubstituted`.

**Parameters**

- `widget` — The widget annotation (merged with its field, or a child of it).
- `objects` — The document's objects.
- `acroForm` — The document's `/AcroForm` dictionary, for the default `/DA` and `/DR`; may be null.
- `onWarning` — Receives warnings; may be null.
- `pageNumber` — The 1-based page number for warnings, or null.

**Returns:** A form XObject the size of the widget's `/Rect`, or null. <exception cref="ArgumentNullException">`widget` or `objects` is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Forms/FieldAppearanceGenerator.cs`](../../../src/Chuvadi.Pdf.Forms/FieldAppearanceGenerator.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
