# WpfRenderer

**Class** in `Chuvadi.Pdf.Rendering.Wpf` (Rendering)

Renders a `PageDisplayList` into a WPF `DrawingVisual`.

```csharp
public sealed class WpfRenderer
```

## Remarks

Translates each `RenderOp` into WPF drawing primitives via `DrawingContext`. `PathOp` becomes a `StreamGeometry` drawn with `DrawingContext.DrawGeometry`; `TextOp` becomes a `FormattedText` drawn with `DrawingContext.DrawText`; `ImageOp` becomes a `BitmapSource` drawn with `DrawingContext`.  

 Coordinate handling: the renderer applies an outer `(1, 0, 0, -1, -x0, y0 + height)` transform, where `x0`, `y0`, `height` are the shown page box's corner and height, so PDF coordinates flow through directly and the box's lower-left corner lands at the visual's bottom-left. Text runs receive a local counter-flip so glyphs read upright. Since 3.32.0 the shown box is the CropBox by default (see `DisplayBox`), the visual is clipped to it, and a box that does not start at 0,0 is drawn where it is (backlog 38).

## Properties

### `DisplayBox`

```csharp
PdfDisplayBox DisplayBox
```

Gets or initialises which page box is shown (3.32.0, P2): the CropBox intersected with the MediaBox by default, or the whole MediaBox as before 3.32.0. The visual is clipped to the box, which spans 0..width by 0..height.

## Methods

### `RenderPage`

```csharp
DrawingVisual RenderPage(PdfDocument document, int pageIndex)
```

Renders a page to a `DrawingVisual`.

### `Render`

```csharp
DrawingVisual Render(PageDisplayList list)
```

Renders a pre-built display list to a `DrawingVisual`.

---

_Source: [`src/Chuvadi.Pdf.Rendering.Wpf/WpfRenderer.cs`](../../../src/Chuvadi.Pdf.Rendering.Wpf/WpfRenderer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
