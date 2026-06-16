# HeaderFooterOptions

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Options controlling header/footer content, geometry, and the content-fit strategy. Header and footer are independent; either may be null.

```csharp
public sealed class HeaderFooterOptions
```

## Properties

### `Header`

```csharp
BandText? Header
```

Gets or initialises the header band, or null for no header.

### `Footer`

```csharp
BandText? Footer
```

Gets or initialises the footer band, or null for no footer.

### `HeaderHeight`

```csharp
double HeaderHeight
```

Gets or initialises the reserved header band height in points (used when `Fit` scales content). Default: 36.

### `FooterHeight`

```csharp
double FooterHeight
```

Gets or initialises the reserved footer band height in points (used when `Fit` scales content). Default: 36.

### `HeaderBaselineOffset`

```csharp
double HeaderBaselineOffset
```

Gets or initialises the header baseline offset measured downward from the top of the reserved band, in points. Default: -24 (24 pt below the top).

### `FooterBaselineOffset`

```csharp
double FooterBaselineOffset
```

Gets or initialises the footer baseline offset measured upward from the bottom of the page, in points. Default: 18.

### `MarginX`

```csharp
double MarginX
```

Gets or initialises the horizontal margin for left/right segments, in points. Default: 36.

### `FontSize`

```csharp
double FontSize
```

Gets or initialises the font size in points. Default: 9.

### `Color`

```csharp
ColorF Color
```

Gets or initialises the text colour. Default: black.

### `Background`

```csharp
ColorF? Background
```

Gets or initialises a background fill drawn behind page content when a reflow strategy is used, or null for none.

### `Fit`

```csharp
PageContentFit Fit
```

Gets or initialises how header/footer bands interact with existing content. Default: `PageContentFit.ReserveAndScale`.

### `PageIndices`

```csharp
IReadOnlyList<int>? PageIndices
```

Gets or initialises which pages receive the header/footer. Null means all pages; otherwise a zero-based page index set.

### `FilePath`

```csharp
string? FilePath
```

Gets or initialises the source file path for the `{filename}` and `{filepath}` tokens, or null.

### `Timestamp`

```csharp
System.DateTimeOffset? Timestamp
```

Gets or initialises the caller-supplied timestamp for the date/time tokens, or null.

---

_Source: [`src/Chuvadi.Pdf.Operations/HeaderFooterOptions.cs`](../../../src/Chuvadi.Pdf.Operations/HeaderFooterOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
