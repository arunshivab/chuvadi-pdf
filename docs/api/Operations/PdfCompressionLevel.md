# PdfCompressionLevel

**Enum** in `Chuvadi.Pdf.Operations` (Operations)

A named compression level, lightest first (3.32.0). Used by `PdfCompressor.Compress(Chuvadi.Pdf.Documents.PdfDocument, System.IO.Stream, PdfCompressionLevel, ImageLevelOptions?, CompressionOptions?)` and by `CompressToTargetOptions.MaxLevel`.

```csharp
public enum PdfCompressionLevel
```

## Remarks

The first three keep the document as it is (text stays selectable). The last three are image levels: every page becomes one image at 150 dpi by default (see `ImageLevelOptions`), so text can no longer be selected or searched, links and form fields are gone, and signatures and encryption are dropped like in any rewrite.

## Values

| Name | Description |
|---|---|
| `Lossless` | Streams re-compressed, identical objects merged, unused objects dropped. Pages look exactly the same; images are not touched. |
| `Medium` | Lossless, plus images re-encoded as JPEG at quality 75 and reduced to 150 dpi. Colour is kept. |
| `High` | Lossless, plus images re-encoded as JPEG at quality 50 and reduced to 100 dpi. Colour is kept. |
| `ReducedColor` | Image level: every page becomes one image of up to 16 colours (white, black and the page's 14 most used other colours). Dark greys (darker than about 63% grey) become black, so thin text stays dark. The closest image level to the original look; the largest of the three. |
| `BlackAndWhiteWithColor` | Image level: clearly coloured marks keep their colour (up to 14 per page); everything else becomes black and white by the `BlackAndWhite` rules, so grey text stays sharp. Smaller than `ReducedColor`. |
| `BlackAndWhite` | Image level: every page becomes one black-and-white (1-bit) image, the smallest output for line drawings. Colour is lost. Every pixel that is not near-white becomes black, so thin grey text stays readable; see `BlackAndWhiteFills` for large fills. |

---

_Source: [`src/Chuvadi.Pdf.Operations/PdfCompressionLevel.cs`](../../../src/Chuvadi.Pdf.Operations/PdfCompressionLevel.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
