# TextOp

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Renders a positioned glyph run.

```csharp
public sealed class TextOp : RenderOp
```

## Properties

### `Kind`

```csharp
override RenderOpKind Kind => RenderOpKind.Text
```

<inheritdoc />

### `FontKey`

```csharp
required string FontKey
```

Font resource name as declared in /Resources/Font.

### `BaseFont`

```csharp
required string BaseFont
```

Base font name (e.g. "Helvetica", "Times-Roman", or a subset like "ABCDEF+MyFont").

### `Font`

```csharp
DisplayListFont? Font
```

The font, resolved through the same resources as the text (3.31): the program, its kind and the substitute for a font that is not embedded. One instance per font dictionary on the page. Null when the font resource cannot be found.

### `FontSize`

```csharp
required double FontSize
```

Font size in user space.

### `Glyphs`

```csharp
required IReadOnlyList<DisplayListGlyph> Glyphs
```

Per-glyph positions and Unicode mappings.

### `Transform`

```csharp
required AffineMatrix Transform
```

Combined CTM × text matrix for the glyph origins.

### `RenderingMode`

```csharp
TextRenderingMode RenderingMode
```

Text rendering mode (PDF §9.3.6).

### `FillColor`

```csharp
PdfColor FillColor
```

Fill color (when mode includes fill).

### `StrokeColor`

```csharp
PdfColor StrokeColor
```

Stroke color (when mode includes stroke).

### `Style`

```csharp
FontStyle Style
```

Resolved presentation style (family, weight, slant).

### `FillOpacity`

```csharp
double FillOpacity
```

Constant fill opacity (ExtGState /ca), 0..1. Default 1 (opaque).

### `StrokeOpacity`

```csharp
double StrokeOpacity
```

Constant stroke opacity (ExtGState /CA), 0..1. Default 1 (opaque).

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
