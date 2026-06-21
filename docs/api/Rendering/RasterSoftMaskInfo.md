# RasterSoftMaskInfo

**Class** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

An active soft mask (ExtGState `/SMask`): the masking transparency group plus how to derive and place its per-pixel coverage.

```csharp
public sealed class RasterSoftMaskInfo
```

## Properties

### `Group`

```csharp
PageDisplayList Group
```

Gets the masking group's display list, in group-local space.

### `Composition`

```csharp
Transform Composition
```

Gets the group-local → page-space transform.

### `IsLuminosity`

```csharp
bool IsLuminosity
```

Gets a value indicating whether this is a luminosity mask (else alpha).

### `Backdrop`

```csharp
double Backdrop
```

Gets the backdrop luminosity for unpainted areas, in [0, 1].

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/RasterSoftMaskInfo.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/Raster/RasterSoftMaskInfo.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
