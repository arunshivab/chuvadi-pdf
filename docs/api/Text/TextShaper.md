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

### `Shape`

__static__

```csharp
static IReadOnlyList<ShapedGlyph> Shape(FontFace face, string text, LipiScript script, ShapingFeatures? features = null)
```

Shapes a run of text with a loaded `FontFace`. The face's tables are parsed once and the GSUB/GPOS feature lookups for each script are cached with the face, so repeated calls (a line-fitting loop) do no font parsing. Results equal `Shape(byte[], string, LipiScript, ShapingFeatures?)` for the same font program. Safe to call concurrently with one face.

**Parameters**

- `face` — The loaded face (TrueType or CFF outlines; a collection face works too).
- `text` — The text to shape. Must be non-empty.
- `script` — The script of the text run.
- `features` — Feature set to apply. Defaults to `ShapingFeatures.Default` when null.

**Returns:** The shaped glyphs in visual order, with advances and offsets in 1000ths of an em. <exception cref="ArgumentNullException"> `face` or `text` is null. </exception> <exception cref="ArgumentException">`text` is empty.</exception>

---

_Source: [`src/Chuvadi.Pdf.Text.Shaping/TextShaper.cs`](../../../src/Chuvadi.Pdf.Text.Shaping/TextShaper.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
