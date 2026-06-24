# TextStamper

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Draws a single line of text at one of twelve anchor positions on selected pages, with template-token substitution (page numbers in several styles, file name/path, caller-supplied date/time, literal text). The stamp is an overlay: existing content is not moved. For running headers/footers that reserve space and reflow content, use `HeaderFooter`. PDF 32000-1:2008 §9.4 — text; §8.10.1 — form XObjects.

```csharp
public static class TextStamper
```

## Methods

### `Apply`

__static__

```csharp
static void Apply(Stream output, PdfDocument document, IEnumerable<int>? pageIndices, string template, StampAnchor anchor, double marginX, double marginY, double fontSize, ColorF color, StampNumbering numbering, string? filePath = null, DateTimeOffset? timestamp = null)
```

Stamps `template` onto the requested pages, resolving the `{number}` token from a running `StampNumbering` sequence (Bates / styled page numbering) in a single pass.

**Parameters**

- `output` — The stream to write the updated PDF to.
- `document` — The source document.
- `pageIndices` — Zero-based page indices to stamp. Null stamps every page. Pages skipped by `StampNumbering.FirstPage` are never stamped even when selected.
- `template` — The text template (may contain tokens, e.g. `{number}`).
- `anchor` — Where on the page to place the text.
- `marginX` — Horizontal inset from the page edge, in points.
- `marginY` — Vertical inset from the page edge, in points.
- `fontSize` — Font size in points.
- `color` — Text colour.
- `numbering` — The running numbering sequence used for `{number}`.
- `filePath` — Source file path for the `{filename}`/`{filepath}` tokens, or null.
- `timestamp` — Caller-supplied timestamp for date/time tokens, or null. <exception cref="ArgumentNullException"> Thrown when `output`, `document`, `template`, or `numbering` is null. </exception>

---

_Source: [`src/Chuvadi.Pdf.Operations/TextStamper.cs`](../../../src/Chuvadi.Pdf.Operations/TextStamper.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
