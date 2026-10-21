# OptionalContentWriter

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Writes optional-content (layer) visibility changes to a PDF document.

```csharp
public static class OptionalContentWriter
```

## Remarks

Complements `OptionalContentReader`: read the groups, then toggle any of them on or off by name and write a new document. The change edits the default configuration's (/OCProperties/D) /ON and /OFF arrays; the original document is not modified in place.

## Methods

### `SetVisibility`

__static__

```csharp
static void SetVisibility(Stream output, PdfDocument document, IReadOnlyDictionary<string, bool> visibilityByName)
```

Writes a copy of `document` to `output` with the named layers shown or hidden in the default configuration.

**Parameters**

- `output` — Destination stream for the modified document.
- `document` — The source document.
- `visibilityByName` — Layer name → visibility. `true` shows the layer, `false` hides it. Names not present in the document are ignored; layers not listed keep their current visibility. <exception cref="ArgumentNullException">When any argument is null.</exception> <exception cref="InvalidOperationException"> When the document declares no optional content (/OCProperties). </exception>

---

_Source: [`src/Chuvadi.Pdf.Documents/OptionalContentWriter.cs`](../../../src/Chuvadi.Pdf.Documents/OptionalContentWriter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
