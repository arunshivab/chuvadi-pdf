# Standard14Widths

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Exact per-glyph advance-width metrics for the PDF Standard 14 fonts in 1/1000-em font design units.

```csharp
public static class Standard14Widths
```

## Remarks

Used by `RenderableFont` (and, through `Standard14GlyphWidths`, the display-list width resolver) as the width source for Standard 14 fonts when a PDF font dictionary omits its /Widths array — as §9.6.2.2 permits for these fonts. Works even when `Standard14Outlines.BundleAvailable` is false — in that case glyphs cannot be drawn, but layout and selection still produce correct positions for the reader-app text layer.  

 The twelve text fonts are indexed by WinAnsi (cp1252) character code; Symbol and ZapfDingbats are indexed by their built-in encodings. Codes with no glyph fall back to a per-font average so a stray byte never collapses layout.  

 Units: 1/1000 of an em square. To convert the returned value to PDF user-space points for a given `pointSize`, multiply by `pointSize / 1000.0`.

## Methods

### `GetWidth`

__static__

```csharp
static int GetWidth(string fontName, int charCode)
```

Returns the advance width of `charCode` in `fontName` in 1/1000 em units.

**Parameters**

- `fontName` — A Standard 14 PostScript font name (e.g. "Helvetica").
- `charCode` — A WinAnsi character code (0–255) for the text fonts, or the built-in encoding code for Symbol and ZapfDingbats.

**Returns:** The exact Adobe AFM advance width in 1/1000 em, or the per-font average when the code has no glyph. For non-Standard 14 fonts returns the em-half default of 500. <exception cref="ArgumentNullException"> Thrown when `fontName` is null. </exception>

### `IsStandard14`

__static__

```csharp
static bool IsStandard14(string fontName)
```

Returns true when `fontName` matches one of the 14 Standard PostScript font names. <exception cref="ArgumentNullException"> Thrown when `fontName` is null. </exception>

## Fields

### `UnitsPerEm`

```csharp
const int UnitsPerEm = 1000
```

The units-per-em value used by all Standard 14 widths (1000).

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/Standard14Widths.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/Standard14Widths.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
