# GenericAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Catch-all annotation for subtypes not specifically modelled. Preserves the basic fields so callers can at least see what's on the page.

```csharp
public sealed class GenericAnnotation : PdfAnnotation
```

## Constructors

### `GenericAnnotation(int pageIndex, RectangleF rect, string rawSubtype, string? contents = null, ColorF? color = null, string? author = null, float opacity = 1f)`

Initialises a generic (unmodelled-subtype) annotation.

## Properties

### `RawSubtype`

```csharp
string RawSubtype
```

Gets the raw PDF /Subtype name as it appeared in the document.

---

_Source: [`src/Chuvadi.Pdf.Annotations/Annotations.cs`](../../../src/Chuvadi.Pdf.Annotations/Annotations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
