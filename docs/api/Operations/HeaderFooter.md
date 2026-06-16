# HeaderFooter

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Adds running headers and/or footers to a document, with three strategies for how the bands interact with existing content (see `PageContentFit`): overlay in the margins, always reserve-and-scale, or scale only when content intrudes. Header/footer text supports the same tokens as `TextStamper` (page numbers in several styles, file name/path, caller-supplied date/time, custom text), each with independent left/centre/ right segments.

```csharp
public static class HeaderFooter
```

## Methods

### `Apply`

__static__

```csharp
static void Apply(Stream output, PdfDocument document, HeaderFooterOptions options)
```

Applies a header and/or footer to `document`.

**Parameters**

- `output` — The stream to write the updated PDF to.
- `document` — The source document.
- `options` — Header/footer content and layout options. <exception cref="ArgumentNullException"> Thrown when any argument is null. </exception>

---

_Source: [`src/Chuvadi.Pdf.Operations/HeaderFooter.cs`](../../../src/Chuvadi.Pdf.Operations/HeaderFooter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
