# PageBuilder

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Per-page drawing API. All coordinates use top-left origin (Y increases downward), units are PDF points (1 pt = 1/72 inch).

```csharp
public sealed class PageBuilder
```

## Properties

### `Width`

```csharp
double Width
```

Page width in points.

### `Height`

```csharp
double Height
```

Page height in points.

## Methods

### `DrawText`

```csharp
PageBuilder DrawText(string text, double x, double y, string font, double size, Color color)
```

Draws a single line of text. The top of the text aligns to `y`.

### `DrawShapedRun`

```csharp
PageBuilder DrawShapedRun(IReadOnlyList<ShapedGlyph> glyphs, double x, double y, string font, double size, Color color)
```

Draws a pre-shaped glyph run with a registered custom (TrueType) font. Each glyph is placed at an absolute position derived from its advance and offsets, preserving the output of an external shaper. Advances and offsets are in 1000-units-per-em text space.

**Parameters**

- `glyphs` — The shaped glyphs, in visual order.
- `x` — The pen x position (top-left origin).
- `y` — The baseline-top y position.
- `font` — The name of a registered custom TrueType font.
- `size` — The font size in points.
- `color` — The fill color.

**Returns:** This page builder. <exception cref="ArgumentNullException">`glyphs` or `font` is null.</exception> <exception cref="InvalidOperationException">`font` is not a registered custom font.</exception>

### `DrawTextBlock`

```csharp
TextBlockResult DrawTextBlock(string text, double x, double y, double width, double height, string font, double size, Color color, TextAlignment align = TextAlignment.Left, double lineHeight = 1.2)
```

Draws word-wrapped text inside a rectangle. Returns a result indicating whether all text fit and what (if any) remains.

### `DrawHyperlink`

```csharp
PageBuilder DrawHyperlink(string text, double x, double y, string font, double size, Color color, string uri)
```

Draws text and registers a clickable hyperlink covering the text bounds.

### `DrawLine`

```csharp
PageBuilder DrawLine(double x1, double y1, double x2, double y2, Color color, double width = 1.0)
```

Draws a line from (x1, y1) to (x2, y2).

### `DrawArrow`

```csharp
PageBuilder DrawArrow(double x1, double y1, double x2, double y2, Color color, double width = 1.0, ArrowEnds ends = ArrowEnds.End, double? headLength = null, double? headWidth = null)
```

Draws an arrow from (x1, y1) to (x2, y2): a stroked shaft plus filled triangular head(s) in the same colour. The shaft stops at each head's base so the line cap never pokes through a tip. Head geometry comes from `ArrowGeometry`, so an on-screen preview built from `ArrowGeometry.HeadPoints` matches this output exactly. A zero-length arrow draws nothing.

**Parameters**

- `x1` — Start x (top-left page coordinates).
- `y1` — Start y (top-left page coordinates).
- `x2` — End x (top-left page coordinates).
- `y2` — End y (top-left page coordinates).
- `color` — Shaft and head colour.
- `width` — Shaft stroke width.
- `ends` — Which ends carry a head.
- `headLength` — Head length, or null for `ArrowGeometry.DefaultHeadLength`.
- `headWidth` — Head base width, or null for `ArrowGeometry.DefaultHeadWidth`.

**Returns:** This builder, for chaining. <exception cref="ArgumentOutOfRangeException"> Thrown when a head size is negative or not finite. </exception>

### `DrawRectangle`

```csharp
PageBuilder DrawRectangle(double x, double y, double width, double height, Color? fill = null, Color? stroke = null, double strokeWidth = 1.0)
```

Draws a rectangle. Supply at least one of `fill` or `stroke`.

### `DrawPath`

```csharp
PageBuilder DrawPath(Path path, Color? fill = null, Color? stroke = null, double strokeWidth = 1.0, FillRule fillRule = FillRule.NonZeroWinding)
```

Draws an arbitrary `Path` (lines and cubic Béziers). Supply at least one of `fill` or `stroke`; a path with neither, or an empty path, paints nothing. `fillRule` selects non-zero winding (default) or even-odd filling.

### `DrawImage`

```csharp
PageBuilder DrawImage(byte[] imageBytes, double x, double y, double width, double height)
```

Embeds an image and draws it at the given top-left rectangle. Supports JPEG, PNG, TIFF, and BMP; images with an alpha channel are embedded with a soft mask so transparency is preserved.

### `DrawImage`

```csharp
PageBuilder DrawImage(ImageFrame image, double x, double y, double width, double height)
```

Embeds an already-decoded image frame and draws it at the given top-left rectangle. Useful for frames produced by the `Chuvadi.Pdf.Images` decoders — for example one page of a multi-frame TIFF.

### `DrawImage`

```csharp
PageBuilder DrawImage(byte[] imageBytes, double x, double y, double width, double height, double opacity)
```

Embeds an image and draws it at the given top-left rectangle with a constant `opacity` (0 fully transparent, 1 fully opaque). Any alpha channel in the image is still honoured via its soft mask; this opacity multiplies on top of it. <exception cref="ArgumentOutOfRangeException"> Thrown when `opacity` is outside [0, 1]. </exception>

### `DrawImage`

```csharp
PageBuilder DrawImage(ImageFrame image, double x, double y, double width, double height, double opacity)
```

Embeds an already-decoded image frame and draws it at the given top-left rectangle with a constant `opacity` (0 fully transparent, 1 fully opaque). Any alpha channel in the frame is still honoured via its soft mask; this opacity multiplies on top of it. <exception cref="ArgumentOutOfRangeException"> Thrown when `opacity` is outside [0, 1]. </exception>

### `DrawTable`

```csharp
TableBuilder DrawTable(double x, double y, double width)
```

Begins a fluent table at (x, y) with the given total width. Call `TableBuilder.Render` when done configuring.

---

_Source: [`src/Chuvadi.Pdf.Authoring/PageBuilder.cs`](../../../src/Chuvadi.Pdf.Authoring/PageBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
