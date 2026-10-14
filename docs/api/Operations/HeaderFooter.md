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

### `ResolveText`

__static__

```csharp
static string ResolveText(string template, int pageIndex, int totalPages, HeaderFooterOptions options)
```

Substitutes header/footer tokens in a single template for one page, using the same context `Apply` builds (1-based page number, total pages, `HeaderFooterOptions.FilePath` and `HeaderFooterOptions.Timestamp`).

**Parameters**

- `template` — Template text containing tokens such as `{page}`.
- `pageIndex` — Zero-based page index.
- `totalPages` — Total page count.
- `options` — Supplies the file path and timestamp tokens.

**Returns:** The substituted text. <exception cref="ArgumentNullException"> Thrown when `template` or `options` is null. </exception>

---

_Source: [`src/Chuvadi.Pdf.Operations/HeaderFooter.cs`](../../../src/Chuvadi.Pdf.Operations/HeaderFooter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
