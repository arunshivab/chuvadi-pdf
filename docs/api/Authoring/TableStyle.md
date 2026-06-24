# TableStyle

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Table-level styling: fonts, borders, padding, header and fills.

```csharp
public sealed class TableStyle
```

## Properties

### `Default`

__static__

```csharp
static TableStyle Default
```

Default style: 10-point Helvetica, full grid in light gray, bold gray header.

### `Font`

```csharp
ReportFont Font
```

Gets or initialises the body font. Default: regular Helvetica.

### `FontSize`

```csharp
double FontSize
```

Gets or initialises the body font size in points. Default: 10.

### `TextColor`

```csharp
Color TextColor
```

Gets or initialises the body text colour. Default: black.

### `ShowHeader`

```csharp
bool ShowHeader
```

Gets or initialises whether the header row is drawn at all. Default: true.

### `RepeatHeaderOnEveryPage`

```csharp
bool RepeatHeaderOnEveryPage
```

Gets or initialises whether the header row repeats at the top of every continuation page. Default: true.

### `HeaderFont`

```csharp
ReportFont HeaderFont
```

Gets or initialises the header font. Default: bold Helvetica.

### `HeaderFontSize`

```csharp
double HeaderFontSize
```

Gets or initialises the header font size in points; 0 (the default) inherits `FontSize`.

### `HeaderTextColor`

```csharp
Color HeaderTextColor
```

Gets or initialises the header text colour. Default: black.

### `HeaderBackground`

```csharp
Color? HeaderBackground
```

Gets or initialises the header background fill. Default: light gray.

### `BorderMode`

```csharp
TableBorderMode BorderMode
```

Gets or initialises which grid lines are drawn. Default: full grid.

### `BorderColor`

```csharp
Color BorderColor
```

Gets or initialises the grid line colour. Default: mid gray.

### `BorderWidth`

```csharp
double BorderWidth
```

Gets or initialises the grid line width in points. Default: 0.5.

### `CellPadding`

```csharp
double CellPadding
```

Gets or initialises the padding, in points, inside every cell. Default: 4.

### `AlternatingRowBackground`

```csharp
Color? AlternatingRowBackground
```

Gets or initialises an alternating fill applied to every second body row (the 2nd, 4th, …). Null (the default) disables row banding.

### `LineSpacing`

```csharp
double LineSpacing
```

Gets or initialises the line spacing of wrapped cell text as a multiple of the font size. Default: 1.2.

### `SpaceAfter`

```csharp
double SpaceAfter
```

Gets or initialises the vertical space, in points, after the table. Default: 8.

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportTable.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportTable.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
