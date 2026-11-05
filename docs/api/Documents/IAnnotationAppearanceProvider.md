# IAnnotationAppearanceProvider

**Interface** in `Chuvadi.Pdf.Documents` (Documents)

Supplies an appearance stream for an annotation when the file's own is missing or out of date (a form field when `/NeedAppearances` is true, or an annotation with no `/AP`). The stream lives in memory only; the file is never changed. `Chuvadi.Pdf.Annotations` provides the implementation the renderers use.

```csharp
public interface IAnnotationAppearanceProvider
```

---

_Source: [`src/Chuvadi.Pdf.Documents/IAnnotationAppearanceProvider.cs`](../../../src/Chuvadi.Pdf.Documents/IAnnotationAppearanceProvider.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
