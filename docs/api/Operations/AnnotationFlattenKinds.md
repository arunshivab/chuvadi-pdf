# AnnotationFlattenKinds

**Enum** in `Chuvadi.Pdf.Operations` (Operations)

```csharp
public enum AnnotationFlattenKinds
```

## Values

| Name | Description |
|---|---|
| `None` | Flatten nothing. |
| `FormFields` | Flatten AcroForm field widgets (`/Subtype /Widget`). |
| `Markup` | Flatten every non-widget annotation subtype (markup, stamp, ink, …). |
| `All` | Flatten both form-field widgets and markup annotations. |

---

_Source: [`src/Chuvadi.Pdf.Operations/AnnotationFlattenKinds.cs`](../../../src/Chuvadi.Pdf.Operations/AnnotationFlattenKinds.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
