# ReportColumn

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

A table column: optional header text, width, and default cell styling.

```csharp
public sealed class ReportColumn
```

## Properties

### `Header`

```csharp
string Header
```

Gets or initialises the header label drawn in the header row. Default: empty.

### `WidthMode`

```csharp
ColumnWidthMode WidthMode
```

Gets or initialises how `Width` is interpreted. Default: Auto.

### `Width`

```csharp
double Width
```

Gets or initialises the width value: a fraction of the table width under `ColumnWidthMode.Fraction`, points under `ColumnWidthMode.Points`, ignored under `ColumnWidthMode.Auto`.

### `Alignment`

```csharp
TextAlignment Alignment
```

Gets or initialises the default horizontal alignment of the column's cells. Default: left.

### `Overflow`

```csharp
CellOverflow Overflow
```

Gets or initialises the default overflow behaviour of the column's cells. Default: wrap.

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportTable.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportTable.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
