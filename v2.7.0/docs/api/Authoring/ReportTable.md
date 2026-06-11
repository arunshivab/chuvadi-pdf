# ReportTable

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

A report table: columns, rows, and a style. Add to a report with `ReportBuilder.AddTable(ReportTable)`; long tables paginate automatically with the header repeated per page (when enabled).

```csharp
public sealed class ReportTable
```

## Properties

### `Columns`

```csharp
List<ReportColumn> Columns
```

Gets the column definitions, left to right.

### `Rows`

```csharp
List<ReportRow> Rows
```

Gets the body rows, top to bottom.

### `Style`

```csharp
TableStyle Style
```

Gets or initialises the table style. Default: `TableStyle.Default`.

## Methods

### `AddColumn`

```csharp
ReportTable AddColumn(ReportColumn column)
```

Adds a column and returns the table for chaining.

### `AddColumn`

```csharp
ReportTable AddColumn(string header)
```

Adds an auto-width column with the given header and returns the table for chaining.

### `AddRow`

```csharp
ReportTable AddRow(params string[] cells)
```

Adds a row of plain text cells and returns the table for chaining.

### `AddRow`

```csharp
ReportTable AddRow(ReportRow row)
```

Adds a row and returns the table for chaining.

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportTable.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportTable.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
