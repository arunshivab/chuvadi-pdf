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

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
