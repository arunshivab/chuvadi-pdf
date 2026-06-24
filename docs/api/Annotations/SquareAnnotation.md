# SquareAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Square (rectangle outline) annotation. PDF 32000-1:2008 §12.5.6.8.

```csharp
public sealed class SquareAnnotation : PdfAnnotation
```

## Properties

### `BorderStyle`

```csharp
BorderStyle? BorderStyle
```

Gets the border style, or null for the viewer default.

### `InteriorColor`

```csharp
ColorF? InteriorColor
```

Gets the interior (fill) colour, or null for an unfilled outline. PDF /IC entry.

---

_Source: [`src/Chuvadi.Pdf.Annotations/ShapeAnnotations.cs`](../../../src/Chuvadi.Pdf.Annotations/ShapeAnnotations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
