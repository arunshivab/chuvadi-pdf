# GlyphNameToUnicode

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Implements the Adobe Glyph List algorithm for deriving Unicode scalar values from a glyph name. The algorithm drops any suffix beginning at the first period, splits the remainder on underscores into ligature components, and maps each component via a direct Adobe Glyph List lookup, the `uniXXXX` form, or the `uXXXX..` form.

```csharp
public static class GlyphNameToUnicode
```

## Remarks

The `uniXXXX` and `uXXXX..` forms require uppercase hexadecimal digits, as mandated by the Adobe Glyph List specification. Non-conformant lowercase forms are not recognised and resolve to nothing.

## Methods

### `Resolve`

__static__

```csharp
static IReadOnlyList<int> Resolve(string glyphName)
```

Resolves a glyph name to its Unicode scalar value sequence using the Adobe Glyph List algorithm. Components that cannot be resolved contribute nothing to the result, so an entirely unresolvable name yields an empty sequence.

**Parameters**

- `glyphName` — The PostScript glyph name to resolve.

**Returns:** The resolved sequence of Unicode scalar values, which may be empty.

### `ResolveSingle`

__static__

```csharp
static int? ResolveSingle(string glyphName)
```

Resolves a glyph name to a single Unicode scalar value, returning `null` when the name resolves to nothing or to more than one scalar value.

**Parameters**

- `glyphName` — The PostScript glyph name to resolve.

**Returns:** The single resolved scalar value, or `null`.

---

_Source: [`src/Chuvadi.Pdf.Fonts/AdobeGlyphList.cs`](../../../src/Chuvadi.Pdf.Fonts/AdobeGlyphList.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
