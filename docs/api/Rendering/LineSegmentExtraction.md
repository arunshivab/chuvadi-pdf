# LineSegmentExtraction

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Extension accessor that presents a page's path content as a flat list of `LineSegment`s, flattening cubic curves to polylines.

```csharp
public static class LineSegmentExtraction
```

## Fields

### `DefaultFlattenTolerance`

```csharp
const double DefaultFlattenTolerance = 0.25
```

Default curve-flattening tolerance, in user-space units.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/LineSegmentExtraction.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/LineSegmentExtraction.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
