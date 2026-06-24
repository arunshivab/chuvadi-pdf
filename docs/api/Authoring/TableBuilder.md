# TableBuilder

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Fluent table builder. Configures columns, header/cell styling, and rows; `Render` commits the table to the page (and may overflow to a continuation rendered on a subsequent page).

```csharp
public sealed class TableBuilder
```

## Methods

### `AddColumn`

```csharp
TableBuilder AddColumn(string header, double widthFraction)
```

Adds a column with the given header label and width fraction (0..1 of total).

### `Font`

```csharp
TableBuilder Font(string font, double size, Color? textColor = null)
```

Configures the table font and size.

### `HeaderStyle`

```csharp
TableBuilder HeaderStyle(bool bold = true, Color? background = null)
```

Configures header row style.

### `CellPadding`

```csharp
TableBuilder CellPadding(double padding)
```

Sets cell padding (points). Default 4.

### `RowHeight`

```csharp
TableBuilder RowHeight(double height)
```

Sets explicit row height (0 = auto from font size + padding).

### `Border`

```csharp
TableBuilder Border(BorderStyle style, Color color, double width = 0.5)
```

Sets table border style.

### `AddRow`

```csharp
TableBuilder AddRow(params string[] cells)
```

Adds a row. Cell count must match the column count.

### `Render`

```csharp
TableRenderResult Render()
```

Renders the table onto the page. Returns the Y position immediately below the last drawn row.

**Remarks:** If the table overflows the page, the overflow is returned via the result; the caller is responsible for adding a new page and re-rendering the remaining rows. v1 does not auto-add pages, but the API surface is designed so v1.3.1 can without breaking existing callers.

---

_Source: [`src/Chuvadi.Pdf.Authoring/TableBuilder.cs`](../../../src/Chuvadi.Pdf.Authoring/TableBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
