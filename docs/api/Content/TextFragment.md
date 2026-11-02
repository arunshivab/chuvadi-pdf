# TextFragment

**Class** in `Chuvadi.Pdf.Content` (Content)

A piece of text extracted from a PDF content stream, together with its approximate position in user space.

```csharp
public sealed class TextFragment
```

## Remarks

Position coordinates are in PDF user space (origin bottom-left, Y increases upward): the origin of the first glyph, from the text matrix mapped through the current transformation matrix (and a form XObject's /Matrix). Since 3.28 the CTM is applied; before, positions were in text space. For a full layout-aware extraction, these fragments should be sorted and grouped based on their Y coordinates (same line = similar Y) and X coordinates (reading order = ascending X). That logic lives in Chuvadi.Pdf.Text.

## Constructors

### `TextFragment(string text, double x, double y, double fontSize)`

Initialises a new `TextFragment`.

**Parameters**

- `text` — The Unicode text content of this fragment.
- `x` — The X position in user space (left edge of first glyph).
- `y` — The Y position in user space (baseline of the text).
- `fontSize` — The font size in points at the time of rendering.

### `TextFragment(string text, double x, double y, double fontSize, double width)`

Initialises a new `TextFragment` with its advance width.

**Parameters**

- `text` — The Unicode text content of this fragment.
- `x` — The X position in user space (left edge of first glyph).
- `y` — The Y position in user space (baseline of the text).
- `fontSize` — The font size in points at the time of rendering.
- `width` — The horizontal advance of the shown glyphs in the same space as `x`, or `double.NaN` when the font's widths are not known.

### `TextFragment(string text, double x, double y, double fontSize, double width, double angle)`

Initialises a new `TextFragment` with its advance width and the direction of its baseline.

**Parameters**

- `text` — The Unicode text content of this fragment.
- `x` — The X position in user space (origin of the first glyph).
- `y` — The Y position in user space (baseline of the text).
- `fontSize` — The font size in user space.
- `width` — The advance of the shown glyphs along the baseline, in user space, or `double.NaN` when the font's widths are not known.
- `angle` — The direction of the baseline in degrees, counter-clockwise from the positive X axis: 0 for ordinary text, 90 for text running up the page.

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

Gets the font size in user space: the `Tf` size scaled by the vertical scale of the text matrix and the CTM (since 3.28; before, the `Tf` size alone).

### `Width`

```csharp
double Width
```

Gets the advance of the fragment's glyphs along the baseline, in user space (from the font's widths), or `double.NaN` when it is not known.

### `Angle`

```csharp
double Angle
```

Gets the direction of the baseline in degrees, counter-clockwise from the positive X axis of user space (since 3.28): 0 for ordinary text, 90 for text running up the page, -90 for text running down it.

### `BaselineShift`

```csharp
BaselineShift BaselineShift
```

Gets whether the fragment is raised or lowered off the baseline of its line (since 3.29). Set by text extraction, which compares the fragment with the main text of its line; `BaselineShift.None` on fragments straight from the content-stream parser.

### `BaselineShiftAmount`

```csharp
double BaselineShiftAmount
```

Gets how far the fragment's baseline is off its line's baseline, in user space, measured across the baseline: positive when raised, negative when lowered, 0 when `BaselineShift` is `BaselineShift.None` (since 3.29).

### `IsActualText`

```csharp
bool IsActualText
```

Gets whether the text came from a marked-content `/ActualText` entry (PDF 32000-1:2008 §14.9.4) rather than from the glyphs' own Unicode mapping (since 3.29). Text extraction never rewrites such text: the source text wins.

### `MarkedContentId`

```csharp
int? MarkedContentId
```

Gets the marked-content identifier (`/MCID`) of the innermost marked-content sequence the text was shown in, or null when it has none (since 3.29). In a Tagged PDF the structure tree refers to page content by these identifiers (PDF 32000-1:2008 §14.7.4).

---

_Source: [`src/Chuvadi.Pdf.Content/TextFragment.cs`](../../../src/Chuvadi.Pdf.Content/TextFragment.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
