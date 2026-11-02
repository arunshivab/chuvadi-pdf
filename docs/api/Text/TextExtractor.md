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

### `Superscripts`

```csharp
SuperscriptMode Superscripts
```

Gets or initialises how raised and lowered text (superscripts and subscripts) is written in the extracted text (since 3.29). Default: `SuperscriptMode.Unicode`. Extracted text is a derived representation: the PDF is never changed, and `ExtractFragments(PdfPage)` returns each fragment's own text with its `TextFragment.BaselineShift` whatever the mode.

### `UseTaggedOrder`

```csharp
bool UseTaggedOrder
```

Gets or initialises whether layout-aware extraction follows a Tagged PDF's structure tree (since 3.29). Default true: when the tree orders at least 80% of a page's text, that order is used; otherwise, or when false, columns and rows are found from the page's geometry.

## Methods

### `ExtractText`

```csharp
string ExtractText(PdfPage page) => ExtractText(page, CancellationToken.None)
```

Extracts all text from the given page as a plain Unicode string.

**Parameters**

- `page` — The page to extract text from.

**Returns:** The extracted text, or an empty string when the page has no text.

### `ExtractText`

```csharp
string ExtractText(PdfPage page, CancellationToken cancellationToken)
```

Extracts all text from the given page as a plain Unicode string, stopping when `cancellationToken` is cancelled.

**Parameters**

- `page` — The page to extract text from.
- `cancellationToken` — A token that cancels the extraction.

**Returns:** The extracted text, or an empty string when the page has no text. <exception cref="ArgumentNullException">`page` is null.</exception> <exception cref="OperationCanceledException">The token was cancelled.</exception> <exception cref="PdfLimitExceededException">A limit was exceeded.</exception> <exception cref="PdfException"> The page could not be read. Failures the code did not anticipate are reported as `PdfCorruptionException` with the original as the inner exception. </exception>

**Remarks:** The work runs in a `PdfWorkScope` with the limits the document was opened with (`PdfObjectStore.Limits`); cancellation is noticed well within a second.

### `ExtractText`

```csharp
string ExtractText(PdfPage page, PageLayout layout) => ExtractText(page, layout, CancellationToken.None)
```

Extracts the page's text with layout-aware reading, in the given reading order whatever the extractor's strategy (since 3.29).

**Parameters**

- `page` — The page to extract text from.
- `layout` — The reading order to use.

**Returns:** The extracted text, or an empty string when the page has no text.

**Remarks:** Pass a layout from `AnalyzeLayout(PdfPage)`, changed as the application needs, to override the bands and columns found; `PageLayout.Rows` reads plain rows (the 3.28 order), and `PageLayout.Tagged` uses the structure tree when it orders the page's text, and otherwise finds columns as `ExtractionStrategy.Layout` does.

### `ExtractText`

```csharp
string ExtractText(PdfPage page, PageLayout layout, CancellationToken cancellationToken)
```

Extracts the page's text in the given reading order, stopping when `cancellationToken` is cancelled. See `ExtractText(PdfPage, PageLayout)`, and `ExtractText(PdfPage, CancellationToken)` for limits and exceptions.

**Parameters**

- `page` — The page to extract text from.
- `layout` — The reading order to use.
- `cancellationToken` — A token that cancels the extraction.

**Returns:** The extracted text, or an empty string when the page has no text.

### `AnalyzeLayout`

```csharp
PageLayout AnalyzeLayout(PdfPage page) => AnalyzeLayout(page, CancellationToken.None)
```

Returns the reading order layout-aware extraction uses for the page (since 3.29): `PageLayout.Tagged` when the document's structure tree orders the page's text (and `UseTaggedOrder` is true), otherwise the bands and columns found (`ReadingOrderSource.Columns`), or `PageLayout.Rows` when no band is read in columns.

**Parameters**

- `page` — The page to analyse.

**Returns:** The page's reading order.

### `AnalyzeLayout`

```csharp
PageLayout AnalyzeLayout(PdfPage page, CancellationToken cancellationToken)
```

Returns the reading order layout-aware extraction uses for the page, stopping when `cancellationToken` is cancelled. See `AnalyzeLayout(PdfPage)`, and `ExtractText(PdfPage, CancellationToken)` for limits and exceptions.

**Parameters**

- `page` — The page to analyse.
- `cancellationToken` — A token that cancels the analysis.

**Returns:** The page's reading order.

### `ExtractFragments`

```csharp
List<TextFragment> ExtractFragments(PdfPage page) => ExtractFragments(page, CancellationToken.None)
```

Extracts positioned text fragments from the given page.

**Parameters**

- `page` — The page to extract fragments from.

**Returns:** A list of fragments, or an empty list when the page has no text.

**Remarks:** Each fragment is a piece of text shown by a single Tj or TJ entry with the X, Y position (PDF user space) and font size at the time of rendering. Returned in operator order, not reading order — callers wanting reading order should apply layout reconstruction. Since 3.29 each fragment that is raised or lowered off its line's baseline carries `TextFragment.BaselineShift` and its shift; the text of every fragment is the PDF's own.

### `ExtractFragments`

```csharp
List<TextFragment> ExtractFragments(PdfPage page, CancellationToken cancellationToken)
```

Extracts positioned text fragments from the given page, stopping when `cancellationToken` is cancelled. See `ExtractText(PdfPage, CancellationToken)` for limits and exceptions.

**Parameters**

- `page` — The page to extract fragments from.
- `cancellationToken` — A token that cancels the extraction.

**Returns:** A list of fragments, or an empty list when the page has no text.

---

_Source: [`src/Chuvadi.Pdf.Text/TextExtractor.cs`](../../../src/Chuvadi.Pdf.Text/TextExtractor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
