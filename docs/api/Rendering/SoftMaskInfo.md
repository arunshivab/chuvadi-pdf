# SoftMaskInfo

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

An active soft mask (ExtGState `/SMask`) for the SVG path: the masking group's display list plus how its coverage is derived and placed.

```csharp
public sealed class SoftMaskInfo
```

## Constructors

### `SoftMaskInfo(PageDisplayList group, AffineMatrix composition, bool isLuminosity, double backdrop)`

Initialises a `SoftMaskInfo`.

**Parameters**

- `group` — The masking group's display list, in group-local space.
- `composition` — Group-local → page-space transform (the CTM when the mask was set).
- `isLuminosity` — True for a luminosity mask; false for an alpha mask.
- `backdrop` — Backdrop luminosity for areas the group does not paint, in [0, 1].

## Properties

### `Group`

```csharp
PageDisplayList Group
```

Gets the masking group's display list, in group-local space.

### `Composition`

```csharp
AffineMatrix Composition
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

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/SoftMaskInfo.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/SoftMaskInfo.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
