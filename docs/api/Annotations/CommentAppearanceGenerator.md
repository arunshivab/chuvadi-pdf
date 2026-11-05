# CommentAppearanceGenerator

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Makes a normal appearance for a comment annotation from its own entries: Highlight, Underline, Squiggly, StrikeOut, Square, Circle, Line, Polygon, PolyLine, Ink, FreeText, Text (the seven standard note icons), Stamp (its name in a box), Caret and FileAttachment (four icons).

```csharp
public static class CommentAppearanceGenerator
```

## Methods

### `IsSupported`

__static__

```csharp
static bool IsSupported(string subtype)
```

True when `subtype` is a comment type this generator draws.

**Parameters**

- `subtype` — The annotation `/Subtype`.

**Returns:** True when supported. <exception cref="ArgumentNullException">`subtype` is null.</exception>

### `Generate`

__static__

```csharp
static PdfStream? Generate(PdfDictionary annotation, PdfObjectStore objects, PdfDictionary? acroForm, Action<PdfWarning>? onWarning, int? pageNumber)
```

Returns a generated normal appearance for `annotation`, or null when its type is not supported or it has no drawable size.

**Parameters**

- `annotation` — The annotation dictionary.
- `objects` — The document's objects.
- `acroForm` — The document's `/AcroForm`, for FreeText's default `/DR`; may be null.
- `onWarning` — Receives warnings; may be null.
- `pageNumber` — The 1-based page number for warnings, or null.

**Returns:** A form XObject the size of the annotation's `/Rect`, or null. <exception cref="ArgumentNullException">`annotation` or `objects` is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Annotations/CommentAppearanceGenerator.cs`](../../../src/Chuvadi.Pdf.Annotations/CommentAppearanceGenerator.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
