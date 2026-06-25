# Imposition

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Composes the pages of a source document onto larger sheets: N-up grids and 2-up saddle-stitch booklets. Each source page is scaled to fit its cell (aspect preserved), centered, and clipped to the cell so nothing overflows.

```csharp
public static class Imposition
```

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

---

_Source: [`src/Chuvadi.Pdf.Operations/Imposition.cs`](../../../src/Chuvadi.Pdf.Operations/Imposition.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
