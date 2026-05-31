# ClipRegion

**Class** in `Chuvadi.Pdf.Rendering` (Rendering)

A device-space clipping region used by `ScanlineRasterizer` to restrict where a fill is painted.

```csharp
public sealed class ClipRegion
```

## Remarks

A region is built from one or more clip paths. Per PDF clipping semantics (PDF 32000-1:2008 §8.5.4), the effective clip is the <em>intersection</em> of every path: a pixel is inside the region only when it is inside all of them.  

 Each clip path is classified once at construction. Axis-aligned rectangles are stored as a single bounding interval and intersected with a cheap min/max test (the common `re W n` case). Non-rectangular paths are stored as edge tables and evaluated per scanline with the same edge-crossing logic used for filling, honouring the path's fill rule.

## Properties

### `IsEmpty`

```csharp
bool IsEmpty
```

Gets a value indicating whether the region excludes everything, in which case no pixel is ever painted.

## Methods

### `List<`

```csharp
List<(double Start, double End)> AllowedIntervals(double scanY)
```

Returns the allowed x-intervals at the given scanline Y (sampled at the pixel centre), as the intersection of every clip shape's intervals. An empty list means nothing is allowed on this row.

**Parameters**

- `scanY` — The scanline sample Y in device space.

**Returns:** Sorted, non-overlapping allowed intervals on this row.

---

_Source: [`src/Chuvadi.Pdf.Rendering/ClipRegion.cs`](../../../src/Chuvadi.Pdf.Rendering/ClipRegion.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
