# ArrowGeometry

**Class** in `Chuvadi.Pdf.Graphics` (Graphics)

Pure arrow geometry: the triangular head polygon at the tip of a line and the shortened shaft that meets the head's base. The same numbers drive `PageBuilder.DrawArrow` output and any on-screen preview, so a preview drawn from these points matches the authored PDF exactly. Coordinates are taken as given; the geometry is symmetric about the shaft, so it is valid in both y-up (PDF) and y-down (screen) coordinate systems.

```csharp
public static class ArrowGeometry
```

## Methods

### `DefaultHeadLength`

__static__

```csharp
static double DefaultHeadLength(double strokeWidth)
```

The default head length for a given stroke width: four line widths, never shorter than 6 units.

**Parameters**

- `strokeWidth` — The shaft stroke width.

**Returns:** The default head length.

### `DefaultHeadWidth`

__static__

```csharp
static double DefaultHeadWidth(double headLength)
```

The default head width (full base width) for a given head length: three quarters of the length.

**Parameters**

- `headLength` — The head length.

**Returns:** The default head width.

### `HeadPoints`

__static__

```csharp
static PointF[] HeadPoints(PointF tail, PointF tip, double headLength, double headWidth)
```

Computes the three corners of an arrowhead pointing at `tip` along the direction from `tail`. The returned array is `[tip, baseLeft, baseRight]`. The head length is clamped to the shaft length so a short arrow never overshoots its tail. Returns an empty array when the tail and tip coincide (no direction to point along).

**Parameters**

- `tail` — The start of the arrow shaft.
- `tip` — The point the arrowhead touches.
- `headLength` — Distance from the tip to the head's base.
- `headWidth` — Full width of the head's base.

**Returns:** The head polygon, or an empty array for a zero-length arrow. <exception cref="ArgumentOutOfRangeException"> Thrown when `headLength` or `headWidth` is negative or not finite. </exception>

### `ShaftEnd`

__static__

```csharp
static PointF ShaftEnd(PointF tail, PointF tip, double headLength)
```

The point where a shaft ending in an arrowhead should stop: the centre of the head's base. Stroking the shaft only to this point keeps the line cap from poking through the head's tip. Returns `tip` for a zero-length arrow.

**Parameters**

- `tail` — The start of the arrow shaft.
- `tip` — The point the arrowhead touches.
- `headLength` — Distance from the tip to the head's base.

**Returns:** The shortened shaft end point. <exception cref="ArgumentOutOfRangeException"> Thrown when `headLength` is negative or not finite. </exception>

---

_Source: [`src/Chuvadi.Pdf.Graphics/ArrowGeometry.cs`](../../../src/Chuvadi.Pdf.Graphics/ArrowGeometry.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
