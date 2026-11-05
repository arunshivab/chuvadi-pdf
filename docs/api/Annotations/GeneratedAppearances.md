# GeneratedAppearances

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

The appearance provider the renderers, text extraction and flattening use (3.31). A form field is drawn from its value when the form's `/NeedAppearances` is true (text and choice fields) or when the widget has no `/AP`; a comment with no `/AP` is drawn from its own entries. Everything else keeps the file's own appearance. Nothing is written back to the file.

```csharp
public sealed class GeneratedAppearances : IAnnotationAppearanceProvider
```

## Properties

### `Instance`

__static__

```csharp
static GeneratedAppearances Instance
```

The shared instance; it keeps no state.

## Methods

### `GetAppearance`

```csharp
PdfStream? GetAppearance(PdfDictionary annotation, PdfStream? stored, PdfPage page, PdfObjectStore objects)
```

<inheritdoc /> <exception cref="ArgumentNullException">An argument is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Annotations/GeneratedAppearances.cs`](../../../src/Chuvadi.Pdf.Annotations/GeneratedAppearances.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
