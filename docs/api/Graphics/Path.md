# Path

**Class** in `Chuvadi.Pdf.Graphics` (Graphics)

A mutable vector graphics path built from moveto, lineto, curve, and close operations.

```csharp
public sealed class Path
```

## Remarks

A `Path` is a sequence of `PathSegment` values. Sub-paths begin with a MoveTo segment and end with a ClosePath segment or the next MoveTo. Empty paths produce no output when painted. PDF 32000-1:2008 §8.5.2 — Path construction operators. PDF 32000-1:2008 §8.5.3 — Path painting operators.

## Constructors

### `Path()`

Initialises an empty path.

## Properties

### `Segments`

```csharp
IReadOnlyList<PathSegment> Segments => _segments
```

Gets the segments that make up this path.

### `Count`

```csharp
int Count => _segments.Count
```

Gets the number of segments.

### `IsEmpty`

```csharp
bool IsEmpty => _segments.Count == 0
```

Returns true when the path contains no segments.

## Methods

### `MoveTo`

```csharp
Path MoveTo(double x, double y)
```

Begins a new sub-path at the given point. PDF operator 'm'. PDF 32000-1:2008 §8.5.2.1.

### `MoveTo`

```csharp
Path MoveTo(PointF point) => MoveTo(point.X, point.Y)
```

Begins a new sub-path at the given point.

### `LineTo`

```csharp
Path LineTo(double x, double y)
```

Appends a straight line from the current point to (x, y). PDF operator 'l'. PDF 32000-1:2008 §8.5.2.2.

### `LineTo`

```csharp
Path LineTo(PointF point) => LineTo(point.X, point.Y)
```

Appends a line to the given point.

### `CubicBezierTo`

```csharp
Path CubicBezierTo(PointF cp1, PointF cp2, PointF endpoint)
```

Appends a cubic Bezier curve. PDF operator 'c': current point → cp1 → cp2 → endpoint. PDF 32000-1:2008 §8.5.2.3.

### `CubicBezierTo`

```csharp
Path CubicBezierTo(double cx1, double cy1, double cx2, double cy2, double ex, double ey)
```

Appends a cubic Bezier curve from coordinates.

### `ClosePath`

```csharp
Path ClosePath()
```

Closes the current sub-path with a line back to the start of the sub-path. PDF operator 'h'. PDF 32000-1:2008 §8.5.2.7.

### `Rectangle`

```csharp
Path Rectangle(RectangleF rect)
```

Appends a complete rectangle as a closed sub-path.

### `Rectangle`

```csharp
Path Rectangle(double x, double y, double width, double height)
```

Appends a complete rectangle from coordinates.

### `Ellipse`

```csharp
Path Ellipse(double cx, double cy, double rx, double ry)
```

Appends an ellipse approximated by four cubic Bezier curves. The Bezier approximation constant k ≈ 0.5523 is industry standard.

### `ArrowHead`

```csharp
Path ArrowHead(PointF tail, PointF tip, double headLength, double headWidth)
```

Appends a closed triangular arrowhead pointing at `tip` along the direction from `tail` (geometry from `ArrowGeometry.HeadPoints`). A zero-length arrow appends nothing.

**Parameters**

- `tail` — The start of the arrow shaft.
- `tip` — The point the arrowhead touches.
- `headLength` — Distance from the tip to the head's base.
- `headWidth` — Full width of the head's base.

**Returns:** This path, for chaining.

### `Clear`

```csharp
void Clear()
```

Removes all segments and resets the current point.

### `EndpointBounds`

```csharp
RectangleF EndpointBounds()
```

Returns the bounding box of all segment endpoints (not curve extrema). A full tight bound for curves requires flattening first.

---

_Source: [`src/Chuvadi.Pdf.Graphics/Path.cs`](../../../src/Chuvadi.Pdf.Graphics/Path.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
