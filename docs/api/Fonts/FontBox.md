# FontBox

**Record** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

A glyph or font bounding box in font design units.

```csharp
public readonly record struct FontBox(int XMin, int YMin, int XMax, int YMax)
```

## Properties

### `Width`

```csharp
int Width => XMax - XMin
```

Gets the width (`XMax` − `XMin`).

### `Height`

```csharp
int Height => YMax - YMin
```

Gets the height (`YMax` − `YMin`).

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/FontFace.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/FontFace.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
