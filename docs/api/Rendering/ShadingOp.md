# ShadingOp

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Paints an axial or radial shading (the `sh` operator). Coordinates and radii are in page space (the CTM at the `sh` operator has already been applied); the renderer fills the current clip region with the gradient.

```csharp
public sealed class ShadingOp : RenderOp
```

## Properties

### `Kind`

```csharp
override RenderOpKind Kind => RenderOpKind.Shading
```

<inheritdoc />

### `IsRadial`

```csharp
required bool IsRadial
```

True for a radial shading; false for axial (linear).

### `X0`

```csharp
double X0
```

Start point x in page space (axial/radial circle 0 centre).

### `Y0`

```csharp
double Y0
```

Start point y in page space.

### `X1`

```csharp
double X1
```

End point x in page space (axial/radial circle 1 centre).

### `Y1`

```csharp
double Y1
```

End point y in page space.

### `R0`

```csharp
double R0
```

Radius of circle 0 in page space (radial only).

### `R1`

```csharp
double R1
```

Radius of circle 1 in page space (radial only).

### `ExtendStart`

```csharp
bool ExtendStart
```

Whether the shading extends beyond the start of the axis.

### `ExtendEnd`

```csharp
bool ExtendEnd
```

Whether the shading extends beyond the end of the axis.

### `Stops`

```csharp
IReadOnlyList<ShadingStop> Stops
```

The sampled gradient stops, in increasing offset order.

### `ShadingToPage`

```csharp
AffineMatrix? ShadingToPage
```

When set, the centres and radii are in shading space and this maps shading space to page space. Used for a radial shading under a transform that does not keep circles circular (unequal scaling or skew), where the circles become ellipses on the page. Null when the geometry is already in page space.

### `ShadingType`

```csharp
int ShadingType
```

The `/ShadingType`, 1 to 7 (3.31); 2 (axial) or 3 (radial) for the ops the list draws.

### `Shading`

```csharp
PdfPrimitive? Shading
```

The shading dictionary, or for mesh types 4 to 7 the shading stream (3.31), so a printer can pass it on as it is (PostScript Level 3 and PCL accept all seven types). Null for ops built without one.

### `IsExpressible`

```csharp
bool IsExpressible
```

False for a shading the structured list cannot express as stops (function-based type 1, meshes 4 to 7, 3.31): only `ShadingType`, `Shading` and `ShadingToPage` are set, and SVG and WPF skip the op.

## Methods

### `GetShadingData`

```csharp
byte[]? GetShadingData()
```

For a mesh shading (types 4 to 7), its decoded stream data (3.31); otherwise null.

**Returns:** The data, or null.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
