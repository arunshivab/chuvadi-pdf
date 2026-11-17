# BlendModes

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Helpers for the PDF blend-mode (/BM) names.

```csharp
public static class BlendModes
```

## Methods

### `FromName`

__static__

```csharp
static PdfBlendMode FromName(string name)
```

Maps a PDF blend-mode name to a `PdfBlendMode`, returning `PdfBlendMode.Normal` for Compatible and unknown names (and, before 3.35.0, for the non-separable names).

**Parameters**

- `name` — The blend-mode name, without the leading slash.

**Returns:** The mapped blend mode.

### `TryFromName`

__static__

```csharp
static bool TryFromName(string name, out PdfBlendMode mode)
```

Attempts to map a PDF blend-mode name to a supported `PdfBlendMode` other than Normal.

**Parameters**

- `name` — The blend-mode name, without the leading slash.
- `mode` — The mapped blend mode when supported.

**Returns:** True when `name` is a supported mode other than Normal (the non-separable modes since 3.35.0); false for Normal, Compatible, or any unknown name.

### `Blend`

__static__

```csharp
static double Blend(PdfBlendMode mode, double cb, double cs)
```

Applies a separable blend function to a single colour channel, per PDF §11.3.5 / the W3C compositing model. Operands are in [0, 1].

**Parameters**

- `mode` — The blend mode (Normal returns the source unchanged).
- `cb` — The backdrop channel value, in [0, 1].
- `cs` — The source channel value, in [0, 1].

**Returns:** The blended channel value, in [0, 1].

### `BlendRgb`

__static__

```csharp
static (double R, double G, double B) BlendRgb(PdfBlendMode mode, (double R, double G, double B) backdrop, (double R, double G, double B) source)
```

Blends an RGB source colour with an RGB backdrop, per PDF §11.3.5: channel by channel for the separable modes, and with the non-separable formulas of §11.3.5.3 for Hue, Saturation, Color and Luminosity. Components are in [0, 1]. Since 3.35.0.

**Parameters**

- `mode` — The blend mode.
- `backdrop` — The backdrop colour.
- `source` — The source colour.

**Returns:** The blended colour.

---

_Source: [`src/Chuvadi.Pdf.Rendering.Walking/BlendModes.cs`](../../../src/Chuvadi.Pdf.Rendering.Walking/BlendModes.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
