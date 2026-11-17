# RedactionOptions

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

Top-level configuration for a redaction operation.

```csharp
public sealed class RedactionOptions
```

## Constructors

### `RedactionOptions()`

Initialises `RedactionOptions` with default values.

## Properties

### `Rectangles`

```csharp
IList<RedactionRect> Rectangles
```

Gets the list of explicit rectangles to redact, by page.

### `Patterns`

```csharp
IList<PatternRule> Patterns
```

Gets the list of regex patterns to redact. Each pattern is matched on the text a viewer shows on each page it applies to, joined across text operators and including form XObjects, tiling-pattern cells, Type 3 glyphs and annotations (3.35.0). Each match becomes boxes inside the matched glyphs, removed as rectangles are; patterns with no page list are also applied outside the pages (bookmarks, metadata, scripts, addresses, alternate texts, attachments). See `Redactor.ApplyWithReport`.

### `OverlayColor`

```csharp
ColorF OverlayColor
```

Gets or initialises the colour painted over each redacted rectangle. Default: opaque black.

### `DrawOverlay`

```csharp
bool DrawOverlay
```

Gets or initialises whether an overlay box is painted over each redacted region. Default: `true`. Set to `false` (or supply a fully transparent `OverlayColor`) for boxless redaction: the matched glyphs are physically removed and no box is drawn, so the page reads as clean where the text was.

### `PatternPadding`

```csharp
double PatternPadding
```

Gets or initialises a padding in PDF points. Default: 1.0.

### `VectorMode`

```csharp
VectorRedactionMode VectorMode
```

Gets or initialises how painted vector paths (fills and strokes) that intersect a redaction region are removed. Default: `VectorRedactionMode.SubtractPrecise` — geometry whose ink provably misses every redaction rectangle survives, and only the intersecting geometry is removed (byte-level). Use `VectorRedactionMode.DropIntersecting` for the pre-3.18 whole-path drop.

### `MaxDegreeOfParallelism`

```csharp
int MaxDegreeOfParallelism
```

Gets or initialises the maximum number of threads used for the per-page content-rewrite stage. Default: `1` (sequential).

### `KeepAttachments`

```csharp
bool KeepAttachments
```

Gets or initialises whether, in a pattern redaction, attachments (embedded files) that cannot be checked against the patterns are kept (3.35.0). Default: `false`: an attachment is kept only when every file in it reads as text and neither the text nor its name matches a document-wide pattern; any other attachment is removed, because it can hold the very data being redacted, and is listed in `RedactionReport.AttachmentsRemoved`. Set to `true` to keep the attachments that could not be checked; they are listed in `RedactionReport.AttachmentsNotScanned`. An attachment that matches a pattern is removed either way. With no patterns at all (rectangles only) attachments are always kept and listed in `RedactionReport.AttachmentsNotScanned`, as there is nothing to check them against.

---

_Source: [`src/Chuvadi.Pdf.Redaction/RedactionOptions.cs`](../../../src/Chuvadi.Pdf.Redaction/RedactionOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
