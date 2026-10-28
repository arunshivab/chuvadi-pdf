# FontFace

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

One loaded font face — a TrueType (`glyf`) or CFF-based OpenType font, or one face of a collection (`.ttc`/`.otc`) — with its font-wide metrics and per-glyph measurements, for layout. Load it once and reuse it: every member reads tables parsed at load (glyph bounds are computed on first use and cached), and the object is safe to share across threads.

```csharp
public sealed class FontFace
```

## Remarks

All metrics are in font design units; divide by `UnitsPerEm` (or use `Scale`) to get ems or points. Signs follow the font tables: ascenders are positive, descenders negative — except `WinDescent`, which the OS/2 table stores as a positive distance below the baseline.

## Properties

### `FontProgram`

```csharp
byte[] FontProgram
```

Gets the face program as a standalone font file (for a face of a collection, the face's tables in a file of their own). Do not modify it.

### `FaceIndex`

```csharp
int FaceIndex
```

Gets the face's index in its collection (0 for a single font file).

### `IsCff`

```csharp
bool IsCff => _cff is not null
```

Gets a value indicating whether the outlines are CFF (PostScript) rather than TrueType.

### `FamilyName`

```csharp
string? FamilyName
```

Gets the typographic family name (name IDs 16 or 1), or null.

### `SubfamilyName`

```csharp
string? SubfamilyName
```

Gets the typographic subfamily (style) name (name IDs 17 or 2), or null.

### `FullName`

```csharp
string? FullName
```

Gets the full font name (name ID 4), or null.

### `PostScriptName`

```csharp
string? PostScriptName
```

Gets the PostScript name (name ID 6), or null.

### `WeightClass`

```csharp
int WeightClass
```

Gets the OS/2 weight class (400 regular, 700 bold).

### `IsBold`

```csharp
bool IsBold
```

Gets a value indicating whether the face is bold (OS/2 fsSelection, else head macStyle).

### `IsItalic`

```csharp
bool IsItalic
```

Gets a value indicating whether the face is italic (OS/2 fsSelection, else head macStyle).

### `IsFixedPitch`

```csharp
bool IsFixedPitch
```

Gets a value indicating whether every glyph has the same advance (post isFixedPitch).

### `UnitsPerEm`

```csharp
int UnitsPerEm
```

Gets the font design units per em (head).

### `GlyphCount`

```csharp
int GlyphCount
```

Gets the number of glyphs (maxp).

### `FontBounds`

```csharp
FontBox FontBounds
```

Gets the bounding box of all glyphs (head).

### `HheaAscender`

```csharp
int HheaAscender
```

Gets the hhea ascender.

### `HheaDescender`

```csharp
int HheaDescender
```

Gets the hhea descender (negative below the baseline).

### `HheaLineGap`

```csharp
int HheaLineGap
```

Gets the hhea line gap.

### `HasOs2Table`

```csharp
bool HasOs2Table
```

Gets a value indicating whether the font has an OS/2 table (the OS/2 values are 0 otherwise).

### `TypoAscender`

```csharp
int TypoAscender
```

Gets the OS/2 typographic ascender.

### `TypoDescender`

```csharp
int TypoDescender
```

Gets the OS/2 typographic descender (negative below the baseline).

### `TypoLineGap`

```csharp
int TypoLineGap
```

Gets the OS/2 typographic line gap.

### `WinAscent`

```csharp
int WinAscent
```

Gets the OS/2 Windows ascent.

### `WinDescent`

```csharp
int WinDescent
```

Gets the OS/2 Windows descent, as a positive distance below the baseline.

### `UseTypoMetrics`

```csharp
bool UseTypoMetrics
```

Gets a value indicating whether the font asks for its typographic metrics to be used for line spacing (OS/2 fsSelection bit 7, USE_TYPO_METRICS).

### `Ascender`

```csharp
int Ascender => UseTypoMetrics ? TypoAscender : HheaAscender
```

Gets the ascender the font asks layout to use: the typographic ascender when `UseTypoMetrics` is set, else the hhea one. Applications that match a particular program's line spacing should pick from the hhea, typographic and Windows values themselves.

### `Descender`

```csharp
int Descender => UseTypoMetrics ? TypoDescender : HheaDescender
```

Gets the descender chosen like `Ascender` (negative below the baseline).

### `LineGap`

```csharp
int LineGap => UseTypoMetrics ? TypoLineGap : HheaLineGap
```

Gets the line gap chosen like `Ascender`.

### `CapHeight`

```csharp
int CapHeight
```

Gets the cap height (OS/2 version 2 or later; otherwise the top of the glyph for "H").

### `XHeight`

```csharp
int XHeight
```

Gets the x-height (OS/2 version 2 or later; otherwise the top of the glyph for "x").

### `UnderlinePosition`

```csharp
int UnderlinePosition
```

Gets the underline position (post; negative below the baseline).

### `UnderlineThickness`

```csharp
int UnderlineThickness
```

Gets the underline thickness (post).

### `StrikeoutPosition`

```csharp
int StrikeoutPosition
```

Gets the strikeout position above the baseline (OS/2).

### `StrikeoutSize`

```csharp
int StrikeoutSize
```

Gets the strikeout thickness (OS/2).

### `ItalicAngle`

```csharp
double ItalicAngle
```

Gets the italic angle in degrees counter-clockwise from vertical (post).

### `CharacterMap`

```csharp
IReadOnlyDictionary<int, int> CharacterMap => _loader.CharacterMap
```

Gets the face's Unicode character map: every code point the face maps, to its glyph id.

## Methods

### `GetFaceCount`

__static__

```csharp
static int GetFaceCount(byte[] fontData)
```

Returns the number of faces in a font file: the count of a collection (`.ttc`/`.otc`), or 1 for a single font.

**Parameters**

- `fontData` — The font file.

**Returns:** The number of faces. <exception cref="ArgumentNullException">`fontData` is null.</exception> <exception cref="FontRenderingException">The data is not a TrueType/OpenType font or collection.</exception>

### `Load`

__static__

```csharp
static FontFace Load(byte[] fontData, int faceIndex = 0)
```

Loads a face from a font file.

**Parameters**

- `fontData` — A TrueType or OpenType font, or a collection.
- `faceIndex` — The face to load from a collection (0 for a single font).

**Returns:** The loaded face. <exception cref="ArgumentNullException">`fontData` is null.</exception> <exception cref="ArgumentOutOfRangeException">`faceIndex` is not a face of the file.</exception> <exception cref="FontRenderingException">The font data is invalid or unsupported.</exception>

### `Scale`

```csharp
double Scale(double designUnits, double fontSize) => designUnits * fontSize / UnitsPerEm
```

Converts a value in design units to points at a font size.

**Parameters**

- `designUnits` — The value in font design units.
- `fontSize` — The font size in points.

**Returns:** The value in points.

### `GetGlyphIndex`

```csharp
int GetGlyphIndex(int codePoint) => _loader.GetGlyphIndex(codePoint)
```

Returns the glyph for a Unicode code point, or 0 (.notdef) when the face has none.

**Parameters**

- `codePoint` — The Unicode code point.

**Returns:** The glyph id.

### `HasGlyph`

```csharp
bool HasGlyph(int codePoint) => GetGlyphIndex(codePoint) > 0
```

Returns whether the face maps the code point to a glyph.

**Parameters**

- `codePoint` — The Unicode code point.

**Returns:** True when a glyph other than .notdef is mapped.

### `GetAdvanceWidth`

```csharp
int GetAdvanceWidth(int glyphId)
```

Returns a glyph's advance width (hmtx).

**Parameters**

- `glyphId` — The glyph id.

**Returns:** The advance in design units; 0 for an id outside the font.

### `GetLeftSideBearing`

```csharp
int GetLeftSideBearing(int glyphId)
```

Returns a glyph's left side bearing (hmtx).

**Parameters**

- `glyphId` — The glyph id.

**Returns:** The bearing in design units; 0 for an id outside the font.

### `GetGlyphBounds`

```csharp
FontBox GetGlyphBounds(int glyphId)
```

Returns a glyph's bounding box: the TrueType glyph header, or the box of a CFF glyph's outline points. Empty glyphs (a space) give an all-zero box.

**Parameters**

- `glyphId` — The glyph id.

**Returns:** The box in design units.

### `MeasureText`

```csharp
int MeasureText(string text)
```

Returns the advance of a string with one glyph per code point, without shaping or kerning (use `TextShaper.Shape` with this face for shaped, kerned runs).

**Parameters**

- `text` — The text.

**Returns:** The total advance in design units.

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/FontFace.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/FontFace.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
