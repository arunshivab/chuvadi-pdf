# Standard14GlyphWidths

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Per-character widths for the PDF Standard 14 fonts. Widths are in units of 1/1000 em, the standard PDF font metric unit.

```csharp
public static class Standard14GlyphWidths
```

## Remarks

When a PDF font dictionary does not include a /Widths array — as is permitted for Standard 14 fonts — these tables fill in the gap so that glyph-level positioning works correctly. Stage 9 will supplement this with full per-glyph outline data from Liberation/URW.

## Methods

### `IsStandard14`

__static__

```csharp
static bool IsStandard14(string baseFont)
```

Returns true when the given base font name is one of the PDF Standard 14 fonts (Helvetica, Times, Courier families, Symbol, ZapfDingbats).

### `Width`

__static__

```csharp
static int Width(string baseFont, char ch)
```

Returns the width in 1/1000 em of the given character.

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/Standard14GlyphWidths.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/Standard14GlyphWidths.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
