# PdfMeasure

**Class** in `Chuvadi.Pdf.Documents` (Documents)

A measure dictionary (`/Measure`): how distances and areas in a region of a page map to real-world units — for example a 1:100 floor plan. Rectilinear measures (`/Subtype /RL`, the default) are converted by `Distance` and `Area`; geospatial measures (`/GEO`) are reported but not converted.

```csharp
public sealed class PdfMeasure
```

## Properties

### `Subtype`

```csharp
string Subtype
```

Gets the measure subtype: `"RL"` (rectilinear) or `"GEO"`.

### `IsRectilinear`

```csharp
bool IsRectilinear => Subtype == "RL"
```

Gets whether this is a rectilinear measure that the conversions support.

### `ScaleRatio`

```csharp
string? ScaleRatio
```

Gets the human-readable scale ratio (`/R`), for example `"1 in = 10 ft"`.

### `X`

```csharp
IReadOnlyList<PdfNumberFormat> X
```

Gets the x-axis unit chain (`/X`).

### `Y`

```csharp
IReadOnlyList<PdfNumberFormat> Y
```

Gets the y-axis unit chain (`/Y`; the x chain when absent).

### `DistanceFormats`

```csharp
IReadOnlyList<PdfNumberFormat> DistanceFormats
```

Gets the distance unit chain (`/D`; the x chain when absent).

### `AreaFormats`

```csharp
IReadOnlyList<PdfNumberFormat> AreaFormats
```

Gets the area unit chain (`/A`; empty when absent).

### `OriginX`

```csharp
double OriginX
```

Gets the origin x (`/O`) in default user space, for coordinate readouts.

### `OriginY`

```csharp
double OriginY
```

Gets the origin y (`/O`) in default user space.

### `YToX`

```csharp
double YToX
```

Gets the y-to-x unit factor (`/CYX`), 1 when absent.

## Methods

### `FromDictionary`

__static__

```csharp
static PdfMeasure? FromDictionary(PdfPrimitive value, IPdfObjectResolver resolver)
```

Reads a measure dictionary (from a viewport, or an annotation's `/Measure` entry). Returns null when the value is not a dictionary or a rectilinear measure has no x-axis format.

**Parameters**

- `value` — The `/Measure` value.
- `resolver` — Resolves indirect references.

**Returns:** The measure, or null. <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>

### `Distance`

```csharp
PdfMeasurement Distance(double x1, double y1, double x2, double y2)
```

Converts a straight-line distance between two points in default user space into the drawing's units (the `/D` chain).

**Parameters**

- `x1` — First point x, in default user space.
- `y1` — First point y.
- `x2` — Second point x.
- `y2` — Second point y.

**Returns:** The measured distance. <exception cref="InvalidOperationException">Thrown when the measure is not rectilinear.</exception>

### `Area`

```csharp
PdfMeasurement Area(double userSpaceArea)
```

Converts an area in square default-user-space units into the drawing's units: the `/A` chain when present, otherwise the x and y factors (labelled with the x unit, squared).

**Parameters**

- `userSpaceArea` — The area in square default user-space units.

**Returns:** The measured area. <exception cref="InvalidOperationException">Thrown when the measure is not rectilinear.</exception>

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfMeasure.cs`](../../../src/Chuvadi.Pdf.Documents/PdfMeasure.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
