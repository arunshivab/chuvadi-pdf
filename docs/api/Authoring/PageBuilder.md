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

### `DrawShapedRun`

```csharp
PageBuilder DrawShapedRun(IReadOnlyList<ShapedGlyph> glyphs, string text, IReadOnlyList<int> clusterStarts, double x, double y, string font, double size, Color color)
```

Draws a pre-shaped glyph run and records the source text, so the run can be copied, searched and extracted. Each glyph's cluster is the span of `text` from `clusterStarts`[i] to the next larger start (or the end of the text). A cluster drawn by one glyph gets a ToUnicode entry; a cluster drawn by several glyphs (conjuncts, ligature parts, split vowels), or by a glyph that already stands for other text, is wrapped in a `/Span` with `/ActualText`.

**Parameters**

- `glyphs` — The shaped glyphs, in visual order.
- `text` — The source text the glyphs were shaped from.
- `clusterStarts` — For each glyph, the UTF-16 index in `text` where its cluster starts (glyphs of one cluster share a start).
- `x` — The pen x position (top-left origin).
- `y` — The baseline-top y position.
- `font` — The name of a registered custom font.
- `size` — The font size in points.
- `color` — The fill color.

**Returns:** This page builder. <exception cref="ArgumentNullException">A reference argument is null.</exception> <exception cref="ArgumentException"> `clusterStarts` does not have one entry per glyph, or an entry is outside `text`. </exception> <exception cref="InvalidOperationException">`font` is not a registered custom font.</exception>

### `DrawShapedRun`

```csharp
PageBuilder DrawShapedRun(IReadOnlyList<ShapedGlyph> glyphs, string text, IReadOnlyList<int> clusterStarts, double x, double y, string font, double size, Color color, GlyphRunStyle style)
```

Draws a pre-shaped glyph run with its source text (see `DrawShapedRun(IReadOnlyList{ShapedGlyph}, string, IReadOnlyList{int}, double, double, string, double, Color)`) and a `GlyphRunStyle`: horizontal scale, slant and a bold stroke.

**Parameters**

- `glyphs` — The shaped glyphs, in visual order.
- `text` — The source text the glyphs were shaped from.
- `clusterStarts` — For each glyph, the UTF-16 index in `text` where its cluster starts.
- `x` — The pen x position (top-left origin).
- `y` — The baseline-top y position.
- `font` — The name of a registered custom font.
- `size` — The font size in points.
- `color` — The fill (and bold-stroke) color.
- `style` — The text-matrix and render-mode options.

**Returns:** This page builder. <exception cref="ArgumentNullException">A reference argument is null.</exception> <exception cref="ArgumentException"> `clusterStarts` does not match the glyphs, or `style` is invalid. </exception> <exception cref="InvalidOperationException">`font` is not a registered custom font.</exception>

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

### `DrawPath`

```csharp
PageBuilder DrawPath(Path path, Color? fill, Color? stroke, LineStyle style, FillRule fillRule = FillRule.NonZeroWinding)
```

Draws a `Path` with a `LineStyle` for the outline (width, dashes, caps, joins). Supply at least one of `fill` or `stroke`; a path with neither, or an empty path, paints nothing.

**Parameters**

- `path` — The path, in top-left coordinates.
- `fill` — The fill colour, or null for no fill.
- `stroke` — The outline colour, or null for no outline.
- `style` — How the outline is stroked.
- `fillRule` — Non-zero winding (default) or even-odd filling.

**Returns:** This page builder. <exception cref="ArgumentNullException">`path` or `style` is null.</exception> <exception cref="ArgumentException">`style` is not valid.</exception>

### `DrawLine`

```csharp
PageBuilder DrawLine(double x1, double y1, double x2, double y2, Color color, LineStyle style)
```

Draws a line from (x1, y1) to (x2, y2) with a `LineStyle` (dashes, caps).

**Parameters**

- `x1` — Start x (top-left origin).
- `y1` — Start y.
- `x2` — End x.
- `y2` — End y.
- `color` — The line colour.
- `style` — How the line is stroked.

**Returns:** This page builder. <exception cref="ArgumentNullException">`style` is null.</exception> <exception cref="ArgumentException">`style` is not valid.</exception>

### `DrawRectangle`

```csharp
PageBuilder DrawRectangle(double x, double y, double width, double height, Color? fill, Color? stroke, LineStyle style)
```

Draws a rectangle with a `LineStyle` for the outline. Supply at least one of `fill` or `stroke`.

**Parameters**

- `x` — Left edge (top-left origin).
- `y` — Top edge.
- `width` — Width in points.
- `height` — Height in points.
- `fill` — The fill colour, or null for no fill.
- `stroke` — The outline colour, or null for no outline.
- `style` — How the outline is stroked.

**Returns:** This page builder. <exception cref="ArgumentNullException">`style` is null.</exception> <exception cref="ArgumentException">`style` is not valid.</exception>

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

### `DrawImage`

```csharp
PageBuilder DrawImage(byte[] imageBytes, double x, double y, double width, double height, ImageCrop crop)
```

Embeds an image and draws the `crop` part of it into the given top-left rectangle. The crop is done by clipping, so the image is embedded whole (a JPEG stays untouched) and the cropped-away pixels remain in the file — do not use cropping to hide content.

**Parameters**

