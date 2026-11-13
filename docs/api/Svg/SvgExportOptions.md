# SvgExportOptions

**Class** in `Chuvadi.Pdf.Svg` (Svg)

Options for PDF → SVG export.

```csharp
public sealed class SvgExportOptions
```

## Properties

### `InlineImages`

```csharp
bool InlineImages
```

Embed images as base64 data URLs (default: true).

### `TextStrategy`

```csharp
SvgTextStrategy TextStrategy
```

Text rendering strategy. Defaults to `SvgTextStrategy.Selectable`.

### `FontStrategy`

```csharp
SvgFontStrategy FontStrategy
```

Font embedding strategy. Defaults to `SvgFontStrategy.EmbedAsWebFont`.

### `Precision`

```csharp
int Precision
```

Number of decimal places for emitted coordinates. Default 4.

### `PrettyPrint`

```csharp
bool PrettyPrint
```

Indent the SVG output (default: false, compact).

### `AnnotationMode`

```csharp
Chuvadi.Pdf.Documents.AnnotationMode AnnotationMode
```

Which annotations are drawn (3.31): View (the default), Print, FormFieldsOnly or None. Used by `SvgRenderer`.

### `Background`

```csharp
ColorF? Background
```

Opaque colour for the page sheet, emitted as a full-page background rectangle behind all content. Defaults to `ColorF.White`, matching the rasteriser's default white paper. Set to `null` for a transparent SVG (useful when compositing over another background).

### `PathSimplifyTolerance`

```csharp
double PathSimplifyTolerance
```

Tolerance (page points) for simplifying straight-segment runs in path geometry (Ramer–Douglas–Peucker) before emission. `0` (default) disables simplification. Curves and clip paths are never simplified. Typical values: 0.1–0.5 for CAD-style line work.

### `MaxVectorOps`

```csharp
int MaxVectorOps
```

Budget on the number of vector painting operations (paths and shadings) per page before `DensityFallback` kicks in. `0` (default) means unlimited — every page renders fully vector.

### `DensityFallback`

```csharp
SvgDensityFallback DensityFallback
```

What happens when a page exceeds `MaxVectorOps`. Defaults to `SvgDensityFallback.Simplify`.

### `RasterFallbackDpi`

```csharp
double RasterFallbackDpi
```

Resolution used when `DensityFallback` is `SvgDensityFallback.Rasterize` and the budget is exceeded. Default: 150 DPI.

### `Rotation`

```csharp
int Rotation
```

Clockwise display rotation applied to the whole rendered page, in degrees: 0 (default), 90, 180 or 270. The SVG's width, height and `viewBox` are emitted for the rotated orientation (swapped for 90 and 270) and the page content is rotated inside it, so the output can be shown as-is without editing the SVG. It is a view rotation; the page's own `/Rotate` is added only when `ApplyPageRotation` is set. Values are normalised modulo 360 (so -90 means 270). <exception cref="ArgumentOutOfRangeException"> Thrown when the value is not a multiple of 90. </exception>

### `ApplyPageRotation`

```csharp
bool ApplyPageRotation
```

Whether the page's own `/Rotate` entry is applied, so the page renders upright the way PDF viewers display it. It adds to `Rotation`. Default: false (the page renders in its unrotated user space, as in earlier releases).

### `IgnoreOptionalContent`

```csharp
bool IgnoreOptionalContent
```

Whether optional content (layers) is ignored, so every layer is drawn whatever its state. Default: false — layers hidden in the document's default configuration are left out, as in Acrobat.

### `LayerVisibility`

```csharp
IReadOnlyDictionary<string, bool>? LayerVisibility
```

Per-layer visibility overrides (layer name → visible) on top of the document's default configuration. Ignored when `IgnoreOptionalContent` is true. Default: null.

### `LayerVisibilityById`

```csharp
IReadOnlyDictionary<int, bool>? LayerVisibilityById
```

Gets or initialises per-layer visibility overrides by layer id (3.32.0): `Chuvadi.Pdf.Documents.OptionalContentGroup.Id` → visible. They win over `LayerVisibility` for the same group, and set apart layers that share a name. Ignored when `IgnoreOptionalContent` is true. Default: null.

### `DisplayBox`

```csharp
Chuvadi.Pdf.Documents.PdfDisplayBox DisplayBox
```

Gets or initialises which page box the SVG shows (3.32.0, P2). Default: `Chuvadi.Pdf.Documents.PdfDisplayBox.CropBox` — the CropBox intersected with the MediaBox, what a viewer shows: the SVG is that box's size and its viewBox starts at the box's corner, so content outside it is not shown. `Chuvadi.Pdf.Documents.PdfDisplayBox.MediaBox` shows the whole MediaBox, as before 3.32.0. A display list not built from a page (no boxes) uses its MediaWidth and MediaHeight either way.

---

_Source: [`src/Chuvadi.Pdf.Svg/SvgExportOptions.cs`](../../../src/Chuvadi.Pdf.Svg/SvgExportOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
