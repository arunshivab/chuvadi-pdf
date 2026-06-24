# AnnotationWriter

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Adds new annotations to a PDF document and writes the result.

```csharp
public static class AnnotationWriter
```

## Remarks

For each annotation, builds the corresponding PDF dictionary, appends an indirect-object reference to each targeted page's `/Annots` array, and writes the modified document. The original document is not changed.

---

_Source: [`src/Chuvadi.Pdf.Annotations/AnnotationWriter.cs`](../../../src/Chuvadi.Pdf.Annotations/AnnotationWriter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
