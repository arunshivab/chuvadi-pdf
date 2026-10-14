# ImpositionSheet

**Class** in `Chuvadi.Pdf.Operations` (Operations)

One output sheet (or booklet sheet side) in an imposition plan.

```csharp
public sealed class ImpositionSheet
```

## Properties

### `Width`

```csharp
double Width
```

Gets the sheet width, in points.

### `Height`

```csharp
double Height
```

Gets the sheet height, in points.

### `Placements`

```csharp
IReadOnlyList<ImpositionPlacement> Placements
```

Gets the source pages placed on this sheet, in drawing order.

---

_Source: [`src/Chuvadi.Pdf.Operations/Imposition.cs`](../../../src/Chuvadi.Pdf.Operations/Imposition.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
