# ShadeOp

**Class** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

Paints an axial or radial shading (the `sh` operator) across the active clip region. Geometry is in page space with the CTM already applied.

```csharp
public sealed class ShadeOp : RenderOp
```

## Properties

### `IsRadial`

```csharp
bool IsRadial
```

Gets a value indicating whether this is a radial shading.

### `X0`

```csharp
double X0
```

Gets the start point / inner-circle centre x in page space.

### `Y0`

```csharp
double Y0
```

Gets the start point / inner-circle centre y in page space.

### `X1`

```csharp
double X1
```

Gets the end point / outer-circle centre x in page space.

### `Y1`

```csharp
double Y1
```

Gets the end point / outer-circle centre y in page space.

### `R0`

```csharp
double R0
```

Gets the inner circle radius in page space (radial only).

### `R1`

```csharp
double R1
```

Gets the outer circle radius in page space (radial only).

### `ExtendStart`

```csharp
bool ExtendStart
```

Gets whether the shading extends before the axis start.

### `ExtendEnd`

```csharp
bool ExtendEnd
```

Gets whether the shading extends past the axis end.

### `Stops`

```csharp
IReadOnlyList<GradientStop> Stops
```

Gets the sampled colour stops in increasing offset order.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/ShadeOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/Raster/ShadeOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
