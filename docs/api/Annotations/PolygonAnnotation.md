# PolygonAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Polygon annotation: a closed shape connecting `Vertices`. PDF 32000-1:2008 §12.5.6.9.

```csharp
public sealed class PolygonAnnotation : PdfAnnotation
```

## Constructors

### `PolygonAnnotation(int pageIndex, RectangleF rect, IReadOnlyList<PointF> vertices, BorderStyle? borderStyle = null, ColorF? interiorColor = null, string? contents = null, ColorF? color = null, string? author = null, float opacity = 1f)`

Initialises a polygon annotation. <exception cref="ArgumentNullException">When `vertices` is null.</exception> <exception cref="ArgumentException">When fewer than 3 vertices are provided.</exception>

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
