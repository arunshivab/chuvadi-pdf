# PolyLineAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

PolyLine annotation: an open shape connecting `Vertices`. PDF 32000-1:2008 §12.5.6.9.

```csharp
public sealed class PolyLineAnnotation : PdfAnnotation
```

## Properties

### `Vertices`

```csharp
IReadOnlyList<PointF> Vertices
```

Gets the vertices of the polyline in order. PDF /Vertices entry.

### `BorderStyle`

```csharp
BorderStyle? BorderStyle
```

Gets the border style, or null for the viewer default.

### `LineEndingStart`

```csharp
LineEnding LineEndingStart
```

Gets the line-ending style at the first vertex. PDF /LE[0].

### `LineEndingEnd`

```csharp
LineEnding LineEndingEnd
```

Gets the line-ending style at the last vertex. PDF /LE[1].

---

_Source: [`src/Chuvadi.Pdf.Annotations/ShapeAnnotations.cs`](../../../src/Chuvadi.Pdf.Annotations/ShapeAnnotations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
