# TrueTypeFontPatch

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Rewrites the cmap table of an embedded TrueType font program so the browser can locate the embedded glyph by its semantic Unicode code point rather than the font's legacy encoding code point.

```csharp
public static class TrueTypeFontPatch
```

## Remarks

PDF symbol fonts (Wingdings, Symbol, Webdings, ZapfDingbats, MT Extra, and similar) carry glyphs at legacy Windows-symbol code points — for example, the Wingdings checkmark glyph is reachable at character code 0xFC in the original Windows encoding. PDF readers do not use the font's cmap; they address glyphs directly by glyph index (CID). When such a PDF is exported to SVG and the embedded font is placed in a `@font-face` rule, the browser DOES use the cmap table and asks for the glyph at the semantic Unicode code point (U+2713 for the checkmark). Since no entry exists in the font's cmap at U+2713, the browser falls back to a system font and renders the wrong glyph.  

 The fix is to add a new cmap subtable mapping each semantic Unicode code point (from the ToUnicode CMap) to the corresponding glyph index. This implementation replaces the cmap table entirely with a fresh one containing a single Windows Unicode subtable — format 4 (BMP only) or format 12 (full Unicode range). The original cmap subtables are not preserved; the embedded font is used only by browser rendering of the SVG, where the new mappings are sufficient.  

 This approach mirrors pdf2htmlEX's font remapping strategy. The alternative of mutating the original cmap in place would require parsing every cmap subtable format the source font might use; replacing the table is simpler and equally effective for the SVG use case.

## Methods

### `WithAugmentedCmap`

__static__

```csharp
static byte[] WithAugmentedCmap(byte[] fontBytes, IReadOnlyDictionary<int, int> unicodeToGid)
```

Returns a copy of `fontBytes` whose cmap table has been replaced by a fresh table containing the given Unicode-to-glyph mappings. The original cmap subtables are discarded.

**Parameters**

- `fontBytes` — Original TrueType/OpenType font program bytes.
- `unicodeToGid` — Map from Unicode code point to glyph index. Code points outside the BMP (>= 0x10000) trigger a format-12 cmap subtable; if all code points are in the BMP the result uses format 4.

**Returns:** New font program bytes with the cmap table replaced. <exception cref="ArgumentNullException"> Thrown when `fontBytes` or `unicodeToGid` is null. </exception> <exception cref="FontRenderingException"> Thrown when the input bytes are not a valid TrueType/OpenType font or are too short to contain an offset table. </exception>

### `WithRequiredTables`

__static__

```csharp
static byte[] WithRequiredTables(byte[] fontBytes, string postScriptName, bool isBold, bool isItalic)
```

Returns a copy of `fontBytes` with minimal `OS/2`, `name` and `post` tables added where the font has none (since 3.30). PDF font subsets often carry only the tables a PDF reader needs; web browsers (their OpenType sanitiser) reject a font without these three and draw the text in a fallback font instead. Tables the font already has are kept unchanged.

**Parameters**

- `fontBytes` — A TrueType or OpenType font program.
- `postScriptName` — The name recorded in the added `name` table.
- `isBold` — Whether the added tables describe a bold face.
- `isItalic` — Whether the added tables describe an italic face.

**Returns:** The font with the three tables present, re-assembled with every table on a 4-byte boundary (browsers also reject misaligned tables). <exception cref="ArgumentNullException"> `fontBytes` or `postScriptName` is null. </exception> <exception cref="FontRenderingException"> The bytes are not a valid TrueType or OpenType font. </exception>

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/TrueTypeFontPatch.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/TrueTypeFontPatch.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
