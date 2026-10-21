# PageOperations

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Provides static methods for high-level PDF page operations: merge, split, delete, rotate, and reorder.

```csharp
public static class PageOperations
```

## Remarks

All operations work at the PDF object-graph level — they copy and reassemble page dictionaries without modifying content streams. Each method writes a new PDF to the supplied output stream using `PdfWriter`. The input documents are not modified. PDF 32000-1:2008 §7.7.3 — Page tree nodes and page objects.

## Methods

### `Merge`

__static__

```csharp
static void Merge(Stream output, params PdfDocument[] documents)
```

Merges two or more PDF documents into a single output stream. Pages appear in the order of the input documents.

**Parameters**

- `output` — The stream to write the merged PDF to.
- `documents` — The documents to merge, in order. <exception cref="ArgumentNullException"> Thrown when `output` or `documents` is null. </exception> <exception cref="OperationsException"> Thrown when any document has no pages or an invalid structure. </exception>

### `Merge`

__static__

```csharp
static void Merge(Stream output, IReadOnlyList<PdfDocument> documents, MergeOptions options)
```

Merges two or more PDF documents into a single output stream, optionally carrying each input's outline (bookmarks) into the result with page indices re-based to the merged offsets. Pages appear in the order of the input documents.

**Parameters**

- `output` — The stream to write the merged PDF to.
- `documents` — The documents to merge, in order.
- `options` — Options controlling outline preservation. <exception cref="ArgumentNullException"> Thrown when `output`, `documents`, or `options` is null. </exception> <exception cref="OperationsException"> Thrown when the document list is empty or contains a null document. </exception>

### `Assemble`

__static__

```csharp
static void Assemble(Stream output, IReadOnlyList<PageSelector> pages)
```

Assembles a new PDF from an ordered list of source pages, each identified by a `PageSelector`. Unlike `ReorderPages` (a single-document permutation), the same page may appear any number of times and selectors may interleave pages from different source documents, all in one write. Output page order is exactly the order of `pages`.

**Parameters**

- `output` — The stream to write the assembled PDF to.
- `pages` — The ordered source pages; duplicates are allowed. <exception cref="ArgumentNullException"> Thrown when `output` or `pages` is null. </exception> <exception cref="OperationsException"> Thrown when the list is empty, a selector has a null document, or a page index is out of range for its source document. </exception>

### `SplitPages`

__static__

```csharp
static List<MemoryStream> SplitPages(PdfDocument document)
```

Splits a document into individual single-page PDFs.

**Parameters**

- `document` — The document to split.

**Returns:** A list of `MemoryStream` objects, one per page, each containing a valid single-page PDF.

### `ExtractPages`

__static__

```csharp
static void ExtractPages(Stream output, PdfDocument document, int startIndex, int count)
```

Extracts a contiguous range of pages from a document into a new PDF.

**Parameters**

- `output` — The stream to write the extracted pages to.
- `document` — The source document.
- `startIndex` — Zero-based index of the first page to include.
- `count` — The number of pages to include.

### `DeletePages`

__static__

```csharp
static void DeletePages(Stream output, PdfDocument document, IEnumerable<int> pageIndices)
```

Writes a new PDF containing all pages except those at the given indices.

**Parameters**

- `output` — The stream to write the result to.
- `document` — The source document.
- `pageIndices` — Zero-based indices of pages to remove.

### `RotatePages`

__static__

```csharp
static void RotatePages(Stream output, PdfDocument document, int degrees, IEnumerable<int>? pageIndices = null)
```

Writes a new PDF with the specified pages rotated by the given angle.

**Parameters**

- `output` — The stream to write the result to.
- `document` — The source document.
- `degrees` — Rotation in degrees. Must be 0, 90, 180, or 270. Applied in addition to any existing /Rotate value on the page.
- `pageIndices` — Zero-based indices of pages to rotate. Pass null or empty to rotate all pages.

### `ReorderPages`

__static__

```csharp
static void ReorderPages(Stream output, PdfDocument document, IReadOnlyList<int> newOrder)
```

Writes a new PDF with pages in the order specified by `newOrder`.

**Parameters**

- `output` — The stream to write the result to.
- `document` — The source document.
- `newOrder` — A permutation of zero-based page indices specifying the new order. Must contain exactly one entry per page.

---

_Source: [`src/Chuvadi.Pdf.Operations/PageOperations.cs`](../../../src/Chuvadi.Pdf.Operations/PageOperations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
