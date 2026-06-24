# MarkupAnnotation

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Text-markup annotation (§12.5.6.10): Highlight, Underline, Squiggly, or StrikeOut. Distinguished by `PdfAnnotation.Type`.

```csharp
public sealed class MarkupAnnotation : PdfAnnotation
```

## Properties

### `QuadPoints`

```csharp
IReadOnlyList<float> QuadPoints
```

Gets the quad-point list. Each group of 8 floats defines a quadrilateral in the order (x1,y1)…(x4,y4). PDF 32000-1:2008 §12.5.6.10.

---

_Source: [`src/Chuvadi.Pdf.Annotations/Annotations.cs`](../../../src/Chuvadi.Pdf.Annotations/Annotations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
