# TrueTypeFontEmbedder

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Builds the PDF object graph that embeds a TrueType (`glyf`) font as a composite Type0 font with Identity-H encoding, so authored content can draw text in a custom font. The font program is subset to the glyphs used (glyph numbering kept); the `/W` width array and `/ToUnicode` CMap cover only those glyphs. For CFF-based OpenType fonts and faces of collections, use `FontEmbedder`.

```csharp
public static class TrueTypeFontEmbedder
```

## Remarks

The font is referenced from a page's `/Font` resource by the returned `EmbeddedFontObjects.Type0FontId`. Text drawn with it must be encoded as two-byte big-endian glyph identifiers (the `ContentStreamWriter` handles that). This embeds glyphs in logical order; it does not perform complex-script shaping (GSUB/GPOS or reordering), so Latin renders correctly and Indic renders correctly only for isolated or already-ordered glyphs.

## Methods

### `Build`

__static__

```csharp
static EmbeddedFontObjects Build(byte[] ttf, TrueTypeLoader loader, IReadOnlyCollection<int> usedCodepoints, string baseFont, Func<PdfObjectId> allocateId, IReadOnlyCollection<int>? extraGlyphs = null)
```

Builds the embedded-font objects for `ttf`.

**Parameters**

- `ttf` — The complete TrueType font program (static, glyf-based).
- `loader` — A loader over the same font, for cmap and widths.
- `usedCodepoints` — Unicode code points actually drawn with the font.
- `baseFont` — The PostScript base-font name to record.
- `allocateId` — Allocates fresh object ids in the owning document.
- `extraGlyphs` — Raw glyph ids from pre-shaped runs to include in the subset.

**Returns:** The Type0 font id and every object to add to the document. <exception cref="ArgumentNullException">When any reference argument is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Authoring/TrueTypeFontEmbedder.cs`](../../../src/Chuvadi.Pdf.Authoring/TrueTypeFontEmbedder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
