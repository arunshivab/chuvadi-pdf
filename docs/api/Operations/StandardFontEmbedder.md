# StandardFontEmbedder

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Embeds the standard 14 text fonts (Helvetica, Times, Courier) that a document names but does not embed, as subsetted Liberation fonts with the same metrics, so the document looks the same in every viewer (3.33.0).

```csharp
public static class StandardFontEmbedder
```

## Remarks

The fonts are found in the resources of the pages, of the forms they use, of annotation appearances and of the interactive form's default resources (`/DR`). Each substitute keeps the glyphs the original font's encoding named. The document is written as a new file, so a signature in it no longer verifies, and an encrypted document is written without encryption (see `StandardFontEmbedOptions.AllowEncryptedInput`).

## Methods

### `Embed`

__static__

```csharp
static StandardFontEmbedResult Embed(PdfDocument document, Stream output, StandardFontEmbedOptions? options = null)
```

Writes a copy of `document` to `output` in which the non-embedded standard fonts are embedded.

**Parameters**

- `document` — The document. It is not changed.
- `output` — The stream to write the new PDF to.
- `options` — The options; null for the defaults.

**Returns:** Which fonts were embedded and which were not. <exception cref="ArgumentNullException">`document` or `output` is null.</exception> <exception cref="OperationsException"> The document is encrypted and `StandardFontEmbedOptions.AllowEncryptedInput` is false. </exception>

---

_Source: [`src/Chuvadi.Pdf.Operations/StandardFontEmbedder.cs`](../../../src/Chuvadi.Pdf.Operations/StandardFontEmbedder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
