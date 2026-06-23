# PathOp

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Renders a path with fill and/or stroke.

```csharp
public sealed class PathOp : RenderOp
```

## Properties

### `Kind`

```csharp
override RenderOpKind Kind => RenderOpKind.Path
```

<inheritdoc />

### `Geometry`

```csharp
required PathGeometry Geometry
```

The path geometry to render.

### `RawGeometry`

```csharp
PathGeometry? RawGeometry
```

The path geometry in user space, exactly as authored in the content stream before the current transformation matrix was applied, or null when raw geometry was not retained. `Ctm` maps this onto `Geometry`: applying the CTM to each raw point yields the corresponding page-space point. Enables extraction at true scale and recovery of the original authored coordinates without inverting the CTM.

### `Ctm`

```csharp
AffineMatrix Ctm
```

The current transformation matrix in effect when this path was painted, mapping `RawGeometry` (user space) onto `Geometry` (page space). `AffineMatrix.Identity` when no raw geometry was retained. PDF 32000-1:2008 §8.3.4.

### `Mode`

```csharp
required PaintMode Mode
```

Paint mode (fill / stroke / both).

### `FillRule`

```csharp
FillRule FillRule
```

Fill rule.

### `FillColor`

```csharp
PdfColor FillColor
```

Fill color (only meaningful when Mode includes fill).

### `StrokeColor`

```csharp
PdfColor StrokeColor
```

Stroke color (only meaningful when Mode includes stroke).

### `Stroke`

```csharp
StrokeStyle? Stroke
```

Stroke style (only meaningful when Mode includes stroke).

### `FillOpacity`

```csharp
double FillOpacity
```

Constant fill opacity (ExtGState /ca), 0..1. Default 1 (opaque).

### `StrokeOpacity`

```csharp
double StrokeOpacity
```

Constant stroke opacity (ExtGState /CA), 0..1. Default 1 (opaque).

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
