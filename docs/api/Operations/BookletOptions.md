# BookletOptions

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Options for `Imposition.Booklet(System.IO.Stream, Chuvadi.Pdf.Documents.PdfDocument, BookletOptions)`: the size of each source-page slot (the output sheet is twice this width) and the margin around each slot.

```csharp
public sealed class BookletOptions
```

## Properties

### `PageSize`

```csharp
PageSize PageSize
```

The size of each page slot. The sheet is twice this width. Default `PageSize.A4`.

### `Margin`

```csharp
double Margin
```

The margin, in points, around each page slot. Default 0.

---

_Source: [`src/Chuvadi.Pdf.Operations/ImpositionOptions.cs`](../../../src/Chuvadi.Pdf.Operations/ImpositionOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
