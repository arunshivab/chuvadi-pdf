# TextExtractor

**Class** in `Chuvadi.Pdf.Text` (Text)

Extracts plain text from a PDF page.

```csharp
public sealed class TextExtractor
```

## Remarks

`TextExtractor` is the top-level public API for Phase 1 text extraction. It wires together all layers: 
 
- Resolves the page's /Contents entry to one or more content streams. 
- Decodes each stream through its filter chain (FlateDecode etc.). 
- Concatenates streams and passes them to `ContentStreamParser`. 
- Applies the chosen `ExtractionStrategy` to the resulting fragments. 
- Returns the extracted text as a plain Unicode string.  Phase 1 scope: born-digital text only. Image-embedded text requires OCR (Phase 3). PDF 32000-1:2008 §9.10 — Extraction of text content.

## Constructors

### `TextExtractor(PdfObjectStore objects, ExtractionStrategy strategy = ExtractionStrategy.Operator)`

Initialises a `TextExtractor` for a document's object store.

**Parameters**

- `objects` — The document's object store, used to resolve references.
- `strategy` — The extraction strategy to use. Defaults to `ExtractionStrategy.Operator`.

## Properties

### `OptionalContent`

```csharp
OptionalContentVisibility? OptionalContent
```

Gets or initialises which optional-content layers are extracted. Default (null): the page's `PdfPage.OptionalContent` — text in layers hidden by the document's default configuration is left out, as Acrobat does. Pass `OptionalContentVisibility.AllVisible` to extract every layer.

## Methods

### `ExtractText`

```csharp
string ExtractText(PdfPage page)
```

Extracts all text from the given page as a plain Unicode string.

**Parameters**

- `page` — The page to extract text from.

**Returns:** The extracted text, or an empty string when the page has no text.

### `ExtractFragments`

```csharp
List<TextFragment> ExtractFragments(PdfPage page)
```

Extracts positioned text fragments from the given page.

**Parameters**

- `page` — The page to extract fragments from.

**Returns:** A list of fragments, or an empty list when the page has no text.

**Remarks:** Each fragment is a piece of text shown by a single Tj or TJ entry with the X, Y position (PDF user space) and font size at the time of rendering. Returned in operator order, not reading order — callers wanting reading order should apply layout reconstruction.

---

_Source: [`src/Chuvadi.Pdf.Text/TextExtractor.cs`](../../../src/Chuvadi.Pdf.Text/TextExtractor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
