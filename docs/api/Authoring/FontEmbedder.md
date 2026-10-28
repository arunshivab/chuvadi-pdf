# FontEmbedder

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Builds the PDF objects that embed a loaded `FontFace` as a composite Type0 font with Identity-H encoding (glyph ids are the codes). TrueType outlines embed as `CIDFontType2` (`/FontFile2`); CFF outlines (.otf) embed as `CIDFontType0` (`/FontFile3 /CIDFontType0C`). A face of a TrueType collection (.ttc/.otc) embeds as that face alone. Either way the program is subset to the glyphs used, with glyph numbering kept.

```csharp
public static class FontEmbedder
```

## Methods

### `Build`

__static__

```csharp
static EmbeddedFontObjects Build(FontFace face, IReadOnlyCollection<int> usedCodepoints, string baseFont, Func<PdfObjectId> allocateId, IReadOnlyCollection<int>? extraGlyphs = null)
```

Builds the embedded-font objects for `face`.

**Parameters**

- `face` — The loaded face (TrueType or CFF outlines).
- `usedCodepoints` — Unicode code points drawn through the face's cmap; each used glyph gets a width and a ToUnicode entry for its code point.
- `baseFont` — The PostScript base-font name to record.
- `allocateId` — Allocates fresh object ids in the owning document.
- `extraGlyphs` — Raw glyph ids from pre-shaped runs to include in the subset.

**Returns:** The Type0 font id and every object to add to the document. <exception cref="ArgumentNullException">When any reference argument is null.</exception> <exception cref="InvalidDataException">The face's CFF program cannot be embedded.</exception>

### `Build`

__static__

```csharp
static EmbeddedFontObjects Build(FontFace face, IReadOnlyCollection<int> usedCodepoints, IReadOnlyDictionary<int, string> glyphText, string baseFont, Func<PdfObjectId> allocateId, IReadOnlyCollection<int>? extraGlyphs = null)
```

Builds the embedded-font objects for `face`, with ToUnicode entries for glyphs that no code point reaches through the cmap (ligatures, conjuncts, alternates from shaping).

**Parameters**

- `face` — The loaded face (TrueType or CFF outlines).
- `usedCodepoints` — Unicode code points drawn through the face's cmap.
- `glyphText` — Glyph id to the text it stands for. An entry is used for glyphs that `usedCodepoints` does not already map; the glyph is also included in the subset. Entries longer than 256 UTF-16 units (the ToUnicode limit) are ignored.
- `baseFont` — The PostScript base-font name to record.
- `allocateId` — Allocates fresh object ids in the owning document.
- `extraGlyphs` — Raw glyph ids from pre-shaped runs to include in the subset.

**Returns:** The Type0 font id and every object to add to the document. <exception cref="ArgumentNullException">When any reference argument is null.</exception> <exception cref="InvalidDataException">The face's CFF program cannot be embedded.</exception>

---

_Source: [`src/Chuvadi.Pdf.Authoring/FontEmbedder.cs`](../../../src/Chuvadi.Pdf.Authoring/FontEmbedder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
