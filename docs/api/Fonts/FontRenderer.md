# FontRenderer

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

High-level API for extracting glyph outlines from a TrueType, OpenType, or CFF (Type 1C) font.

```csharp
public sealed class FontRenderer
```

## Remarks

`FontRenderer` wraps either a `TrueTypeLoader` (for `glyf`-based TrueType programs) or a `CffLoader` (for bare CFF / Type 1C programs and OpenType fonts whose outlines live in a `CFF ` table) and provides convenient methods for text rendering pipelines: 
 
- Map a character to its glyph index via the font's cmap (TrueType) or a charset-derived Unicode map (CFF). 
- Get the scaled glyph outline for a given point size. 
- Enumerate glyphs for a string with advance-width positioning.  The embedded program format is detected from the raw bytes: a bare CFF program (`/FontFile3` subtype `Type1C`) and an OpenType font whose only outline table is `CFF ` are both routed to `CffLoader`; everything else is parsed as TrueType. CFF programs have no hinting bytecode, so `GetHintedGlyphOutline` returns `null` for them and callers fall back to the scaled unhinted outline. Glyph outlines are cached after first access to avoid repeated parsing. The cache is per-`FontRenderer` instance and is not thread-safe.

## Constructors

### `FontRenderer(byte[] fontData)`

Initialises a `FontRenderer` from raw font bytes.

**Parameters**

- `fontData` — The raw TrueType, OpenType, or CFF program bytes. <exception cref="ArgumentNullException"> Thrown when `fontData` is null. </exception> <exception cref="FontRenderingException"> Thrown when the font data is invalid or missing required tables. </exception>

## Properties

### `UnitsPerEm`

```csharp
int UnitsPerEm => _cff?.UnitsPerEm ?? Loader.UnitsPerEm
```

Gets the number of font design units per em square.

### `NumGlyphs`

```csharp
int NumGlyphs => _cff?.NumGlyphs ?? Loader.NumGlyphs
```

Gets the total number of glyphs in the font.

## Methods

### `GetGlyphIndex`

```csharp
int GetGlyphIndex(int codePoint)
```

Maps a Unicode code point to its glyph index. Returns 0 (.notdef) when the character is not present in the font.

### `GetGlyphIndexForCode`

```csharp
int GetGlyphIndexForCode(int code, bool symbolic)
```

Resolves a raw character code (from a content-stream string) to a glyph index, honouring symbol and Macintosh cmaps and a direct code-as-index fallback. Use for simple fonts where the code, not a Unicode value, selects the glyph. Returns 0 (.notdef) when nothing matches.

**Remarks:** CFF programs carry no cmap; their glyphs are selected by name (and hence by Unicode via the charset), so this method returns 0 for CFF fonts and callers resolve the glyph through `GetGlyphIndexUnicode`.

### `GetGlyphIndexUnicode`

```csharp
int GetGlyphIndexUnicode(int codePoint)
```

Maps a Unicode code point to a glyph index via the Unicode cmap (TrueType) or charset map (CFF).

### `GetGlyphOutline`

```csharp
GlyphOutline GetGlyphOutline(int glyphId)
```

Gets the glyph outline for a glyph index, in font design units (unscaled). Results are cached after first access.

### `GetGlyphOutlineForChar`

```csharp
GlyphOutline GetGlyphOutlineForChar(char c)
```

Gets the glyph outline for a Unicode code point, in font design units. Returns the .notdef glyph when the character is not present.

### `GetScaledGlyphOutline`

```csharp
GlyphOutline GetScaledGlyphOutline(int glyphId, double pointSize)
```

Gets the glyph outline for a glyph index, scaled to the given point size.

### `List<`

```csharp
List<(double X, GlyphOutline Glyph)> LayoutText(string text, double pointSize)
```

Returns an ordered list of positioned glyph outlines for a string of text, scaled to the given point size. Each entry includes the glyph and its X origin (in PDF points, starting from 0).

**Parameters**

- `text` — The text to lay out.
- `pointSize` — The target size in PDF points.

**Returns:** A list of (x, GlyphOutline) pairs in visual order.

### `MeasureText`

```csharp
double MeasureText(string text, double pointSize)
```

Measures the total advance width of a string in PDF points.

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/FontRenderer.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/FontRenderer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
