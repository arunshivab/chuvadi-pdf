# FreeTextAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Free-text annotation drawn directly on the page (§12.5.6.6).

```csharp
public sealed class FreeTextAnnotation : PdfAnnotation
```

## Constructors

### `FreeTextAnnotation(int pageIndex, RectangleF rect, string contents, ColorF? color = null, string? author = null, float opacity = 1f, string defaultAppearance = "/Helvetica 12 Tf 0 0 0 rg")`

Initialises a free-text annotation.

## Properties

### `DefaultAppearance`

```csharp
string DefaultAppearance
```

Gets the PDF default-appearance string (DA), describing font, size, and colour using PDF content-stream operators.

---

_Source: [`src/Chuvadi.Pdf.Annotations/Annotations.cs`](../../../src/Chuvadi.Pdf.Annotations/Annotations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
