# ParagraphStyle

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Paragraph styling: font, size, colour, alignment, spacing, and indents.

```csharp
public sealed class ParagraphStyle
```

## Properties

### `Default`

__static__

```csharp
static ParagraphStyle Default
```

Default body style: 11-point Helvetica, left-aligned, 1.25 line spacing.

### `Font`

```csharp
ReportFont Font
```

Gets or initialises the font. Default: regular Helvetica.

### `FontSize`

```csharp
double FontSize
```

Gets or initialises the font size in points. Default: 11.

### `Color`

```csharp
Color Color
```

Gets or initialises the text colour. Default: black.

### `Alignment`

```csharp
TextAlignment Alignment
```

Gets or initialises the horizontal alignment. Default: left. `TextAlignment.Justify` stretches every full line to the column width.

### `LineSpacing`

```csharp
double LineSpacing
```

Gets or initialises the line spacing as a multiple of the font size. Default: 1.25.

### `SpaceBefore`

```csharp
double SpaceBefore
```

Gets or initialises the vertical space, in points, inserted before the paragraph. Default: 0.

### `SpaceAfter`

```csharp
double SpaceAfter
```

Gets or initialises the vertical space, in points, inserted after the paragraph. Default: 6.

### `FirstLineIndent`

```csharp
double FirstLineIndent
```

Gets or initialises the extra indent, in points, applied to the first line only. Default: 0.

### `LeftIndent`

```csharp
double LeftIndent
```

Gets or initialises the left indent, in points, applied to every line. Default: 0.

### `RightIndent`

```csharp
double RightIndent
```

Gets or initialises the right indent, in points, applied to every line. Default: 0.

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportStyles.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportStyles.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
