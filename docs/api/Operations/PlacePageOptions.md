# PlacePageOptions

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Optional per-placement controls for `PageComposer.PlacePage(Chuvadi.Pdf.Documents.PdfDocument, int, Transform, PlacePageOptions)`.

```csharp
public sealed class PlacePageOptions
```

## Remarks

Both rectangles are in PDF user space (points, bottom-left origin) and are independent: `SourceClip` selects the region of the source page to import; `DestinationClip` confines the placed result on the target sheet. Either or both may be left `null`.

## Properties

### `DestinationClip`

```csharp
RectangleF? DestinationClip
```

Gets or sets the clip rectangle applied on the target sheet, in target (destination) user space. When set, the placed page is hard-clipped to this rectangle (a `re W n` clip outside the placement transform), so a page placed into an N-up cell cannot bleed into neighbouring cells. `null` places without a destination clip.

### `SourceClip`

```csharp
RectangleF? SourceClip
```

Gets or sets the crop rectangle applied to the source page, in source user space. When set, only this region of the source is imported: the placed form XObject's `BBox` is set to this rectangle (rather than the source crop box), so content outside it is clipped at import time. `null` imports the full source crop box.

---

_Source: [`src/Chuvadi.Pdf.Operations/PlacePageOptions.cs`](../../../src/Chuvadi.Pdf.Operations/PlacePageOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
