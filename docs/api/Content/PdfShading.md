# PdfShading

**Class** in `Chuvadi.Pdf.Content` (Content)

An axial (Type 2) or radial (Type 3) shading (PDF 32000-1:2008 §8.7.4.5). Use `Parse` to build one from a shading dictionary, then read `Coords` for geometry and `EvaluateRgb` for the colour at a normalised position along the axis.

```csharp
public sealed class PdfShading
```

## Properties

### `ShadingType`

```csharp
int ShadingType
```

The shading type: 2 (axial) or 3 (radial).

### `Coords`

```csharp
double[] Coords
```

Axis geometry. Axial: [x0, y0, x1, y1]. Radial: [x0, y0, r0, x1, y1, r1].

### `DomainStart`

```csharp
double DomainStart
```

Lower bound of the parametric domain (default 0).

### `DomainEnd`

```csharp
double DomainEnd
```

Upper bound of the parametric domain (default 1).

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

### `ColorSpace`

```csharp
PdfPrimitive? ColorSpace
```

The raw /ColorSpace entry, or null when absent.

### `IsAxial`

```csharp
bool IsAxial => ShadingType == 2
```

True for an axial (linear) shading.

### `IsRadial`

```csharp
bool IsRadial => ShadingType == 3
```

True for a radial shading.

## Methods

### `Parse`

__static__

```csharp
static PdfShading Parse(PdfPrimitive shading, PdfObjectStore objects)
```

Parses an axial or radial shading dictionary (or shading stream, whose dictionary is used).

**Parameters**

- `shading` — The shading object or reference.
- `objects` — The object store for resolving references.

**Returns:** The parsed shading. <exception cref="ContentException"> Thrown for an unsupported shading type or a malformed dictionary. </exception>

### `EvaluateColor`

```csharp
double[] EvaluateColor(double s)
```

Evaluates the shading colour at a normalised axis position `s` in [0, 1], which is mapped onto the parametric domain before the /Function is applied.

**Parameters**

- `s` — Normalised position along the axis, clamped to [0, 1].

**Returns:** The colour components in the shading's colour space.

---

_Source: [`src/Chuvadi.Pdf.Content/PdfShading.cs`](../../../src/Chuvadi.Pdf.Content/PdfShading.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
