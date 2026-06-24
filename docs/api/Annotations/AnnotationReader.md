# AnnotationReader

**Class** in `Chuvadi.Pdf.Annotations` (Annotations)

Reads annotations from a PDF document.

```csharp
public static class AnnotationReader
```

## Remarks

For each page's `/Annots` array, resolves every entry and decodes the subtype into one of the modelled `PdfAnnotation` derivatives. Subtypes not modelled by Chuvadi are returned as `GenericAnnotation` with their raw `/Subtype` name preserved.

## Methods

### `GetAnnotations`

__static__

```csharp
static IReadOnlyList<PdfAnnotation> GetAnnotations(PdfDocument document, int pageIndex)
```

Returns all annotations on the given page. Empty when the page has no `/Annots` entry.

### `GetAllAnnotations`

__static__

```csharp
static IReadOnlyList<PdfAnnotation> GetAllAnnotations(PdfDocument document)
```

Returns annotations from every page in the document, in page order.

---

_Source: [`src/Chuvadi.Pdf.Annotations/AnnotationReader.cs`](../../../src/Chuvadi.Pdf.Annotations/AnnotationReader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
