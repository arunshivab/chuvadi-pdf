# OpTransparency

**Record** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

An op's own transparency inside its innermost group (3.31): the values the group's content set, starting from the defaults at the group's start (PDF 32000-1:2008 §11.6.6).

```csharp
public sealed record OpTransparency(double FillOpacity, double StrokeOpacity, PdfBlendMode BlendMode, SoftMaskInfo? SoftMask)
```

## Parameters

- `FillOpacity` — The non-stroking constant alpha.
- `StrokeOpacity` — The stroking constant alpha.
- `BlendMode` — The blend mode.
- `SoftMask` — The soft mask, or null.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/TransparencyGroup.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/TransparencyGroup.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