- `imageBytes` — The encoded image (JPEG, PNG, TIFF or BMP).
- `x` — Left edge of the destination (top-left origin).
- `y` — Top edge of the destination.
- `width` — Destination width in points.
- `height` — Destination height in points.
- `crop` — The part of the image to show, in source pixels.

**Returns:** This page builder. <exception cref="ArgumentNullException">`imageBytes` is null.</exception> <exception cref="ArgumentOutOfRangeException">The crop has no area or is not finite.</exception> <exception cref="ArgumentException">The bytes are not a supported image.</exception>

### `DrawImage`

```csharp
PageBuilder DrawImage(ImageFrame image, double x, double y, double width, double height, ImageCrop crop)
```

Embeds a decoded image frame and draws the `crop` part of it into the given top-left rectangle (by clipping; see `DrawImage(byte[], double, double, double, double, ImageCrop)`).

**Parameters**

- `image` — The decoded frame.
- `x` — Left edge of the destination (top-left origin).
- `y` — Top edge of the destination.
- `width` — Destination width in points.
- `height` — Destination height in points.
- `crop` — The part of the image to show, in source pixels.

**Returns:** This page builder. <exception cref="ArgumentNullException">`image` is null.</exception> <exception cref="ArgumentOutOfRangeException">The crop has no area or is not finite.</exception>

### `PushClip`

```csharp
PageBuilder PushClip(double x, double y, double width, double height)
```

Starts clipping to a rectangle: everything drawn until the matching `PopClip` is cut off outside it (and outside any clip already in force). Emits `q … re W n`.

**Parameters**

- `x` — Left edge (top-left origin).
- `y` — Top edge.
- `width` — Width in points.
- `height` — Height in points.

**Returns:** This page builder.

### `PushClip`

```csharp
PageBuilder PushClip(Path path, FillRule fillRule = FillRule.NonZeroWinding)
```

Starts clipping to a path (top-left coordinates), until the matching `PopClip`.

**Parameters**

- `path` — The clip outline. An empty path clips everything away.
- `fillRule` — Which points are inside: non-zero winding (default) or even-odd.

**Returns:** This page builder. <exception cref="ArgumentNullException">`path` is null.</exception>

### `PopClip`

```csharp
PageBuilder PopClip() => PopScope(ScopeKind.Clip, nameof(PopClip))
```

Ends the innermost clip started with `PushClip` (emits `Q`).

**Returns:** This page builder. <exception cref="InvalidOperationException">The innermost open scope is not a clip.</exception>

### `PushOpacity`

```csharp
PageBuilder PushOpacity(double opacity) => PushOpacity(opacity, opacity)
```

Draws everything until the matching `PopOpacity` with the given constant opacity (fills and strokes alike; text, paths and images). Nested scopes multiply.

**Parameters**

- `opacity` — 0 (invisible) to 1 (opaque).

**Returns:** This page builder. <exception cref="ArgumentOutOfRangeException">`opacity` is outside [0, 1].</exception>

### `PushOpacity`

```csharp
PageBuilder PushOpacity(double fillOpacity, double strokeOpacity)
```

Draws everything until the matching `PopOpacity` with the given fill and stroke opacity (ExtGState `/ca` and `/CA`). Nested scopes multiply; an image's own opacity multiplies too.

**Parameters**

- `fillOpacity` — Opacity of fills and text, 0 to 1.
- `strokeOpacity` — Opacity of strokes, 0 to 1.

**Returns:** This page builder. <exception cref="ArgumentOutOfRangeException">An opacity is outside [0, 1].</exception>

### `PopOpacity`

```csharp
PageBuilder PopOpacity() => PopScope(ScopeKind.Opacity, nameof(PopOpacity))
```

Ends the innermost `PushOpacity` scope (emits `Q`).

**Returns:** This page builder. <exception cref="InvalidOperationException">The innermost open scope is not an opacity scope.</exception>

### `AddLink`

```csharp
PageBuilder AddLink(double x, double y, double width, double height, Uri uri)
```

Adds a clickable link to `uri` over a rectangle. Nothing is drawn; the link has no border.

**Parameters**

- `x` — Left edge (top-left origin).
- `y` — Top edge.
- `width` — Width in points.
- `height` — Height in points.
- `uri` — The link target, written as given.

**Returns:** This page builder. <exception cref="ArgumentNullException">`uri` is null.</exception>

### `AddLink`

```csharp
PageBuilder AddLink(double x, double y, double width, double height, int targetPageIndex, double targetTop)
```

Adds a link over a rectangle that jumps to another page of this document (a `/GoTo` action), showing `targetTop` at the top of the window. Nothing is drawn.

**Parameters**

- `x` — Left edge (top-left origin).
- `y` — Top edge.
- `width` — Width in points.
- `height` — Height in points.
- `targetPageIndex` — The zero-based index of the target page, checked when the document is saved.
- `targetTop` — The y position on the target page (top-left origin) to scroll to.

**Returns:** This page builder. <exception cref="ArgumentOutOfRangeException">`targetPageIndex` is negative.</exception>

### `DrawTable`

```csharp
TableBuilder DrawTable(double x, double y, double width)
```

Begins a fluent table at (x, y) with the given total width. Call `TableBuilder.Render` when done configuring.

---

_Source: [`src/Chuvadi.Pdf.Authoring/PageBuilder.cs`](../../../src/Chuvadi.Pdf.Authoring/PageBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
