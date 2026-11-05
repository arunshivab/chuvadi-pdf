# AdobeGlyphList

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Provides the canonical Adobe Glyph List (AGL) version 2.0, mapping PostScript glyph names to their Unicode scalar values. All scalar values in the list lie within the Basic Multilingual Plane.

```csharp
public static class AdobeGlyphList
```

## Properties

### `Count`

__static__

```csharp
static int Count => SingleMap.Count + SequenceMap.Count
```

Gets the total number of glyph-name entries in the list.

## Methods

### `TryGetCodePoint`

__static__

```csharp
static bool TryGetCodePoint(string glyphName, out int codePoint)
```

Attempts to resolve a glyph name to a single Unicode scalar value via a direct lookup in the Adobe Glyph List. Names that are absent, or that map to a sequence of more than one scalar value, yield `false`.

**Parameters**

- `glyphName` — The PostScript glyph name to look up.
- `codePoint` — When this method returns `true`, the resolved scalar value.

**Returns:** `true` if the name maps to exactly one scalar value; otherwise `false`.

### `TryGetCodePoints`

__static__

```csharp
static bool TryGetCodePoints(string glyphName, out IReadOnlyList<int> codePoints)
```

Attempts to resolve a glyph name to its full Unicode scalar value sequence via a direct lookup in the Adobe Glyph List. Most names map to a single scalar value; a small number of presentation forms map to a sequence.

**Parameters**

- `glyphName` — The PostScript glyph name to look up.
- `codePoints` — When this method returns `true`, the resolved scalar value sequence.

**Returns:** `true` if the name is present in the list; otherwise `false`.

### `Contains`

__static__

```csharp
static bool Contains(string glyphName)
```

Determines whether the specified glyph name is present in the Adobe Glyph List.

**Parameters**

- `glyphName` — The PostScript glyph name to test.

**Returns:** `true` if the name is present; otherwise `false`.

---

_Source: [`src/Chuvadi.Pdf.Fonts/AdobeGlyphList.cs`](../../../src/Chuvadi.Pdf.Fonts/AdobeGlyphList.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
