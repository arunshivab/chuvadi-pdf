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

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
