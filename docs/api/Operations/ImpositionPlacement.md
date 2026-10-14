# ImpositionPlacement

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Where one source page lands on an imposition sheet.

```csharp
public sealed class ImpositionPlacement
```

## Properties

### `SourcePageIndex`

```csharp
int SourcePageIndex
```

Gets the zero-based source page index.

### `Transform`

```csharp
Transform Transform
```

Gets the transform from source page user space to sheet space (crop box and `/Rotate` already folded in; see `Imposition.FitTransform`).

### `Cell`

```csharp
RectangleF Cell
```

Gets the cell rectangle on the sheet (bottom-left origin, points). The placed page is clipped to it.

---

_Source: [`src/Chuvadi.Pdf.Operations/Imposition.cs`](../../../src/Chuvadi.Pdf.Operations/Imposition.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
