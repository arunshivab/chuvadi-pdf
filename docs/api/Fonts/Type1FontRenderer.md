# Type1FontRenderer

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Renders glyphs from an embedded Type1 (PostScript) font program — the `FontFile` stream of a simple Type1 font. Implements eexec decryption, charstring decryption, and the Type1 charstring interpreter (PDF 32000-1:2008 §9.6.2; Adobe Type 1 Font Format).

```csharp
public sealed class Type1FontRenderer
```

## Remarks

Outlines are produced in a 1000-unit em and scaled by the caller's point size. Glyph selection is by character code through the font dictionary's `/Encoding` (with `/Differences`) when present, otherwise the font program's built-in encoding, otherwise StandardEncoding.

## Methods

### `Create`

__static__

```csharp
static Type1FontRenderer? Create(byte[] fontFile, PdfDictionary fontDict, IPdfObjectResolver resolver)
```

Parses an embedded Type1 program and builds a renderer. The `fontDict` supplies a PDF `/Encoding` override (Differences) when present.

**Returns:** A renderer, or `null` when the program cannot be parsed. <exception cref="ArgumentNullException">When a required argument is null.</exception>

### `GetGlyphPath`

```csharp
GraphicsPath GetGlyphPath(int code, double pointSize, out double advance)
```

Returns the outline path for `code`, scaled to `pointSize`, and reports the glyph advance in points.

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/Type1FontRenderer.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/Type1FontRenderer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
