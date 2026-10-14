# Imposition

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Composes the pages of a source document onto larger sheets: N-up grids and 2-up saddle-stitch booklets. Each source page is scaled to fit its cell (aspect preserved), centred, and clipped to the cell so nothing overflows. Placement honours each source page's crop box (only the visible region is fitted, wherever its origin lies) and its `/Rotate` (a rotated page is placed upright, as a viewer displays it).

```csharp
public static class Imposition
```

## Remarks

`PlanNUp` and `PlanBooklet` return the exact sheet geometry that `NUp(Stream, PdfDocument, NUpOptions)` and `Booklet(Stream, PdfDocument, BookletOptions)` write, so a preview drawn from a plan matches the output.

## Methods

### `NUp`

__static__

```csharp
static void NUp(Stream output, PdfDocument source, NUpOptions options) => NUp(output, source, options, null)
```

Lays out the source pages as an N-up grid and writes the result.

**Parameters**

- `output` — The stream to write to.
- `source` — The source document.
- `options` — The grid layout options.

### `NUp`

__static__

```csharp
static void NUp(Stream output, PdfDocument source, NUpOptions options, EncryptionOptions? encryption)
```

Lays out the source pages as an N-up grid and writes the result, optionally encrypted.

**Parameters**

- `output` — The stream to write to.
- `source` — The source document.
- `options` — The grid layout options.
- `encryption` — The encryption options, or null for no encryption.

### `Booklet`

__static__

```csharp
static void Booklet(Stream output, PdfDocument source, BookletOptions options) => Booklet(output, source, options, null)
```

Lays out the source pages as a 2-up saddle-stitch booklet and writes the result.

**Parameters**

- `output` — The stream to write to.
- `source` — The source document.
- `options` — The booklet layout options.

### `Booklet`

__static__

```csharp
static void Booklet(Stream output, PdfDocument source, BookletOptions options, EncryptionOptions? encryption)
```

Lays out the source pages as a 2-up saddle-stitch booklet and writes the result, optionally encrypted.

**Parameters**

- `output` — The stream to write to.
- `source` — The source document.
- `options` — The booklet layout options.
- `encryption` — The encryption options, or null for no encryption.

### `PlanNUp`

__static__

```csharp
static IReadOnlyList<ImpositionSheet> PlanNUp(PdfDocument source, NUpOptions options)
```

Computes the N-up sheet layout without writing anything: one `ImpositionSheet` per output sheet, each listing where every source page lands.

**Parameters**

- `source` — The source document.
- `options` — The grid layout options.

**Returns:** The sheets, in output order. <exception cref="ArgumentNullException">Thrown when an argument is null.</exception> <exception cref="ArgumentException"> Thrown when the source has no pages, rows or columns are below 1, or the margin and gutter leave no room for a cell. </exception>

### `PlanBooklet`

__static__

```csharp
static IReadOnlyList<ImpositionSheet> PlanBooklet(PdfDocument source, BookletOptions options)
```

Computes the saddle-stitch booklet layout without writing anything: two sheet sides per physical sheet, in print order, padded with blank slots to a multiple of four pages.

**Parameters**

- `source` — The source document.
- `options` — The booklet layout options.

**Returns:** The sheet sides, in output order. <exception cref="ArgumentNullException">Thrown when an argument is null.</exception> <exception cref="ArgumentException"> Thrown when the source has no pages or the margin leaves no room in a slot. </exception>

### `FitTransform`

__static__

```csharp
static Transform FitTransform(PdfPage page, RectangleF target)
```

Returns the transform that fits a source page's visible area — its crop box, displayed upright per `/Rotate` — centred inside `target` with its aspect ratio preserved. Concatenate it (`cm`) before drawing the page's content, as `PageComposer.PlacePage(PdfDocument, int, Transform)` does.

**Parameters**

- `page` — The source page.
- `target` — The rectangle to fit into, in target user space.

**Returns:** The placement transform. <exception cref="ArgumentNullException">Thrown when `page` is null.</exception> <exception cref="ArgumentException"> Thrown when the page's crop box or the target rectangle has no area. </exception>

---

_Source: [`src/Chuvadi.Pdf.Operations/Imposition.cs`](../../../src/Chuvadi.Pdf.Operations/Imposition.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
