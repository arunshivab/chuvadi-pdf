# PageStamper

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Stamps a source page onto one or more existing pages of a target document under an affine transform, preserving the rest of the document. The source page is imported once as a form XObject and reused across target pages, so stamping a logo or letterhead onto every page is cheap. Existing content is isolated in its own graphics-state scope so stamps are unaffected by it.

```csharp
public static class PageStamper
```

---

_Source: [`src/Chuvadi.Pdf.Operations/PageStamper.cs`](../../../src/Chuvadi.Pdf.Operations/PageStamper.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
