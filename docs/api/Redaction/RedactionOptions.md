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

Gets the list of regex patterns to redact. Each matching span across extracted text on a targeted page is resolved to a device-space rectangle and added to the redaction set.

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

Gets or initialises the padding (PDF points) added around each pattern-derived rectangle to compensate for font-metric approximation. Default: 1.0.

### `MaxDegreeOfParallelism`

```csharp
int MaxDegreeOfParallelism
```

Gets or initialises the maximum number of threads used for the per-page content-rewrite stage. Default: `1` (sequential).

---

_Source: [`src/Chuvadi.Pdf.Redaction/RedactionOptions.cs`](../../../src/Chuvadi.Pdf.Redaction/RedactionOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
