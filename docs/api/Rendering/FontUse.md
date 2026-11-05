# FontUse

**Record** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

A font that is not embedded, and where it is used.

```csharp
public sealed record FontUse(int PageNumber, string Location, string ResourceName, string BaseFont, string Subtype)
```

## Parameters

- `PageNumber` — The 1-based page.
- `Location` — Where: "page", "form XObject 12 0 R", "pattern …", "Type 3 font …" or "annotation …".
- `ResourceName` — The font's resource name, for example "F1".
- `BaseFont` — The `/BaseFont` name.
- `Subtype` — The font `/Subtype`.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/FontEmbeddingReport.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/FontEmbeddingReport.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
