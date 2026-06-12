# ReportCell

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

One table cell: text or an image, an optional column/row span, and optional per-cell style overrides.

```csharp
public sealed class ReportCell
```

## Constructors

### `ReportCell()`

Creates an empty cell.

### `ReportCell(string text)`

Creates a text cell.

## Properties

### `Text`

```csharp
string Text
```

Gets or initialises the cell text. Default: empty.

### `ImageBytes`

```csharp
byte[]? ImageBytes
```

Gets or initialises an image drawn inside the cell instead of text (scaled to the cell's inner width, preserving aspect ratio). Accepts JPEG, PNG, TIFF, or BMP bytes.

### `ImageFrame`

```csharp
ImageFrame? ImageFrame
```

Gets or initialises a decoded image frame drawn inside the cell instead of text.

### `ColSpan`

```csharp
int ColSpan
```

Gets or initialises how many columns the cell spans. Default: 1.

### `RowSpan`

```csharp
int RowSpan
```

Gets or initialises how many rows the cell spans. Default: 1. Rows tied together by a span paginate as a unit and are kept on the same page.

### `Font`

```csharp
ReportFont? Font
```

Gets or initialises a font override; null inherits the table font.

### `FontSize`

```csharp
double? FontSize
```

Gets or initialises a font-size override; null inherits the table font size.

### `TextColor`

```csharp
Color? TextColor
```

Gets or initialises a text-colour override; null inherits the table text colour.

### `Background`

```csharp
Color? Background
```

Gets or initialises a background fill; null means no fill (row/alternating fills show through).

### `Alignment`

```csharp
TextAlignment? Alignment
```

Gets or initialises a horizontal-alignment override; null inherits the column alignment.

### `VerticalAlignment`

```csharp
VerticalAlignment VerticalAlignment
```

Gets or initialises the vertical alignment inside the cell. Default: top.

### `Overflow`

```csharp
CellOverflow? Overflow
```

Gets or initialises an overflow override; null inherits the column overflow.

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportTable.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportTable.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
