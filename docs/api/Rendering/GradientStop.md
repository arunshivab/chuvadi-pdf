# GradientStop

**Struct** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

A single sampled gradient stop: an offset in [0, 1] and its colour.

```csharp
public readonly struct GradientStop
```

## Constructors

### `GradientStop(double offset, ColorF color)`

Initialises a `GradientStop`.

**Parameters**

- `offset` — The normalised offset along the axis, in [0, 1].
- `color` — The colour at this offset.

## Properties

### `Offset`

```csharp
double Offset
```

Gets the normalised offset along the axis, in [0, 1].

### `Color`

```csharp
ColorF Color
```

Gets the colour at this offset.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/ShadeOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/Raster/ShadeOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
