# ShadeOp

**Class** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

Paints an axial or radial shading (the `sh` operator) across the active clip region. Geometry is in page space with the CTM already applied.

```csharp
public sealed class ShadeOp : RenderOp
```

## Constructors

### `ShadeOp(bool isRadial, double x0, double y0, double x1, double y1, double r0, double r1, bool extendStart, bool extendEnd, IReadOnlyList<GradientStop> stops, IReadOnlyList<ClipPath>? clips = null)`

Initialises a `ShadeOp`.

**Parameters**

- `isRadial` — True for a radial shading; false for axial.
- `x0` — Start point / inner-circle centre x, page space.
- `y0` — Start point / inner-circle centre y, page space.
- `x1` — End point / outer-circle centre x, page space.
- `y1` — End point / outer-circle centre y, page space.
- `r0` — Inner circle radius in page space (radial only).
- `r1` — Outer circle radius in page space (radial only).
- `extendStart` — Whether the shading extends before the axis start.
- `extendEnd` — Whether the shading extends past the axis end.
- `stops` — Sampled colour stops in increasing offset order.
- `clips` — Clip paths active when this op was emitted. <exception cref="ArgumentNullException">Thrown when `stops` is null.</exception>

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

### `ShadingToPage`

```csharp
Transform? ShadingToPage
```

When set, the centres and radii are in shading space and this maps shading space to page space. Used for a radial shading under a transform that does not keep circles circular (unequal scaling or skew), where the circles become ellipses on the page. Null when the geometry is already in page space.

---

_Source: [`src/Chuvadi.Pdf.Rendering.Raster/ShadeOp.cs`](../../../src/Chuvadi.Pdf.Rendering.Raster/ShadeOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
