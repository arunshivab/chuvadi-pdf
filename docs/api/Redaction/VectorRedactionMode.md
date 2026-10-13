# VectorRedactionMode

**Enum** in `Chuvadi.Pdf.Redaction` (Redaction)

How painted vector paths (fills and strokes) that intersect a redaction region are removed from the content stream.

```csharp
public enum VectorRedactionMode
```

## Remarks

Both modes are byte-level safe: every piece of path geometry whose ink can touch a redaction rectangle is physically removed from the content stream, never merely covered or clipped. The modes differ only in how much of the surrounding, untouched geometry survives.

## Values

| Name | Description |
|---|---|
| `SubtractPrecise` | Geometry inside a redaction region is subtracted at the finest granularity that is provably safe: subpaths whose ink cannot touch any redaction rectangle are kept verbatim; straight stroke segments crossing a rectangle are split and only the outside pieces survive; axis-aligned rectangle fills are decomposed into the surviving sub-rectangles. Any geometric doubt (curves crossing a region, non-trivial fill-rule interactions, non-invertible matrices) falls back to dropping the construct whole, so precision never costs safety. This is the default. |
| `DropIntersecting` | The pre-3.18 behaviour: a painted path whose bounding box intersects any redaction rectangle is dropped in its entirety, however large the path is relative to the rectangle (a full-page border drawn as one path vanishes when one cell is redacted). |

---

_Source: [`src/Chuvadi.Pdf.Redaction/VectorRedactionMode.cs`](../../../src/Chuvadi.Pdf.Redaction/VectorRedactionMode.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
