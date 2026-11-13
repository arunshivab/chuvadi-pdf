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
- `values` — Map of fully-qualified field name → value. For text and choice fields the value is the string content (any Unicode text); a choice field's value is its option's export value. For a check box: the widget's on-state or export value (see `FormWidget`), or `Yes`, `On` or `true` (any case) to turn it on, and `Off`, `false` or an empty string to turn it off. For a radio group: the on-state or export value of the button to turn on, or `Off` for none. Pushbuttons have no value and are left unchanged. An empty string clears a text or choice field. Names that match no field are ignored. <exception cref="FormException"> A check box or radio-group value matches none of the field's states (3.34.0; `FormException.Reason` is `FormErrorReason.UnknownState`). </exception>

**Remarks:** Since 3.34.0 a button's `/V` is written as a name and every widget's `/AS` is set: the matching widget gets its own on-state, the others `/Off`. Text values outside Latin-1 are written as UTF-16 text strings. Also since 3.34.0, a text or choice value removes the field's rich-text value (`/RV`), which would otherwise still show the old text, and a choice value sets the selected option indices (`/I`) to the options it matches, or removes them when it matches none. To produce fixed page content from the filled values, open the result and flatten it with `AnnotationFlattener` (Operations): the flattener draws text and choice values itself when `NeedAppearances` is set, which `Fill` does.

### `Fill`

__static__

```csharp
static void Fill(Stream output, PdfDocument document, IReadOnlyDictionary<string, string> values, FormFillOptions options)
```

Fills the given field values and writes the resulting PDF to `output`, with options (3.34.0).

**Parameters**

- `output` — The stream to write the filled PDF to.
- `document` — The source document.
- `values` — Map of fully-qualified field name → value, as for `Fill(Stream, PdfDocument, IReadOnlyDictionary{string, string})`.
- `options` — Fill options. `FormFillOptions.Encryption` encrypts the output; without it the output is not encrypted, even when the source was. `FormFillOptions.Validate` checks values first. `FormFillOptions.UnknownFields` cannot be `FormUnknownFields.Report` here, as this overload returns nothing: use the overload that returns a `FormFillResult`. <exception cref="ArgumentException"> `FormFillOptions.UnknownFields` is `FormUnknownFields.Report`. </exception> <exception cref="FormException"> A value is refused; `FormException.Reason` says why. </exception>

### `Fill`

__static__

```csharp
static FormFillResult Fill(Stream output, PdfDocument document, IReadOnlyDictionary<string, IReadOnlyList<string>> values, FormFillOptions options)
```

Fills the given field values, several per field where the field takes them, writes the resulting PDF to `output` and reports what was done (3.34.0).

**Parameters**

- `output` — The stream to write the filled PDF to.
- `document` — The source document.
- `values` — Map of fully-qualified field name → values. A multi-select list box takes any number of its options' export values: `/V` is written as one string for one value and as an array for more, and `/I` lists the indices of the options chosen. Every other field takes one value, as for `Fill(Stream, PdfDocument, IReadOnlyDictionary{string, string})`. An empty list, like an empty string, clears a text or choice field and turns a button off.
- `options` — Fill options; see `FormFillOptions`.

**Returns:** The fields written and, with `FormUnknownFields.Report`, the names that matched no field. <exception cref="FormException"> A value is refused; `FormException.Reason` says why. Nothing is written. </exception>

---

_Source: [`src/Chuvadi.Pdf.Forms/FormFiller.cs`](../../../src/Chuvadi.Pdf.Forms/FormFiller.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
