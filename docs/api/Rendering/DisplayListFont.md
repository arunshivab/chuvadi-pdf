# DisplayListFont

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

A font used by text on a page, as the structured display list sees it: one shared instance per font dictionary, referenced from every `TextOp` drawn with it. Resolved through the same resources as the text (page, form, annotation appearance, pattern or Type 3 glyph), so the same resource name in two places gives two different objects when it names two fonts.

```csharp
public sealed class DisplayListFont
```

## Remarks

It gives a vector consumer what it needs to embed or substitute the font: the program bytes and their kind, the Type 0 details and, for a font the PDF does not embed, the substitute the raster renderer draws it with. It does not convert programs to other formats.

## Properties

### `Dictionary`

```csharp
PdfDictionary Dictionary
```

The font dictionary.

### `Subtype`

```csharp
string Subtype
```

The `/Subtype`: Type1, MMType1, TrueType, Type0 or Type3.

### `BaseFont`

```csharp
string BaseFont
```

The `/BaseFont` name, including any subset prefix; empty when absent.

### `EncodingName`

```csharp
string? EncodingName
```

The `/Encoding` when it is a name (for example "WinAnsiEncoding" or "Identity-H"); null otherwise.

### `Descriptor`

```csharp
PdfDictionary? Descriptor
```

The font descriptor (of the descendant font for Type 0), or null.

### `IsEmbedded`

```csharp
bool IsEmbedded
```

True when the PDF embeds the font program.

### `ProgramKind`

```csharp
FontProgramKind ProgramKind
```

The kind of the embedded program, or `FontProgramKind.None`.

### `DescendantFont`

```csharp
PdfDictionary? DescendantFont
```

For a Type 0 font: the descendant CIDFont dictionary; otherwise null.

### `CidSystemInfo`

```csharp
CidSystemInfo? CidSystemInfo
```

For a Type 0 font: the descendant's `/CIDSystemInfo`; otherwise null.

### `CidWidths`

```csharp
IReadOnlyDictionary<int, double> CidWidths
```

For a Type 0 font: the descendant's `/W` widths by CID, in thousandths of an em; otherwise empty.

### `DefaultCidWidth`

```csharp
double DefaultCidWidth
```

For a Type 0 font: the descendant's `/DW` (1000 when absent); otherwise 0.

### `Substitute`

```csharp
string? Substitute
```

For a font the PDF does not embed: the standard 14 font the raster renderer draws it with (chosen from the name, flags, `/FontWeight` and `/ItalicAngle`); null for an embedded font or a Type 3 font. Indic text in such a font is drawn by the raster renderer with the LiPi Sans face of its script from Chuvadi.Fonts, Bold when this name is bold.

### `UnitsPerEm`

```csharp
int UnitsPerEm
```

The program's units per em: from the TrueType `head` table or the CFF font matrix; 1000 for Type 1, Type 3 and fonts that are not embedded.

### `EmbeddingRights`

```csharp
FontEmbeddingRights EmbeddingRights
```

The embedding rights from the program's `OS/2` `fsType` (TrueType and OpenType programs); `FontEmbeddingPermission.Unknown` otherwise.

## Methods

### `GetProgram`

```csharp
byte[]? GetProgram()
```

Returns the decoded program bytes (read once and cached), or null when the font is not embedded or the stream cannot be decoded.

**Returns:** The program, or null. The array is shared: do not change it.

### `GetCidToGidMap`

```csharp
int[]? GetCidToGidMap()
```

For a Type 0 font with a `/CIDToGIDMap` stream: the glyph id of each CID (index = CID). Null when the map is `/Identity`, absent, or the font is not a CIDFontType2.

**Returns:** The map, or null. The array is shared: do not change it.

### `GetWidth`

```csharp
double GetWidth(int code)
```

The width of a character code in thousandths of an em: `/Widths` (with `/FirstChar`, else `/MissingWidth`) for a simple font, `/W` and `/DW` for a Type 0 font, and the standard 14 metrics for a standard 14 font without `/Widths`.

**Parameters**

- `code` — The character code (a CID for Type 0 fonts).

**Returns:** The width.

### `GetGlyphId`

```csharp
int GetGlyphId(int code)
```

The glyph a character code selects in the embedded program, by the same rules the raster renderer uses: for a Type 0 font, the CID's glyph (through `/CIDToGIDMap` or the CFF charset); for a simple TrueType, OpenType or CFF font, the encoding and `cmap` rules of §9.6.6.4; for a Type 1 font, the code itself (`GetGlyphOutline` takes codes for Type 1). Returns -1 when the font is not embedded or the program cannot be read, and 0 (.notdef) when the code has no glyph.

**Parameters**

- `code` — The character code (a CID for Type 0 fonts).

**Returns:** The glyph id, 0, or -1.

### `GetGlyphOutline`

```csharp
GlyphOutline? GetGlyphOutline(int glyphId)
```

The outline of a glyph in the embedded program, in the program's own units (`UnitsPerEm`), or null when the font is not embedded or the glyph cannot be read. For a Type 1 font, pass the character code.

**Parameters**

- `glyphId` — The glyph id from `GetGlyphId`.

**Returns:** The outline, or null.

### `GetSubstituteGlyphOutline`

```csharp
Chuvadi.Pdf.Graphics.Path? GetSubstituteGlyphOutline(int code)
```

For a font the PDF does not embed: the outline of a character code in `Substitute`, the standard 14 font the raster renderer draws it with, in a 1000-unit em. Null when there is no substitute or no glyph.

**Parameters**

- `code` — The character code.

**Returns:** The outline, or null.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListFont.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListFont.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
