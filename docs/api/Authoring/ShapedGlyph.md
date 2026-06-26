# ShapedGlyph

**Record** in `Chuvadi.Pdf.Authoring` (Authoring)

One glyph of a pre-shaped run, as produced by an external shaper. Advances and offsets are in 1000-units-per-em text space.

```csharp
public readonly record struct ShapedGlyph(int GlyphId, double XAdvance, double XOffset, double YOffset, int Cluster)
```

---

_Source: [`src/Chuvadi.Pdf.Authoring/ShapedGlyph.cs`](../../../src/Chuvadi.Pdf.Authoring/ShapedGlyph.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
