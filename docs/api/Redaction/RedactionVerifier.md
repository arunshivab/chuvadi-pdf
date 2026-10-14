# RedactionVerifier

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

Checks what a document still paints inside given regions, using the same display list the renderers draw — page content, form XObjects, Type 3 glyphs and annotation appearances included. Run it on a redacted document with the redaction rectangles to confirm nothing visible remains (the redaction overlay rectangle itself is ignored); or use `ExtractWithin` on any document to list what a region holds.

```csharp
public static class RedactionVerifier
```

## Remarks

Text is matched per glyph on an approximate ink box (the middle 80% of the glyph's advance, from the baseline to 0.7 em), so a neighbouring glyph that merely abuts a rectangle is not reported. Invisible text (render mode 3 or clip-only), fully transparent paint, shadings and pattern cells are not reported.

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
