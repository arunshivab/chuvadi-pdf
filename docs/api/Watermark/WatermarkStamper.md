# WatermarkStamper

**Class** in `Chuvadi.Pdf.Watermark` (Watermark)

Stamps text or image watermarks onto PDF pages by appending new content streams, preserving the original page content.

```csharp
public static class WatermarkStamper
```

## Remarks

Watermarks are applied as additional content streams appended to each targeted page. The original content is untouched. Opacity is implemented via PDF ExtGState (/ca fill opacity, PDF 32000-1:2008 §11.6.4.4). Since 3.36.0 (D-412) text watermarks are shaped and subset-embedded through the shared mark-text composer: the text is segmented by script, shaped with the LiPi Sans face for each script (or a caller-supplied font), with a monochrome Noto Emoji fallback for code points the primary face lacks, and every face is embedded as a composite Type0/Identity-H font. The watermark is therefore always embedded and is no longer restricted to the Helvetica WinAnsi repertoire.

## Methods

### `ApplyText`

__static__

```csharp
static void ApplyText(Stream output, PdfDocument document, TextWatermarkOptions options)
```

Applies a text watermark to all (or specified) pages of a document and writes the result to `output`.

**Parameters**

- `output` — The stream to write the watermarked PDF to.
- `document` — The source document.
- `options` — Watermark configuration.

**Remarks:** (Since 3.36.0, D-412) The text is shaped and subset-embedded through the shared mark-text composer. When `TextWatermarkOptions.FontData` is supplied it is the primary face for every code point it covers; otherwise the LiPi Sans face for each script is used, with a Noto Emoji fallback. The watermark is always embedded.

### `ApplyImage`

__static__

```csharp
static void ApplyImage(Stream output, PdfDocument document, ImageFrame frame, ImageWatermarkOptions options)
```

Applies an image watermark to all (or specified) pages of a document and writes the result to `output`.

**Parameters**

- `output` — The stream to write the watermarked PDF to.
- `document` — The source document.
- `frame` — The image to use as the watermark.
- `options` — Watermark configuration.

---

_Source: [`src/Chuvadi.Pdf.Watermark/WatermarkStamper.cs`](../../../src/Chuvadi.Pdf.Watermark/WatermarkStamper.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
