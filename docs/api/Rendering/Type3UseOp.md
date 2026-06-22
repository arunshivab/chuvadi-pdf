# Type3UseOp

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Paints one Type 3 glyph: a cached glyph-space sub-display-list positioned by a composition transform (FontMatrix · text-scale · text matrix · CTM). The renderer wraps the sub-list in a transform group so a repeated glyph reuses the same cached ops. Blend mode and soft mask ride on this op (not baked into the cached sub-list), so the cache is colour-keyed only.

```csharp
public sealed class Type3UseOp : RenderOp
```

## Constructors

### `Type3UseOp(PageDisplayList glyph, AffineMatrix composition)`

Initialises a `Type3UseOp`.

**Parameters**

- `glyph` — The glyph's sub-display-list, in glyph space.
- `composition` — Glyph space → page space transform.

## Properties

### `Kind`

```csharp
override RenderOpKind Kind => RenderOpKind.Type3Glyph
```

<inheritdoc />

### `Glyph`

```csharp
PageDisplayList Glyph
```

The glyph's sub-display-list, in glyph space.

### `Composition`

```csharp
AffineMatrix Composition
```

Glyph space → page space transform for this occurrence.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
