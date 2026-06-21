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

Maps a PDF blend-mode name to a `PdfBlendMode`, returning `PdfBlendMode.Normal` for unknown or non-separable names.

**Parameters**

- `name` — The blend-mode name, without the leading slash.

**Returns:** The mapped blend mode.

### `TryFromName`

__static__

```csharp
static bool TryFromName(string name, out PdfBlendMode mode)
```

Attempts to map a PDF blend-mode name to a supported separable `PdfBlendMode`.

**Parameters**

- `name` — The blend-mode name, without the leading slash.
- `mode` — The mapped blend mode when supported.

**Returns:** True when `name` is a supported separable mode; false for Normal, Compatible, the non-separable modes, or any unknown name.

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

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/BlendModes.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/BlendModes.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
