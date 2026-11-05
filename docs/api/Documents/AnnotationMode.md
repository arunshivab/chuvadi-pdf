# AnnotationMode

**Enum** in `Chuvadi.Pdf.Documents` (Documents)

Which annotations a renderer or text extractor includes with a page.

```csharp
public enum AnnotationMode
```

## Values

| Name | Description |
|---|---|
| `View` | As a viewer shows the page on screen (the default): every annotation except Hidden, NoView and Popup annotations. |
| `Print` | As the page prints: only annotations with the Print flag, except Hidden and Popup annotations. NoView is ignored, so print-only stamps appear. |
| `FormFieldsOnly` | Form fields only, as a print dialog's "form fields only": widget annotations under the `Print` rules, without the page's own content and without other annotations. |
| `None` | The page content without any annotations. |

---

_Source: [`src/Chuvadi.Pdf.Documents/AnnotationMode.cs`](../../../src/Chuvadi.Pdf.Documents/AnnotationMode.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
