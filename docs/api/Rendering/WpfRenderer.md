# WpfRenderer

**Class** in `Chuvadi.Pdf.Rendering.Wpf` (Rendering)

Renders a `PageDisplayList` into a WPF `DrawingVisual`.

```csharp
public sealed class WpfRenderer
```

## Remarks

Translates each `RenderOp` into WPF drawing primitives via `DrawingContext`. `PathOp` becomes a `StreamGeometry` drawn with `DrawingContext.DrawGeometry`; `TextOp` becomes a `FormattedText` drawn with `DrawingContext.DrawText`; `ImageOp` becomes a `BitmapSource` drawn with `DrawingContext`.  

 Coordinate handling: the renderer applies an outer `(1, 0, 0, -1, 0, pageHeight)` transform so PDF coordinates flow through directly. Text runs receive a local counter-flip so glyphs read upright.

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
