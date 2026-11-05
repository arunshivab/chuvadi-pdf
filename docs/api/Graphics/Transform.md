# Transform

**Struct** in `Chuvadi.Pdf.Graphics` (Graphics)

An immutable 2D affine transformation matrix.

```csharp
public readonly struct Transform : IEquatable<Transform>
```

## Remarks

PDF represents a 2D affine matrix as six values [a b c d e f]: 
```
 | a  b  0 | | c  d  0 | | e  f  1 | 
```
 This maps user-space coordinates to device space via: x' = a*x + c*y + e y' = b*x + d*y + f PDF 32000-1:2008 §8.3.3 — Transformation matrices.

## Constructors

### `Transform(double a, double b, double c, double d, double e, double f)`

Initialises a transform from the six affine components.

## Properties

### `A`

```csharp
double A
```

Horizontal scaling / cosine of rotation.

### `B`

```csharp
double B
```

Horizontal shearing / sine of rotation.

### `C`

```csharp
double C
```

Vertical shearing / negative sine of rotation.

### `D`

```csharp
double D
```

Vertical scaling / cosine of rotation.

### `E`

```csharp
double E
```

Horizontal translation (X offset).

### `F`

```csharp
double F
```

Vertical translation (Y offset).

### `Identity`

__static__

```csharp
static Transform Identity
```

The identity matrix [1 0 0 1 0 0].

### `Translation`

```csharp
PointF Translation => new PointF(E, F)
```

Gets the translation component as a point.

## Methods

### `CreateTranslation`

__static__

```csharp
static Transform CreateTranslation(double tx, double ty)
```

Creates a translation matrix.

### `CreateScale`

__static__

```csharp
static Transform CreateScale(double scale)
```

Creates a uniform scaling matrix.

### `CreateScale`

__static__

```csharp
static Transform CreateScale(double sx, double sy)
```

Creates a non-uniform scaling matrix.

### `CreateRotation`

__static__

```csharp
static Transform CreateRotation(double radians)
```

Creates a rotation matrix. PDF 32000-1:2008 §8.3.4 — Rotation.

**Parameters**

- `radians` — Angle in radians, counter-clockwise.

### `CreateRotationDegrees`

__static__

```csharp
static Transform CreateRotationDegrees(double degrees)
```

Creates a rotation matrix from degrees.

### `Multiply`

```csharp
Transform Multiply(Transform other)
```

Concatenates this matrix with `other` (this × other). PDF 32000-1:2008 §8.3.3 — Matrix multiplication.

### `TransformPoint`

```csharp
PointF TransformPoint(PointF p)
```

Applies this transform to a point.

### `TransformVector`

```csharp
PointF TransformVector(double dx, double dy)
```

Applies only the linear part (no translation) to a vector.

### `Invert`

```csharp
Transform Invert()
```

Returns the inverse of this transform. <exception cref="InvalidOperationException"> Thrown when the matrix is singular (determinant is zero). </exception>

### `Translate`

```csharp
Transform Translate(double tx, double ty)
```

Returns a copy of this transform with a translation prepended.

### `==`

__static__

```csharp
static bool operator ==(Transform left, Transform right) => left.Equals(right)
```

Equality operator.

### `!=`

__static__

```csharp
static bool operator !=(Transform left, Transform right) => !left.Equals(right)
```

Inequality operator.

### `*`

__static__

```csharp
static Transform operator *(Transform left, Transform right) => left.Multiply(right)
```

Matrix multiplication operator.

---

_Source: [`src/Chuvadi.Pdf.Graphics/Transform.cs`](../../../src/Chuvadi.Pdf.Graphics/Transform.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
