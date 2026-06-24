# ReportRow

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

One table row: its cells plus optional height and background overrides.

```csharp
public sealed class ReportRow
```

## Constructors

### `ReportRow()`

Creates an empty row.

### `ReportRow(params string[] cells)`

Creates a row of plain text cells.

## Properties

### `Cells`

```csharp
List<ReportCell> Cells
```

Gets the row's cells, left to right. Spanned-over grid positions are skipped, HTML-style.

### `Height`

```csharp
double Height
```

Gets or initialises a fixed row height in points; 0 (the default) sizes the row to its content.

### `Background`

```csharp
Color? Background
```

Gets or initialises a background fill for the whole row; null lets table-level fills apply.

---

_Source: [`src/Chuvadi.Pdf.Authoring/ReportTable.cs`](../../../src/Chuvadi.Pdf.Authoring/ReportTable.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
