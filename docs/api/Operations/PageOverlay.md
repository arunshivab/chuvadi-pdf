# PageOverlay

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Recolours existing pages by drawing a solid background fill behind the page content and/or rendering the existing content at reduced opacity. The page's content is wrapped in a form XObject and painted under an ExtGState constant-alpha (`/ca`), so 0 opacity yields a blank (optionally coloured) page and 1 leaves content fully opaque. PDF 32000-1:2008 §8.10.1 (form XObjects), §11.6.4.4 (constant alpha).

```csharp
public static class PageOverlay
```

---

_Source: [`src/Chuvadi.Pdf.Operations/PageOverlay.cs`](../../../src/Chuvadi.Pdf.Operations/PageOverlay.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
