# TextRun

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

A contiguous run of text on a page, with glyph-level positions for selection-overlay use cases.

```csharp
public sealed class TextRun
```

## Properties

### `Unicode`

```csharp
string Unicode
```

The logical character sequence (concatenation of glyph Unicodes).

### `BoundingBox`

```csharp
Rect BoundingBox
```

Bounding box of the run in PDF user-space coords.

### `Glyphs`

```csharp
IReadOnlyList<GlyphPosition> Glyphs
```

Per-glyph positions.

### `Direction`

```csharp
TextDirection Direction
```

Reading direction.

### `ReadingOrderIndex`

```csharp
int ReadingOrderIndex
```

Monotonic 0-based reading-order index within the page.

### `FontFamily`

```csharp
string FontFamily
```

Resolved font family (subset tag and style suffix stripped).

### `FontWeight`

```csharp
int FontWeight
```

CSS-style numeric weight (400 normal, 700 bold).

### `Slant`

```csharp
FontSlant Slant
```

Slant classification (normal, italic, or oblique).

### `FontSize`

```csharp
double FontSize
```

Effective font size of the run in user-space points.

### `Layers`

```csharp
IReadOnlyList<string> Layers
```

Optional-content (OCG) layers this run belongs to, outermost first, as resolved from the marked-content stack. Empty when the run is not inside any optional-content group.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/TextRun.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/TextRun.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
