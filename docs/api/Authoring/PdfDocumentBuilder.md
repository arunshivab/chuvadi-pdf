# PdfDocumentBuilder

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Top-level entry point for creating fresh PDF documents.

```csharp
public sealed class PdfDocumentBuilder
```

## Remarks

Pages are added in order via `AddPage`. Each returns a `PageBuilder` for drawing. Optional document-level header and footer callbacks run for every page just before save, with the final page number and total page count supplied.  

 Call `Save` or `ToByteArray` to emit the PDF bytes.

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

Registers a TrueType font program so pages can draw text in it. The font is embedded (as a Type0 / CIDFontType2 composite font) only if used, and only the glyphs actually drawn get width and ToUnicode entries.

**Parameters**

- `name` — The font name to pass to `PageBuilder.DrawText`; also recorded as the PostScript base-font name.
- `fontData` — The complete static TrueType (glyf) font program.

**Remarks:** The font must be a static TrueType font (convert variable fonts to a static instance first). Text is emitted in logical order without complex-script shaping, so Latin renders correctly and Indic renders correctly only for isolated or already-ordered glyphs.

### `AddPage`

```csharp
PageBuilder AddPage(PageSize size)
```

Adds a page of the given size and returns its builder.

### `Save`

```csharp
void Save(Stream output)
```

Saves the document to a stream.

### `ToByteArray`

```csharp
byte[] ToByteArray()
```

Returns the document as a byte array.

---

_Source: [`src/Chuvadi.Pdf.Authoring/PdfDocumentBuilder.cs`](../../../src/Chuvadi.Pdf.Authoring/PdfDocumentBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
