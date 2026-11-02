# RenderableFont

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

A PDF font that supports both text decoding (character codes to Unicode) and glyph rendering (character codes to vector outlines + metrics).

```csharp
public sealed class RenderableFont
```

## Remarks

`RenderableFont` is the public surface that the v2 reader-app pipeline will use to draw text. It composes `PdfFont` (text decoding) with a glyph outline source that varies by font kind:  
 
- <b>Standard 14 fonts</b>: glyph outlines come from `Standard14Outlines` (an embedded resource bundle that ships independent of any host font) and widths come from `Standard14Widths`. 
- <b>Other fonts</b>: in v2.0.0 R1 D3b this returns empty paths and approximate half-em widths. R1 D3c will add embedded-font-program support so that fonts with a FontFile / FontFile2 / FontFile3 stream render their real outlines.  

 `RenderableFont` is immutable after construction. Glyph outlines are produced on demand and are not cached by this type; the underlying `Standard14Outlines` lazily loads the bundle once per process.

## Properties

### `FontName`

```csharp
string FontName
```

Gets the font's PostScript name with any subset prefix removed.

### `IsStandard14`

```csharp
bool IsStandard14
```

Gets whether this font is one of the 14 standard PDF base fonts.

### `UnitsPerEm`

```csharp
int UnitsPerEm
```

Gets the font's units-per-em value (1000 for Standard 14, otherwise the value reported by the embedded font program if any, else 1000).

## Methods

### `IsStandard14Name`

__static__

```csharp
static bool IsStandard14Name(string fontName)
```

Returns true when `fontName` matches one of the 14 Standard PostScript font names. <exception cref="ArgumentNullException"> Thrown when `fontName` is null. </exception>

### `FromDictionary`

__static__

```csharp
static RenderableFont FromDictionary(PdfDictionary fontDict, IPdfObjectResolver resolver)
```

Builds a `RenderableFont` from a font dictionary.

**Parameters**

- `fontDict` — The font dictionary from the page Resources.
- `resolver` — Used to resolve indirect object references. <exception cref="ArgumentNullException"> Thrown when `fontDict` or `resolver` is null. </exception>

### `AsSubstitute`

__static__

```csharp
static RenderableFont AsSubstitute(PdfDictionary fontDict, IPdfObjectResolver resolver, string standard14Name)
```

Builds a `RenderableFont` that draws a font the PDF does not embed with a standard 14 font's outlines (since 3.30): characters are decoded through the font's own encoding and ToUnicode map, and glyph shapes come from `standard14Name` (see `Standard14Substitute`).

**Parameters**

- `fontDict` — The font dictionary from the page Resources.
- `resolver` — Used to resolve indirect object references.
- `standard14Name` — The standard 14 font whose outlines to use.

**Returns:** A font that reports `IsStandard14` as true. <exception cref="ArgumentNullException">An argument is null.</exception> <exception cref="ArgumentException">`standard14Name` is not a standard 14 font name.</exception>

### `Default`

__static__

```csharp
static RenderableFont Default()
```

Returns a default `RenderableFont` equivalent to Helvetica with WinAnsi encoding. Used when no font dictionary is available.

### `GetGlyphPath`

```csharp
Path GetGlyphPath(int charCode)
```

Returns the glyph outline for `charCode` in unscaled font design units (Y up, origin at the glyph anchor).

**Remarks:** Returns an empty path when: 
 
- The character is not present in the font. 
- The font is Standard 14 but the outline bundle has not been built (see `Standard14Outlines.BundleAvailable`). 
- The font is non-Standard 14 (embedded font support arrives in D3c).

### `GetGlyphPath`

```csharp
Path GetGlyphPath(int charCode, double pointSize)
```

Returns the glyph outline for `charCode` scaled to `pointSize` PDF points. <exception cref="ArgumentOutOfRangeException"> Thrown when `pointSize` is not positive. </exception>

### `GetAdvanceWidth`

```csharp
double GetAdvanceWidth(int charCode, double pointSize)
```

Returns the advance width of `charCode` in PDF points at `pointSize`. <exception cref="ArgumentOutOfRangeException"> Thrown when `pointSize` is not positive. </exception>

### `DecodeText`

```csharp
string DecodeText(byte[] bytes)
```

Decodes raw bytes from a text-showing operator (Tj, TJ, ', ") to a Unicode string. Delegates to the wrapped `PdfFont`. <exception cref="ArgumentNullException"> Thrown when `bytes` is null. </exception>

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/RenderableFont.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/RenderableFont.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
