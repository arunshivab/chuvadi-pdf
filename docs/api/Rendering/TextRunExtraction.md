# TextRunExtraction

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Extension surface for extracting text runs from a `PageDisplayList`.

```csharp
public static class TextRunExtraction
```

## Methods

### `ExtractTextRuns`

__static__

```csharp
static IReadOnlyList<TextRun> ExtractTextRuns(this PageDisplayList list)
```

Extracts the page's text as a reading-order sequence of `TextRun`s. Each run carries its page-space `TextRun.BoundingBox`, per-glyph positions, resolved font presentation, and the optional-content `TextRun.Layers` it belongs to. Symmetric with `LineSegmentExtraction.ExtractLineSegments`.

**Parameters**

- `list` — The page display list to read.

**Returns:** The text runs in reading order. Never null.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/TextRunExtraction.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/TextRunExtraction.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
