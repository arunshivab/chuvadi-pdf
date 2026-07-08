# DrawGlyphOp

**Class** in `Chuvadi.Pdf.Rendering.Raster` (Rendering)

Paints a single glyph outline.

```csharp
public sealed class DrawGlyphOp : RenderOp
```

## Remarks

Emitted by the builder once per visible glyph in a text-showing operator (Tj, TJ, ', "). Each call to a text-showing operator produces a sequence of `DrawGlyphOp`s — one per glyph — with the path already transformed into PDF user space by the combination of the text matrix, font size, and CTM in effect at emission time.  

 The outline is filled (not stroked) by default; PDF supports stroked and outline-only text rendering modes which a later op type may model. In v2.0.0 R1 the rendering-mode-3 (invisible text) case is handled by the builder simply not emitting glyph ops.

## Properties

### `Path`

```csharp
Path Path
```

Gets the glyph outline path in user space.

### `Color`

```csharp
ColorF Color
```

Gets the fill colour.

---

_Source: [`src/Chuvadi.Pdf.Rendering.Raster/DrawGlyphOp.cs`](../../../src/Chuvadi.Pdf.Rendering.Raster/DrawGlyphOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
