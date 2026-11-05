# FontEmbeddingReport

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Checks whether every font a document can use is embedded (3.31). The check reads resources, so a font that a resource dictionary names but no content uses still counts: the answer errs towards `FontEmbeddingVerdict.NotAllEmbedded`. Standard 14 fonts that are not embedded count as not embedded.

```csharp
public sealed class FontEmbeddingReport
```

## Properties

### `Verdict`

```csharp
FontEmbeddingVerdict Verdict
```

The verdict.

### `NotEmbedded`

```csharp
IReadOnlyList<FontUse> NotEmbedded
```

Each font that is not embedded, with the first page and place it was found (resources shared by several pages are read once), in page order.

### `NeedsGeneratedAppearances`

```csharp
bool NeedsGeneratedAppearances
```

True when some form field or comment is drawn from its own entries rather than its stored appearance (`/NeedAppearances` is true for text or choice fields, or an annotation has no `/AP`). Such a PDF does not print as it is, whatever the verdict.

## Methods

### `Check`

__static__

```csharp
static FontEmbeddingReport Check(PdfDocument document)
```

Checks `document`.

**Parameters**

- `document` — The document.

**Returns:** The report. <exception cref="ArgumentNullException">`document` is null.</exception>

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/FontEmbeddingReport.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/FontEmbeddingReport.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
