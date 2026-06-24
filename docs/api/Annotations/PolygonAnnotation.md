# PolygonAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Polygon annotation: a closed shape connecting `Vertices`. PDF 32000-1:2008 §12.5.6.9.

```csharp
public sealed class PolygonAnnotation : PdfAnnotation
```

## Properties

### `Vertices`

```csharp
IReadOnlyList<PointF> Vertices
```

Gets the vertices of the polygon in order. PDF /Vertices entry.

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
