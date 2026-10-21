# GlyphMetrics

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Typographic metrics for a single glyph, in font units (unscaled).

```csharp
public sealed class GlyphMetrics
```

## Remarks

All values are in the font's internal coordinate system (font design units). To convert to PDF points at a given point size: value_in_points = (value_in_font_units / unitsPerEm) × pointSize OpenType spec §hmtx — Horizontal Metrics table.

## Constructors

### `GlyphMetrics(int advanceWidth, int leftSideBearing, int unitsPerEm, RectangleF bounds)`

Initialises a `GlyphMetrics` instance.

## Properties

### `AdvanceWidth`

```csharp
int AdvanceWidth
```

Horizontal advance width in font design units. The cursor advances by this much after drawing the glyph.

### `LeftSideBearing`

```csharp
int LeftSideBearing
```

Left side bearing in font design units. Horizontal distance from the origin to the left edge of the bounding box.

### `UnitsPerEm`

```csharp
int UnitsPerEm
```

Font units per em square. Typically 1000 (PostScript) or 2048 (TrueType).

### `Bounds`

```csharp
RectangleF Bounds
```

Glyph bounding box in font design units.

## Methods

### `AdvanceWidthAt`

```csharp
double AdvanceWidthAt(double pointSize)
```

Scales the advance width to PDF points at the given point size.

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/GlyphMetrics.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/GlyphMetrics.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
