# Type3Glyph

**Record** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

A single Type 3 glyph: its content stream and glyph-space width.

```csharp
public readonly record struct Type3Glyph(byte[] Content, double Width)
```

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/Type3Font.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/Type3Font.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
