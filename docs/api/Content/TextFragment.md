# TextFragment

**Class** in `Chuvadi.Pdf.Content` (Content)

A piece of text extracted from a PDF content stream, together with its approximate position in user space.

```csharp
public sealed class TextFragment
```

## Remarks

Position coordinates are in PDF user space (origin bottom-left, Y increases upward). The X and Y values come from the text matrix at the point the text was rendered. For a full layout-aware extraction, these fragments should be sorted and grouped based on their Y coordinates (same line = similar Y) and X coordinates (reading order = ascending X). That logic lives in Chuvadi.Pdf.Text.

## Constructors

### `TextFragment(string text, double x, double y, double fontSize)`

Initialises a new `TextFragment`.

**Parameters**

- `text` — The Unicode text content of this fragment.
- `x` — The X position in user space (left edge of first glyph).
- `y` — The Y position in user space (baseline of the text).
- `fontSize` — The font size in points at the time of rendering.

## Properties

### `Text`

```csharp
string Text
```

Gets the Unicode text content of this fragment.

### `X`

```csharp
double X
```

Gets the X position (left edge) in PDF user space.

### `Y`

```csharp
double Y
```

Gets the Y position (baseline) in PDF user space.

### `FontSize`

```csharp
double FontSize
```

Gets the font size in points.

---

_Source: [`src/Chuvadi.Pdf.Content/TextFragment.cs`](../../../src/Chuvadi.Pdf.Content/TextFragment.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
