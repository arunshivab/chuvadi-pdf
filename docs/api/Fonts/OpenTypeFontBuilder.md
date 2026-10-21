# OpenTypeFontBuilder

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Wraps a raw CFF font program in an OpenType (OTTO) SFNT envelope with the synthesised tables a browser requires (`CFF `, `cmap`, `head`, `hhea`, `hmtx`, `maxp`, `name`, `OS/2`, `post`), so the font can be embedded in an SVG `@font-face` rule and located by semantic Unicode code point.

```csharp
public static class OpenTypeFontBuilder
```

## Remarks

The raw CFF program is passed through unchanged as the `CFF ` table. Glyph metrics for `hmtx`, `head`, `hhea`, and `OS/2` are read from `CffLoader`. Created/modified timestamps are fixed at zero so the output is deterministic.

## Methods

### `Build`

__static__

```csharp
static byte[] Build(byte[] cffProgram, CffLoader loader, IReadOnlyDictionary<int, int> unicodeToGid, string postScriptName, bool isBold, bool isItalic)
```

Builds an OpenType (OTTO) font program wrapping `cffProgram`.

**Parameters**

- `cffProgram` — The raw CFF program bytes (the PDF /FontFile3 stream).
- `loader` — A loader over the same CFF program, used for glyph metrics.
- `unicodeToGid` — Map from Unicode code point to glyph index for the cmap.
- `postScriptName` — The font's PostScript name (subset prefix already stripped).
- `isBold` — Whether the font should be flagged bold.
- `isItalic` — Whether the font should be flagged italic.

**Returns:** The assembled OpenType font program bytes. <exception cref="ArgumentNullException">Thrown when any reference argument is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/OpenTypeFontBuilder.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/OpenTypeFontBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
