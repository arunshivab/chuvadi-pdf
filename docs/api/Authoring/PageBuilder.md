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

### `DrawPath`

```csharp
PageBuilder DrawPath(Path path, Color? fill = null, Color? stroke = null, double strokeWidth = 1.0, FillRule fillRule = FillRule.NonZeroWinding)
```

Draws an arbitrary `Path` (lines and cubic Béziers). Supply at least one of `fill` or `stroke`; a path with neither, or an empty path, paints nothing. `fillRule` selects non-zero winding (default) or even-odd filling.

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
