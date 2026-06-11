# TrueTypeLoader

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Loads a TrueType or OpenType font from raw bytes and provides access to glyph outlines and metrics.

```csharp
public sealed class TrueTypeLoader
```

## Remarks

Parses the following required tables: head (font header, unitsPerEm, loca format), hhea (numberOfHMetrics), maxp (numGlyphs), loca (glyph offsets), glyf (glyph contour data), hmtx (advance widths and left side bearings), cmap (character → glyph index mapping, format 4 preferred). Supports simple glyphs and composite glyphs (one level deep). Quadratic Bezier curves (TrueType) are converted to cubic for compatibility with the Graphics Path layer. OpenType specification — https://docs.microsoft.com/typography/opentype/spec/

## Constructors

### `TrueTypeLoader(byte[] fontData)`

Loads a font from raw TTF/OTF bytes.

**Parameters**

- `fontData` — The raw font file bytes. <exception cref="ArgumentNullException"> Thrown when `fontData` is null. </exception> <exception cref="FontRenderingException"> Thrown when the font data is invalid or missing required tables. </exception>

## Properties

### `UnitsPerEm`

```csharp
int UnitsPerEm => _unitsPerEm
```

Gets the number of font design units per em square.

### `NumGlyphs`

```csharp
int NumGlyphs => _numGlyphs
```

Gets the total number of glyphs in the font.

## Methods

### `GetGlyphIndex`

```csharp
int GetGlyphIndex(int codePoint)
```

Maps a Unicode code point to its glyph index. Returns 0 (the .notdef glyph) when the character is not present.

### `GetGlyphOutline`

```csharp
GlyphOutline GetGlyphOutline(int glyphId)
```

Extracts the outline and metrics for a glyph by glyph index. Returns an empty outline for whitespace or missing glyphs.

**Parameters**

- `glyphId` — Zero-based glyph index.

### `GetGlyphMetrics`

```csharp
GlyphMetrics GetGlyphMetrics(int glyphId)
```

Returns the typographic metrics for a glyph without building its path. Useful for text advance width calculations.

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/TrueTypeLoader.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/TrueTypeLoader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
