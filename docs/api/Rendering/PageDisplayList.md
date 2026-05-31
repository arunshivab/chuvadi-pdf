# PageDisplayList

**Class** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

An immutable, renderer-neutral representation of a PDF page's drawable content.

```csharp
public sealed class PageDisplayList
```

## Remarks

A `PageDisplayList` is the output of the content-stream builder (added in D3c-2) and the input of any renderer: the existing pixel rasterizer, an SVG writer, a PDF/UA accessibility walker, etc.  

 Coordinate space: PDF user space (Y up, origin at the bottom-left of the MediaBox, units of 1/72 inch). DPI scaling and Y-flipping happen in the renderer, not the display list — which means the same list can be re-rendered at any zoom level without rebuilding.  

 `PageWidth` and `PageHeight` are the MediaBox dimensions in points. They are advisory information for the renderer (e.g. for sizing the pixel buffer); the ops themselves are not clipped to the page rectangle.  

 Page rotation (the PDF /Rotate entry) is NOT baked into the ops here. A renderer that honours rotation applies an outer transform of the appropriate multiple of 90°.

## Constructors

### `PageDisplayList(IReadOnlyList<RenderOp> ops, double pageWidth, double pageHeight)`

Initialises a `PageDisplayList` by defensively copying `ops`.

**Parameters**

- `ops` — The render operations, in paint order.
- `pageWidth` — The MediaBox width in PDF user-space points. Must be non-negative.
- `pageHeight` — The MediaBox height in PDF user-space points. Must be non-negative. <exception cref="ArgumentNullException"> Thrown when `ops` is null. </exception> <exception cref="ArgumentException"> Thrown when `ops` contains a null entry. </exception> <exception cref="ArgumentOutOfRangeException"> Thrown when `pageWidth` or `pageHeight` is negative. </exception>

## Properties

### `Ops`

```csharp
IReadOnlyList<RenderOp> Ops
```

Gets the render operations in paint order.

### `PageWidth`

```csharp
double PageWidth
```

Gets the MediaBox width in PDF user-space points.

### `PageHeight`

```csharp
double PageHeight
```

Gets the MediaBox height in PDF user-space points.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/PageDisplayList.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/Raster/PageDisplayList.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
