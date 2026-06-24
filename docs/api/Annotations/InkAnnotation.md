# InkAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Free-hand ink annotation (§12.5.6.13). Each stroke is a polyline of points in PDF user space.

```csharp
public sealed class InkAnnotation : PdfAnnotation
```

## Properties

### `Strokes`

```csharp
IReadOnlyList<IReadOnlyList<PointF>> Strokes
```

Gets the strokes. Each inner list is one continuous polyline of (X, Y) points in PDF user space.

---

_Source: [`src/Chuvadi.Pdf.Annotations/Annotations.cs`](../../../src/Chuvadi.Pdf.Annotations/Annotations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
