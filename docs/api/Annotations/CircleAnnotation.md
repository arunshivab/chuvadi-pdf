# CircleAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Circle (ellipse outline) annotation. The ellipse is inscribed in `PdfAnnotation.Rect`. PDF 32000-1:2008 §12.5.6.8.

```csharp
public sealed class CircleAnnotation : PdfAnnotation
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
