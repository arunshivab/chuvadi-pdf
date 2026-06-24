# TextWatermarkOptions

**Class** in `Chuvadi.Pdf.Watermark` (Watermark)

Options for stamping a text watermark onto PDF pages.

```csharp
public sealed class TextWatermarkOptions
```

## Constructors

### `TextWatermarkOptions(string text)`

Initialises a `TextWatermarkOptions` with required text.

**Parameters**

- `text` — The watermark text (e.g., "CONFIDENTIAL").

## Properties

### `Text`

```csharp
string Text
```

Gets or initialises the watermark text.

### `FontSize`

```csharp
double FontSize
```

Gets or initialises the font size in PDF points. Default: 48.

### `Color`

```csharp
ColorF Color
```

Gets or initialises the text colour. Default: 50% gray.

### `Opacity`

```csharp
float Opacity
```

Gets or initialises the opacity from 0 (transparent) to 1 (opaque). Default: 0.3.

### `RotationDegrees`

```csharp
double RotationDegrees
```

Gets or initialises the rotation angle in degrees (counter-clockwise). Default: 45 (diagonal).

### `FontName`

```csharp
string FontName
```

Gets or initialises the font name. When `FontData` is null this must be one of the 14 standard PDF fonts (the watermark is drawn with Helvetica metrics). When `FontData` is supplied this is recorded as the embedded font's PostScript base-font name. Default: Helvetica.

### `FontData`

```csharp
byte[]? FontData
```

Gets or initialises the bytes of a TrueType/OpenType (glyf-based) font to embed for this watermark. When null (the default) the watermark uses the standard Helvetica font. When supplied, the font is embedded as a composite Type0/CIDFontType2 font with Identity-H encoding, so watermark text in non-Latin scripts (e.g. Tamil, Devanagari) renders with the caller's own face. The text is emitted as glyphs in logical order without complex shaping (no GSUB/GPOS reordering).

### `PageIndices`

```csharp
int[]? PageIndices
```

Gets or initialises which pages to watermark. Null means all pages. Otherwise a zero-based page index set.

---

_Source: [`src/Chuvadi.Pdf.Watermark/TextWatermarkOptions.cs`](../../../src/Chuvadi.Pdf.Watermark/TextWatermarkOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
