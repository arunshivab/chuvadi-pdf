# SvgDensityFallback

**Enum** in `Chuvadi.Pdf.Svg` (Svg)

What happens when a page's vector-operation count exceeds `SvgExportOptions.MaxVectorOps`. Dense vector pages (CAD exports, large plots) can otherwise produce SVGs with tens of thousands of elements that overwhelm viewers.

```csharp
public enum SvgDensityFallback
```

## Values

| Name | Description |
|---|---|
| `Simplify` | Stay vector but force path simplification: straight-segment runs are simplified (Ramer–Douglas–Peucker) at `SvgExportOptions.PathSimplifyTolerance` or, when that is unset, an automatic 0.3-point tolerance. Reduces output size while keeping text selectable. Default. |
| `Rasterize` | Rasterize the whole page at `SvgExportOptions.RasterFallbackDpi` and emit a single `&lt;image&gt;` SVG instead of vector elements. Bounded output size for any density; text is no longer selectable. Only the document-based entry points (`RenderPage`) can rasterize — rendering a pre-built display list falls back to `Simplify`. |

---

_Source: [`src/Chuvadi.Pdf.Svg/SvgExportOptions.cs`](../../../src/Chuvadi.Pdf.Svg/SvgExportOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
