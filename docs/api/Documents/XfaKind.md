# XfaKind

**Enum** in `Chuvadi.Pdf.Documents` (Documents)

Classifies how a document uses XFA (XML Forms Architecture), so a consumer can tell forms that render from the page content apart from dynamic XFA that needs a dedicated processor and may otherwise appear blank. PDF 32000-1:2008 §12.7.8 (XFA), §7.7.2 (catalog `/NeedsRendering`).

```csharp
public enum XfaKind
```

## Values

| Name | Description |
|---|---|
| `None` | The document has no XFA form. |
| `Static` | Static XFA: an `/XFA` entry is present and the form has a fixed layout that renders from the page content, with no traditional AcroForm fields alongside it. |
| `Hybrid` | Hybrid XFA: an `/XFA` entry is present alongside traditional AcroForm fields, so the form also renders in viewers that do not process XFA. |
| `Dynamic` | Dynamic XFA: the catalog requests rendering (`/NeedsRendering true`); the form's layout is produced by an XFA processor and the page content may be blank without it. |

---

_Source: [`src/Chuvadi.Pdf.Documents/XfaKind.cs`](../../../src/Chuvadi.Pdf.Documents/XfaKind.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
