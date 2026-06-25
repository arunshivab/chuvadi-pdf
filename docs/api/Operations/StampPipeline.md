# StampPipeline

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Accumulates several overlay operations — text stamps, page numbers, text watermarks, headers and footers — plus an optional outline, and applies them all in a single write. This avoids the repeated full-document serialization that results from chaining the standalone stamp operations, each of which writes the document on its own. Add steps in any order; call `Write(System.IO.Stream, Chuvadi.Pdf.IO.EncryptionOptions?)` once to emit the result.

```csharp
public sealed class StampPipeline
```

## Constructors

### `StampPipeline(PdfDocument document)`

Creates a pipeline that overlays content onto `document`.

**Parameters**

- `document` — The document to stamp.

## Methods

### `AddTextStamp`

```csharp
StampPipeline AddTextStamp(string template, StampAnchor anchor, double fontSize, ColorF color, double marginX = 24, double marginY = 24, IEnumerable<int>? pages = null, string? filePath = null, DateTimeOffset? timestamp = null)
```

Adds an anchored text stamp whose template may contain tokens such as `{page}`.

**Parameters**

- `template` — The text template; supports stamp tokens.
- `anchor` — Where on the page the text sits.
- `fontSize` — Font size in points.
- `color` — Text color.
- `marginX` — Horizontal inset from the page edge, in points.
- `marginY` — Vertical inset from the page edge, in points.
- `pages` — The zero-based page indices to stamp, or null for all pages.
- `filePath` — Source path for filename tokens, or null.
- `timestamp` — Timestamp for date/time tokens, or null.

**Returns:** This pipeline, for chaining.

### `AddPageNumbers`

```csharp
StampPipeline AddPageNumbers(StampNumbering numbering, StampAnchor anchor, double fontSize, ColorF color, double marginX = 24, double marginY = 24, string template = "
```

Adds page numbers formatted by `numbering`.

**Parameters**

- `numbering` — The numbering scheme (start value, padding, format, first-page mode).
- `anchor` — Where on the page the number sits.
- `fontSize` — Font size in points.
- `color` — Text color.
- `marginX` — Horizontal inset from the page edge, in points.
- `marginY` — Vertical inset from the page edge, in points.
- `template` — The template wrapping the number; `{number}` is the formatted value.

**Returns:** This pipeline, for chaining.

### `AddTextWatermark`

```csharp
StampPipeline AddTextWatermark(string text, double fontSize, ColorF color, double opacity = 0.12, double rotationDegrees = 45, IEnumerable<int>? pages = null)
```

Adds a rotated, semi-transparent text watermark centered on each page.

**Parameters**

- `text` — The watermark text.
- `fontSize` — Font size in points.
- `color` — Text color.
- `opacity` — Constant alpha in the range 0 (transparent) to 1 (opaque).
- `rotationDegrees` — Counter-clockwise rotation in degrees.
- `pages` — The zero-based page indices to mark, or null for all pages.

**Returns:** This pipeline, for chaining.

### `AddHeaderFooter`

```csharp
StampPipeline AddHeaderFooter(HeaderFooterOptions options)
```

Adds a header and/or footer as anchored text segments (overlay only, no page reflow).

**Parameters**

- `options` — The header/footer band text and layout.

**Returns:** This pipeline, for chaining.

### `AddOutline`

```csharp
StampPipeline AddOutline(IReadOnlyList<OutlineEntry> entries)
```

Adds a document outline (bookmarks) that is folded into the single write.

**Parameters**

- `entries` — The top-level outline entries; each may carry children.

**Returns:** This pipeline, for chaining.

### `Write`

```csharp
void Write(Stream output, EncryptionOptions? encryption = null)
```

Writes the stamped document to `output` in a single pass.

**Parameters**

- `output` — The stream to write to.
- `encryption` — The encryption options, or null for no encryption.

---

_Source: [`src/Chuvadi.Pdf.Operations/StampPipeline.cs`](../../../src/Chuvadi.Pdf.Operations/StampPipeline.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
