# NUpOptions

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Options for `Imposition.NUp(System.IO.Stream, Chuvadi.Pdf.Documents.PdfDocument, NUpOptions)`: how many source pages to place per sheet, the sheet size, and the spacing around and between cells.

```csharp
public sealed class NUpOptions
```

## Properties

### `Rows`

```csharp
int Rows
```

The number of cell rows per sheet. Default 1.

### `Columns`

```csharp
int Columns
```

The number of cell columns per sheet. Default 2.

### `SheetSize`

```csharp
PageSize SheetSize
```

The output sheet size. Default `PageSize.A4`.

### `Margin`

```csharp
double Margin
```

The margin, in points, around the grid of cells. Default 0.

### `Gutter`

```csharp
double Gutter
```

The gutter, in points, between adjacent cells. Default 0.

### `Order`

```csharp
NUpOrder Order
```

The order in which source pages fill cells. Default `NUpOrder.RowMajor`.

---

_Source: [`src/Chuvadi.Pdf.Operations/ImpositionOptions.cs`](../../../src/Chuvadi.Pdf.Operations/ImpositionOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
