# ShadingStop

**Struct** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

A single colour stop of a shading gradient.

```csharp
public readonly struct ShadingStop
```

## Constructors

### `ShadingStop(double offset, double r, double g, double b)`

Creates a gradient stop.

**Parameters**

- `offset` — Normalised position along the axis in [0, 1].
- `r` — Red in [0, 1].
- `g` — Green in [0, 1].
- `b` — Blue in [0, 1].

## Properties

### `Offset`

```csharp
double Offset
```

Normalised position along the axis in [0, 1].

### `R`

```csharp
double R
```

Red in [0, 1].

### `G`

```csharp
double G
```

Green in [0, 1].

### `B`

```csharp
double B
```

Blue in [0, 1].

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
