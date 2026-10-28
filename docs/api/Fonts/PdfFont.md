# PdfFont

**Class** in `Chuvadi.Pdf.Fonts` (Fonts)

Represents a PDF font and provides character code to Unicode mapping for text extraction purposes.

```csharp
public sealed class PdfFont
```

## Remarks

Phase 1 supports text extraction only — no glyph rendering or metrics. The mapping strategy, in priority order: 
 
- ToUnicode CMap — present in most modern PDFs, most accurate. 
- Encoding + glyph name lookup — for simple fonts without ToUnicode. 
- Direct code-as-Unicode — last resort for unrecognised configurations.  PDF 32000-1:2008 §9.10.2 — Mapping character codes to Unicode values.

## Properties

### `ToUnicodeMap`

```csharp
IReadOnlyDictionary<int, string>? ToUnicodeMap => _toUnicodeMap
```

The ToUnicode CMap mapping (source code → Unicode string) for this font, or `null` when the font has no ToUnicode entry.

## Methods

### `FromDictionary`

__static__

```csharp
static PdfFont FromDictionary(PdfDictionary fontDict, IPdfObjectResolver resolver)
```

Builds a `PdfFont` from a font dictionary.

**Parameters**

- `fontDict` — The font dictionary from the page Resources.
- `resolver` — Used to resolve indirect objects (e.g. ToUnicode stream).

### `Default`

__static__

```csharp
static PdfFont Default()
```

Returns a default font that maps codes directly to their Latin-1 equivalents. Used when no font dictionary is available.

### `FromMappings`

__static__

```csharp
static PdfFont FromMappings(IReadOnlyDictionary<int, string>? toUnicodeMap, IReadOnlyList<CodespaceRange>? codespaceRanges = null, PdfFontEncoding? encoding = null, bool isComposite = false)
```

Constructs a font directly from explicit mappings, bypassing PDF dictionary parsing. Useful for synthetic PDFs, programmatic font construction, and unit tests that need deterministic mapping state.

**Parameters**

- `toUnicodeMap` — Source-code-to-Unicode mapping. Keys are 1- to 4-byte codes packed big-endian into an int (e.g. the byte sequence 0xE2 0x9C 0x93 is the key 0xE29C93). May be `null`.
- `codespaceRanges` — Optional codespace declarations from the source CMap. Used (together with the maximum key in `toUnicodeMap`) to bound the byte window the decoder considers. May be `null`.
- `encoding` — Optional single-byte encoding for fallback when no `toUnicodeMap` entry matches a position.
- `isComposite` — Whether this represents a Type0/composite font. Affects the default byte-window width when neither codespace ranges nor mapping keys suggest a wider window.

### `TryMeasure`

```csharp
bool TryMeasure(byte[] bytes, out double width, out int codeCount, out int spaceCount)
```

Measures a shown string with the font's glyph widths (/Widths for a simple font, /W and /DW for a Type0 font with the Identity-H encoding).

**Parameters**

- `bytes` — The raw bytes of a Tj, TJ or similar operand.
- `width` — The summed glyph width, in thousandths of text space.
- `codeCount` — The number of character codes (for character spacing).
- `spaceCount` — The number of single-byte code 32s (for word spacing).

**Returns:** `true` when the font's widths are known. <exception cref="ArgumentNullException">`bytes` is null.</exception>

### `Decode`

```csharp
string Decode(byte[] bytes)
```

Converts a sequence of bytes from a PDF text string operator to Unicode text.

**Parameters**

- `bytes` — The raw bytes from a Tj, TJ, or similar operator.

**Returns:** The decoded Unicode string.

**Remarks:** v2.1.4: at each byte position the decoder tries widths from `MaxSupportedByteCount` down to 1 against the ToUnicode mapping; the first width whose packed code is present in the map consumes that many bytes. If no width matches, the byte falls through to the single-byte encoding fallback and the cursor advances by one. This correctly handles 1-byte simple-font CMaps, 2-byte composite CMaps, and 3-byte CMaps emitted by Word for non-Latin/symbol fonts (UTF-8 encoding of the semantic Unicode codepoint as the source code).

### `DecodeCode`

```csharp
string DecodeCode(int code)
```

Converts a single character code to a Unicode string. Returns an empty string when the code cannot be mapped.

---

_Source: [`src/Chuvadi.Pdf.Fonts/PdfFont.cs`](../../../src/Chuvadi.Pdf.Fonts/PdfFont.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
