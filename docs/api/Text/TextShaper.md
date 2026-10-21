# TextShaper

**Class** in `Chuvadi.Pdf.Text.Shaping` (Text)

Shapes a run of text using the OpenType GSUB and GPOS tables embedded in a TrueType/OpenType font. Returns a sequence of positioned glyphs in visual order.

```csharp
public static class TextShaper
```

## Methods

### `Shape`

__static__

```csharp
static IReadOnlyList<ShapedGlyph> Shape(byte[] ttf, string text, LipiScript script, ShapingFeatures? features = null)
```

Shapes a run of text using the specified font and script.

**Parameters**

- `ttf` — Raw TrueType/OpenType font bytes (sfnt).
- `text` — The text to shape. Must be non-empty.
- `script` — The script of the text run.
- `features` — Feature set to apply. Defaults to `ShapingFeatures.Default` when null.

**Returns:** The shaped glyphs in visual order, with advances and offsets in 1000ths of an em. <exception cref="ArgumentNullException"> `ttf` or `text` is null. </exception> <exception cref="ArgumentException">`text` is empty.</exception>

---

_Source: [`src/Chuvadi.Pdf.Text.Shaping/TextShaper.cs`](../../../src/Chuvadi.Pdf.Text.Shaping/TextShaper.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
