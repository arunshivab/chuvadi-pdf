# HintingMode

**Enum** in `Chuvadi.Pdf.Rendering` (Rendering)

Controls how strongly the TrueType bytecode hinting interpreter adjusts glyph outlines before rasterization.

```csharp
public enum HintingMode
```

## Values

| Name | Description |
|---|---|
| `Off` | No hinting: outlines are scaled and rendered as-is. |
| `Light` | Light hinting: grid-fit the vertical (Y) axis only, leaving horizontal positions at their naturally scaled values. This keeps baselines and stem heights crisp without the horizontal stem snapping that can look heavy under grayscale anti-aliasing. Recommended for anti-aliased output. |
| `Full` | Full classic hinting: execute the complete bytecode interpreter on both axes. Best for black-and-white or very low-resolution output. |

---

_Source: [`src/Chuvadi.Pdf.Rendering/RenderOptions.cs`](../../../src/Chuvadi.Pdf.Rendering/RenderOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
