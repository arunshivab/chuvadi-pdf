# ClipOp

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Pushes a clipping region.

```csharp
public sealed class ClipOp : RenderOp
```

## Properties

### `Kind`

```csharp
override RenderOpKind Kind => RenderOpKind.Clip
```

<inheritdoc />

### `Geometry`

```csharp
required PathGeometry Geometry
```

The clipping path.

### `FillRule`

```csharp
FillRule FillRule
```

Fill rule for the clip region.

### `Stroke`

```csharp
StrokeStyle? Stroke
```

When set, the clip region is the area a stroke of `Geometry` in this style would paint (its outline), not the path's interior: content after the clip shows only where the stroke would. Used for a stroke whose colour is a pattern. `FillRule` does not apply.

## Methods

### `StrokeReach`

__static__

```csharp
static double StrokeReach(StrokeStyle style)
```

How far a stroke in `style` can reach beyond its path (page units): half the line width, scaled by the miter limit (or the diagonal of a square cap, whichever is larger).

**Parameters**

- `style` — The stroke style.

**Returns:** The reach.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
