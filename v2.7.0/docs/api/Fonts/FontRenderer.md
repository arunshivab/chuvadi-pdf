# FontRenderer

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

High-level API for extracting glyph outlines from a TrueType or OpenType font.

```csharp
public sealed class FontRenderer
```

## Remarks

`FontRenderer` wraps a `TrueTypeLoader` and provides convenient methods for text rendering pipelines: 
 
- Map a character to its glyph index via the font's cmap table. 
- Get the scaled glyph outline for a given point size. 
- Enumerate glyphs for a string with advance-width positioning.  Glyph outlines are cached after first access to avoid repeated parsing. The cache is per-`FontRenderer` instance and is not thread-safe.

## Constructors

### `FontRenderer(byte[] fontData)`

Initialises a `FontRenderer` from raw font bytes.

**Parameters**

- `fontData` — The raw TTF or OTF file bytes. <exception cref="ArgumentNullException"> Thrown when `fontData` is null. </exception> <exception cref="FontRenderingException"> Thrown when the font data is invalid or missing required tables. </exception>

## Properties

### `UnitsPerEm`

```csharp
int UnitsPerEm => _loader.UnitsPerEm
```

Gets the number of font design units per em square.

### `NumGlyphs`

```csharp
int NumGlyphs => _loader.NumGlyphs
```

Gets the total number of glyphs in the font.

## Methods

### `GetGlyphIndex`

```csharp
int GetGlyphIndex(int codePoint)
```

Maps a Unicode code point to its glyph index. Returns 0 (.notdef) when the character is not present in the font.

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
