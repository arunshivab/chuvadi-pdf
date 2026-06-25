# AnnotationFlattener

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Flattens annotations and AcroForm field widgets by baking each annotation's normal appearance stream (`/AP /N`) into the page content as a form XObject, then removing the live annotation. The output looks identical but is static and no longer editable.

```csharp
public static class AnnotationFlattener
```

## Remarks

Each baked appearance is placed per ISO 32000-1 §12.5.5: the appearance's `/BBox` (transformed by its `/Matrix`) is mapped onto the annotation's `/Rect`. Existing page content is preserved byte-for-byte and wrapped in a balanced `q … Q` so the baked appearances draw at the page's initial coordinate system. Annotations that cannot be baked (no appearance, no `/BBox`, or an indeterminate appearance state) are left live unless `AnnotationFlattenOptions.DropRemainingAnnotations` is set.

## Methods

### `Flatten`

__static__

```csharp
static void Flatten(Stream output, PdfDocument document) => Flatten(output, document, AnnotationFlattenOptions.Default)
```

Flattens the document with `AnnotationFlattenOptions.Default` and writes the result.

### `Flatten`

__static__

```csharp
static void Flatten(Stream output, PdfDocument document, AnnotationFlattenOptions options)
```

Flattens the document using `options` and writes the result to `output`.

---

_Source: [`src/Chuvadi.Pdf.Operations/AnnotationFlattener.cs`](../../../src/Chuvadi.Pdf.Operations/AnnotationFlattener.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
