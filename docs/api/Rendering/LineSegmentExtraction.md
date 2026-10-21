# LineSegmentExtraction

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Extension accessor that presents a page's path content as a flat list of `LineSegment`s, flattening cubic curves to polylines.

```csharp
public static class LineSegmentExtraction
```

## Methods

### `ExtractLineSegments`

__static__

```csharp
static IReadOnlyList<LineSegment> ExtractLineSegments(this PageDisplayList list, double flattenTolerance = DefaultFlattenTolerance, bool skipDegenerate = true)
```

Extracts every straight segment of every `PathOp` on the page as a flat list, in draw order. Cubic curves are subdivided to within `flattenTolerance`; closed subpaths contribute their closing segment, so wall loops close. Each segment carries the source path's page-space and user-space endpoints, stroke width/colour/dash, optional-content layers, and paint mode.

**Parameters**

- `list` — The page display list to read.
- `flattenTolerance` — Maximum chord deviation when flattening curves, in user-space units. Non-positive values fall back to `DefaultFlattenTolerance`.
- `skipDegenerate` — When true (the default), zero-length segments — whose page-space endpoints coincide — are omitted. AutoCAD and similar exports emit many degenerate `MoveTo p; LineTo p` / `MoveTo p; Close` ops, so skipping keeps the result free of no-op noise. Pass false to retain every point pair verbatim.

**Returns:** All line segments on the page, in draw order. Never null. <exception cref="ArgumentNullException">`list` is null.</exception>

## Fields

### `DefaultFlattenTolerance`

```csharp
const double DefaultFlattenTolerance = 0.25
```

Default curve-flattening tolerance, in user-space units.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/LineSegmentExtraction.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/LineSegmentExtraction.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
