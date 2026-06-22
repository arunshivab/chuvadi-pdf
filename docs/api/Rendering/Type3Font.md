# Type3Font

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

A parsed Type 3 font: its FontMatrix, per-code glyph content streams, the font's own /Resources, and glyph-space widths. Returned by `FromDictionary`; rendering sinks execute each glyph's content stream under the FontMatrix-composed text rendering matrix.

```csharp
public sealed class Type3Font
```

## Properties

### `FontMatrix`

```csharp
double[] FontMatrix
```

The six FontMatrix entries mapping glyph space to text space.

### `Resources`

```csharp
PdfDictionary? Resources
```

The font's own /Resources, used when executing a glyph's content stream.

## Methods

### `TryGetGlyph`

```csharp
bool TryGetGlyph(int code, out Type3Glyph glyph) => _glyphs.TryGetValue(code, out glyph)
```

Gets the glyph for a character code, if defined.

### `FromDictionary`

__static__

```csharp
static Type3Font? FromDictionary(PdfDictionary fontDict, IPdfObjectResolver resolver)
```

Parses a Type 3 font dictionary. Returns null if the dictionary is not a Type 3 font or lacks a CharProcs dictionary.

**Parameters**

- `fontDict` — The font dictionary (`/Subtype /Type3`).
- `resolver` — Resolver for indirect references.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/Type3Font.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/Type3Font.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
