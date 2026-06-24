# BorderStyleType

**Enum** in `Chuvadi.Pdf.Annotations` (Annotations)

PDF border-style kind. PDF 32000-1:2008 §12.5.4, Table 166 — /S entry.

```csharp
public enum BorderStyleType
```

## Values

| Name | Description |
|---|---|
| `Solid` | Solid border (PDF /S = S, the default). |
| `Dashed` | Dashed border (PDF /S = D). |
| `Beveled` | Beveled border, raised appearance (PDF /S = B). |
| `Inset` | Inset border, recessed appearance (PDF /S = I). |
| `Underline` | Underline border, single line below (PDF /S = U). |

---

_Source: [`src/Chuvadi.Pdf.Annotations/BorderStyle.cs`](../../../src/Chuvadi.Pdf.Annotations/BorderStyle.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
