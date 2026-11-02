# LayoutBand

**Class** in `Chuvadi.Pdf.Text` (Text)

A horizontal band of a page: a region read as a whole before the next band, holding one column (read as rows) or several (read one after another).

```csharp
public sealed class LayoutBand
```

## Constructors

### `LayoutBand(PdfRectangle bounds, IReadOnlyList<LayoutColumn> columns)`

Initialises a band.

**Parameters**

- `bounds` — The band's rectangle in user space.
- `columns` — The band's columns in reading order (one or more). <exception cref="ArgumentNullException">`columns` is null.</exception>

## Properties

### `Bounds`

```csharp
PdfRectangle Bounds
```

Gets the band's rectangle in user space.

### `Columns`

```csharp
IReadOnlyList<LayoutColumn> Columns
```

Gets the band's columns in reading order.

---

_Source: [`src/Chuvadi.Pdf.Text/PageLayout.cs`](../../../src/Chuvadi.Pdf.Text/PageLayout.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
