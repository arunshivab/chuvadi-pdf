# RedactionVerifier

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

Checks what a document still paints inside given regions, using the same display list the renderers draw — page content, form XObjects, Type 3 glyphs and annotation appearances included. Run it on a redacted document with the redaction rectangles to confirm nothing visible remains (the redaction overlay rectangle itself is ignored); or use `ExtractWithin` on any document to list what a region holds.

```csharp
public static class RedactionVerifier
```

## Remarks

Text is matched per glyph on an approximate ink box (the middle 80% of the glyph's advance, from the baseline to 0.7 em), so a neighbouring glyph that merely abuts a rectangle is not reported. Invisible text (render mode 3 or clip-only) and fully transparent paint are not reported. A shading is reported when its clip covers part of a region (3.35.0; before, shadings were not reported). `RedactionVerifier.VerifyPatterns` checks a pattern redaction (3.35.0).

## Methods

### `Verify`

__static__

```csharp
static RedactionVerification Verify(PdfDocument document, IReadOnlyList<RedactionRect> rectangles)
```

Verifies that the regions of `rectangles` are empty in `document` (typically the redaction output).

**Parameters**

- `document` — The document to inspect.
- `rectangles` — The regions that must be empty.

**Returns:** The verification result. <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>

### `VerifyPatterns`

__static__

```csharp
static RedactionVerification VerifyPatterns(PdfDocument document, IReadOnlyList<PatternRule> patterns)
```

Verifies that nothing in `document` (typically the output of a pattern redaction) still matches `patterns` (3.35.0): on each page the patterns apply to, the text a viewer shows, matched as `Redactor` matches it (across text operators, in form XObjects, tiling-pattern cells, Type 3 glyphs and annotation appearances, with or without the spaces the layout implies), and the annotations' text entries and actions; and, for the patterns with no page list, everything outside the page content: every string in the document's objects, the XMP metadata, scripts, replacement text (`/ActualText`, `/Alt`, `/E`) in content streams, and attachments. An attachment that cannot be read as text is reported as a survivor with no text, since it cannot be checked.

**Parameters**

- `document` — The document to inspect.
- `patterns` — The patterns that must not match.

**Returns:** The verification result: one survivor per match. <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>

### `ExtractWithin`

__static__

```csharp
static IReadOnlyList<RedactionSurvivor> ExtractWithin(PdfDocument document, int pageIndex, RectangleF region)
```

Lists everything page `pageIndex` paints inside `region`: glyphs, images, paths and annotations.

**Parameters**

- `document` — The document to inspect.
- `pageIndex` — Zero-based page index.
- `region` — The region, in page space (points, origin bottom-left).

**Returns:** The content found, in drawing order. <exception cref="ArgumentNullException">Thrown when `document` is null.</exception> <exception cref="ArgumentOutOfRangeException">Thrown when the page index is out of range.</exception>

---

_Source: [`src/Chuvadi.Pdf.Redaction/RedactionVerifier.cs`](../../../src/Chuvadi.Pdf.Redaction/RedactionVerifier.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
