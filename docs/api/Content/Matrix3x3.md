# Matrix3x3

**Struct** in `Chuvadi.Pdf.Content` (Content)

A 3x3 matrix used for 2D affine transformations in PDF user space. PDF uses the form [a b c d e f] representing the matrix: | a b 0 | | c d 0 | | e f 1 | PDF 32000-1:2008 §8.3.3 — Transformation matrices.

```csharp
public readonly struct Matrix3x3
```

## Constructors

### `Matrix3x3(double a, double b, double c, double d, double e, double f)`

Initialises a matrix with the given six components.

## Properties

### `Identity`

__static__

```csharp
static Matrix3x3 Identity
```

Gets the identity matrix.

### `A`

```csharp
double A
```

Horizontal scaling component.

### `B`

```csharp
double B
```

Horizontal shearing component.

### `C`

```csharp
double C
```

Vertical shearing component.

### `D`

```csharp
double D
```

Vertical scaling component.

### `E`

```csharp
double E
```

Horizontal translation component.

### `F`

```csharp
double F
```

Vertical translation component.

### `TranslationX`

```csharp
double TranslationX => E
```

Gets the X translation (horizontal position).

### `TranslationY`

```csharp
double TranslationY => F
```

Gets the Y translation (vertical position).

## Methods

### `Multiply`

```csharp
Matrix3x3 Multiply(Matrix3x3 other)
```

Multiplies two transformation matrices (this × other). PDF 32000-1:2008 §8.3.3.

### `Translate`

```csharp
Matrix3x3 Translate(double tx, double ty)
```

Translates by (tx, ty) in the current coordinate system.

---

_Source: [`src/Chuvadi.Pdf.Content/GraphicsState.cs`](../../../src/Chuvadi.Pdf.Content/GraphicsState.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
