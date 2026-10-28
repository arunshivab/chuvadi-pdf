# GlyphRunStyle

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Text-matrix and render-mode options for a shaped glyph run: character scaling, a synthetic italic slant and a synthetic bold stroke.

```csharp
public sealed class GlyphRunStyle
```

## Properties

### `HorizontalScale`

```csharp
double HorizontalScale
```

Gets the horizontal scale (1 = 100%). Glyphs are drawn this much wider or narrower, and the run's advances and x offsets are multiplied by it too (as Word's character scaling and PDF's `Tz` do), so pass the shaper's unscaled advances. Must be positive. Default 1.

### `Skew`

```csharp
double Skew
```

Gets the slant: the tangent of the lean angle (the text matrix's `c`). Positive leans right; 0.2 (about 11°) is a usual synthetic italic. Default 0.

### `StrokeWidth`

```csharp
double StrokeWidth
```

Gets the outline stroke width in points for synthetic bold. When positive, glyphs are filled and then stroked in the fill colour (render mode 2). Default 0 (fill only).

---

_Source: [`src/Chuvadi.Pdf.Authoring/GlyphRunStyle.cs`](../../../src/Chuvadi.Pdf.Authoring/GlyphRunStyle.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
