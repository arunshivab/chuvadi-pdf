# PdfViewport

**Class** in `Chuvadi.Pdf.Documents` (Documents)

A viewport (`/VP` entry): a rectangular region of a page with its own name and measurement scale.

```csharp
public sealed class PdfViewport
```

## Properties

### `Bounds`

```csharp
PdfRectangle Bounds
```

Gets the region (`/BBox`) in default user space.

### `Name`

```csharp
string? Name
```

Gets the viewport's name (`/Name`), or null.

### `Measure`

```csharp
PdfMeasure? Measure
```

Gets the region's measure (`/Measure`), or null when it has none.

## Methods

### `Contains`

```csharp
bool Contains(double x, double y)
```

Gets whether the point (default user space) lies inside the region.

**Parameters**

- `x` — Point x.
- `y` — Point y.

**Returns:** Whether the point is inside (edges included).

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfMeasure.cs`](../../../src/Chuvadi.Pdf.Documents/PdfMeasure.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
