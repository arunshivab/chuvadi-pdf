# PdfRectangle

**Struct** in `Chuvadi.Pdf.Documents` (Documents)

An immutable rectangle in PDF user space (points, 1/72 inch). Origin is bottom-left by PDF convention.

```csharp
public readonly struct PdfRectangle
```

## Constructors

### `PdfRectangle(double x1, double y1, double x2, double y2)`

Initialises a `PdfRectangle` from four coordinates.

## Properties

### `X1`

```csharp
double X1
```

Left edge (in PDF user space).

### `Y1`

```csharp
double Y1
```

Bottom edge (in PDF user space).

### `X2`

```csharp
double X2
```

Right edge (in PDF user space).

### `Y2`

```csharp
double Y2
```

Top edge (in PDF user space).

### `Width`

```csharp
double Width => Math.Abs(X2 - X1)
```

Width in points.

### `Height`

```csharp
double Height => Math.Abs(Y2 - Y1)
```

Height in points.

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfPage.cs`](../../../src/Chuvadi.Pdf.Documents/PdfPage.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
