# PdfDocumentBuilder

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Top-level entry point for creating fresh PDF documents.

```csharp
public sealed class PdfDocumentBuilder
```

## Remarks

Pages are added in order via `AddPage`. Each returns a `PageBuilder` for drawing. Optional document-level header and footer callbacks run for every page just before save, with the final page number and total page count supplied.  

 Call `Save(System.IO.Stream)` or `ToByteArray()` to emit the PDF bytes.

## Methods

### `Create`

__static__

```csharp
static PdfDocumentBuilder Create() => new()
```

Creates a new empty document builder.

### `SetTitle`

```csharp
PdfDocumentBuilder SetTitle(string title)
```

Sets the document's /Title metadata.

### `SetAuthor`

```csharp
PdfDocumentBuilder SetAuthor(string author)
```

Sets the document's /Author metadata.

### `SetSubject`

```csharp
PdfDocumentBuilder SetSubject(string sub)
```

Sets the document's /Subject metadata.

### `SetHeader`

```csharp
PdfDocumentBuilder SetHeader(Action<PageBuilder, int, int> draw)
```

Registers a page header callback. The callback receives the page, 1-based page number, and total page count; it should draw header content.

### `SetFooter`

```csharp
PdfDocumentBuilder SetFooter(Action<PageBuilder, int, int> draw)
```

Registers a page footer callback. Same shape as `SetHeader`.

### `AddTrueTypeFont`

```csharp
PdfDocumentBuilder AddTrueTypeFont(string name, byte[] fontData)
```

Registers a font program so pages can draw text in it. The font is embedded (as a Type0 composite font) only if used, subset to the glyphs drawn, and only those glyphs get width and ToUnicode entries. Accepts a TrueType font, a CFF-based OpenType font (.otf) or a collection (.ttc/.otc, whose first face is used — see `AddFont(string, byte[], int)` for another face).

**Parameters**

- `name` — The font name to pass to `PageBuilder.DrawText`; also recorded as the PostScript base-font name.
- `fontData` — The complete font program.

**Remarks:** The font must be static (convert variable fonts to a static instance first; CFF2 is not supported). `PageBuilder.DrawText` emits glyphs in logical order without complex-script shaping; shape with `TextShaper` and draw with `PageBuilder.DrawShapedRun(IReadOnlyList{Chuvadi.Pdf.Text.Shaping.ShapedGlyph}, string, IReadOnlyList{int}, double, double, string, double, Color)` for complex scripts. <exception cref="Chuvadi.Pdf.Fonts.Rendering.FontRenderingException">The font data is invalid or unsupported.</exception>

### `AddFont`

```csharp
PdfDocumentBuilder AddFont(string name, byte[] fontData, int faceIndex = 0)
```

Registers one face of a font program — TrueType (.ttf), CFF-based OpenType (.otf) or a collection (.ttc/.otc) — so pages can draw text in it. TrueType outlines embed as `CIDFontType2`, CFF outlines as `CIDFontType0` (`/FontFile3 /CIDFontType0C`); either way the program is subset to the glyphs drawn.

**Parameters**

- `name` — The font name to draw with; also the PostScript base-font name.
- `fontData` — The complete font file.
- `faceIndex` — The face of a collection (0 for a single font).

**Returns:** This builder. <exception cref="ArgumentNullException">`name` or `fontData` is null.</exception> <exception cref="ArgumentOutOfRangeException">`faceIndex` is not a face of the file.</exception> <exception cref="Chuvadi.Pdf.Fonts.Rendering.FontRenderingException">The font data is invalid or unsupported.</exception>

### `UseLipiFonts`

```csharp
PdfDocumentBuilder UseLipiFonts()
```

Enables the automatic LiPi Sans font. After calling this, drawing text with the font name "Lipi" selects the matching LiPi face per script and embeds each used face. Indic runs are shaped with `TextShaper` and carry their source text as `/ActualText`, so they extract and search as written.

**Returns:** This builder.

### `AddPage`

```csharp
PageBuilder AddPage(PageSize size)
```

Adds a page of the given size and returns its builder.

### `Save`

```csharp
void Save(Stream output) => Save(output, null)
```

Saves the document to a stream.

### `Save`

```csharp
void Save(Stream output, EncryptionOptions? encryption)
```

Saves the document to a stream, optionally encrypting it. Pass an `EncryptionOptions` (for example `EncryptionOptions.Aes256(string, string?)`) to encrypt, or null for no encryption. PDF 32000-1:2008 §7.6 — encryption.

**Parameters**

- `output` — The stream to write to.
- `encryption` — The encryption options, or null for no encryption.

### `ToByteArray`

```csharp
byte[] ToByteArray() => ToByteArray(null)
```

Returns the document as a byte array.

### `ToByteArray`

```csharp
byte[] ToByteArray(EncryptionOptions? encryption)
```

Returns the document as a byte array, optionally encrypting it. Pass an `EncryptionOptions` (for example `EncryptionOptions.Aes256(string, string?)`) to encrypt, or null for no encryption. PDF 32000-1:2008 §7.6 — encryption.

**Parameters**

- `encryption` — The encryption options, or null for no encryption.

---

_Source: [`src/Chuvadi.Pdf.Authoring/PdfDocumentBuilder.cs`](../../../src/Chuvadi.Pdf.Authoring/PdfDocumentBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
