# ShapedGlyph

**Record** in `Chuvadi.Pdf.Text.Shaping` (Text)

One glyph of a pre-shaped run, as produced by an external shaper or by `TextShaper`. Advances and offsets are in 1000-units-per-em text space (1000 = one em), so device values scale by size/1000.

```csharp
public readonly record struct ShapedGlyph(int GlyphId, double XAdvance, double XOffset, double YOffset, int Cluster)
```

---

_Source: [`src/Chuvadi.Pdf.Text.Shaping/ShapedGlyph.cs`](../../../src/Chuvadi.Pdf.Text.Shaping/ShapedGlyph.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
