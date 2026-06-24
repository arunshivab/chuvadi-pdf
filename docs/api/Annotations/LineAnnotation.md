# LineAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Line annotation. PDF 32000-1:2008 §12.5.6.7.

```csharp
public sealed class LineAnnotation : PdfAnnotation
```

## Remarks

The line runs from `Start` to `End`, both in PDF user-space coordinates. The annotation `PdfAnnotation.Rect` must enclose both endpoints.

## Properties

### `Start`

```csharp
PointF Start
```

Gets the start point of the line (PDF /L entry, first pair).

### `End`

```csharp
PointF End
```

Gets the end point of the line (PDF /L entry, second pair).

### `BorderStyle`

```csharp
BorderStyle? BorderStyle
```

Gets the border style, or null for the viewer default.

### `LineEndingStart`

```csharp
LineEnding LineEndingStart
```

Gets the line-ending style at `Start`. PDF /LE[0].

### `LineEndingEnd`

```csharp
LineEnding LineEndingEnd
```

Gets the line-ending style at `End`. PDF /LE[1].

---

_Source: [`src/Chuvadi.Pdf.Annotations/ShapeAnnotations.cs`](../../../src/Chuvadi.Pdf.Annotations/ShapeAnnotations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
