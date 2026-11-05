# RectangleF

**Struct** in `Chuvadi.Pdf.Graphics` (Graphics)

An immutable axis-aligned rectangle in PDF user space (points, 1/72 inch). Origin is bottom-left by PDF convention; Y increases upward.

```csharp
public readonly struct RectangleF : IEquatable<RectangleF>
```

## Constructors

### `RectangleF(double x, double y, double width, double height)`

Initialises a `RectangleF` from origin and size.

## Properties

### `X`

```csharp
double X
```

Left edge (X of origin).

### `Y`

```csharp
double Y
```

Bottom edge in PDF space (Y of origin).

### `Width`

```csharp
double Width
```

Width in PDF points.

### `Height`

```csharp
double Height
```

Height in PDF points.

### `Right`

```csharp
double Right => X + Width
```

Right edge (X + Width).

### `Top`

```csharp
double Top => Y + Height
```

Top edge in PDF space (Y + Height).

### `BottomLeft`

```csharp
PointF BottomLeft => new PointF(X, Y)
```

Bottom-left corner.

### `TopRight`

```csharp
PointF TopRight => new PointF(Right, Top)
```

Top-right corner.

### `Centre`

```csharp
PointF Centre => new PointF(X + Width / 2.0, Y + Height / 2.0)
```

Centre of the rectangle.

### `Size`

```csharp
SizeF Size => new SizeF(Width, Height)
```

Size of the rectangle.

### `Zero`

__static__

```csharp
static RectangleF Zero
```

Empty rectangle at origin.

### `IsEmpty`

```csharp
bool IsEmpty => Width == 0 || Height == 0
```

Returns true when Width or Height is zero.

## Methods

### `FromCorners`

__static__

```csharp
static RectangleF FromCorners(double x1, double y1, double x2, double y2)
```

Creates a `RectangleF` from two corner points. PDF MediaBox format: [x1 y1 x2 y2].

### `Intersect`

```csharp
RectangleF Intersect(RectangleF other)
```

Returns the intersection of this rectangle with `other`, or `Zero` when they do not intersect.

### `Contains`

```csharp
bool Contains(PointF point)
```

Returns whether `point` lies inside this rectangle.

### `Inflate`

```csharp
RectangleF Inflate(double amount)
```

Returns this rectangle expanded by `amount` on all sides.

### `==`

__static__

```csharp
static bool operator ==(RectangleF left, RectangleF right) => left.Equals(right)
```

Equality operator.

### `!=`

__static__

```csharp
static bool operator !=(RectangleF left, RectangleF right) => !left.Equals(right)
```

Inequality operator.

---

_Source: [`src/Chuvadi.Pdf.Graphics/RectangleF.cs`](../../../src/Chuvadi.Pdf.Graphics/RectangleF.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
