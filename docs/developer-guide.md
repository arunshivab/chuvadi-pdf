# Chuvadi Developer Guide

A practical, example-driven guide to using **Chuvadi** — a zero-dependency
.NET 10 PDF library — in your own applications. Every code sample here is
written against the public API as shipped.

Chuvadi is a general-purpose PDF library: read, render, extract, transform,
author, and inspect PDF documents. It has **no external NuGet dependencies** —
every byte is parsed and every pixel is rendered in managed C#.

---

## Table of contents

1. [Installation](#1-installation)
2. [Core concepts](#2-core-concepts)
3. [Opening a document](#3-opening-a-document)
4. [Rendering pages to images and SVG](#4-rendering-pages-to-images-and-svg)
5. [Extracting text](#5-extracting-text)
6. [Page operations](#6-page-operations-merge-split-extract-delete-rotate-reorder)
7. [Filling and reading forms](#7-filling-and-reading-forms)
8. [Redaction](#8-redaction)
9. [Watermarks](#9-watermarks)
10. [Annotations](#10-annotations)
11. [Outlines (bookmarks)](#11-outlines-bookmarks)
12. [Creating PDFs](#12-creating-pdfs)
13. [Compressing PDFs](#13-compressing-pdfs)
14. [Reading and verifying signatures](#14-reading-and-verifying-signatures)
15. [Building an interactive viewer](#15-building-an-interactive-viewer)
16. [Advanced: the display list and custom adapters](#16-advanced-the-display-list-and-custom-adapters)
17. [Error handling and patterns](#17-error-handling-and-patterns)
18. [Module map](#18-module-map)

---

## 1. Installation

Chuvadi ships as a set of NuGet packages — one per module — plus a convenience
meta-package that pulls in the whole cross-platform stack.

The simplest start: install the meta-package and get everything.

```bash
dotnet add package Chuvadi.Pdf
```

If you want a minimal dependency surface, install only the modules you use
(for example `Chuvadi.Pdf.Documents` + `Chuvadi.Pdf.Text` for text extraction).
The [module map](#18-module-map) shows what each package provides.

Target framework: **.NET 10**. Windows-only WPF rendering lives in the separate
`Chuvadi.Pdf.Rendering.Wpf` package.

---

## 2. Core concepts

A handful of types appear throughout the API:

- **`PdfDocument`** (`Chuvadi.Pdf.Documents`) — an open document. It is
  `IDisposable`; always dispose it (a `using` declaration is simplest).
- **`doc.Pages`** — the page collection; `doc.Pages[i]` is a `PdfPage`.
- **`doc.PageCount`** — number of pages.
- **`doc.Objects`** — the underlying object store. Several lower-level APIs
  (text extraction, rasterization) take this rather than the document.

Two patterns recur:

- **Read / transform APIs** open a `PdfDocument` and write the result to a
  `Stream` you provide (file, memory, network). The input document is never
  mutated in place.
- **Streams vs paths** — most entry points accept either. When you pass a
  stream, `leaveOpen` controls whether Chuvadi closes it for you.

```csharp
using Chuvadi.Pdf.Documents;

using PdfDocument doc = PdfDocument.Open("input.pdf");
Console.WriteLine($"The document has {doc.PageCount} page(s).");
```

---

## 3. Opening a document

There are four `Open` overloads:

```csharp
using Chuvadi.Pdf.Documents;

// From a path
using PdfDocument a = PdfDocument.Open("input.pdf");

// From a path, password-protected
using PdfDocument b = PdfDocument.Open("secure.pdf", "the-password");

// From a stream (leaveOpen: false closes the stream when the document is disposed)
using FileStream fs = File.OpenRead("input.pdf");
using PdfDocument c = PdfDocument.Open(fs, leaveOpen: false);

// From a stream, password-protected
using FileStream sfs = File.OpenRead("secure.pdf");
using PdfDocument d = PdfDocument.Open(sfs, "the-password", leaveOpen: false);
```

Encrypted documents are decrypted transparently once the correct password is
supplied; from then on the document behaves like any other. (Chuvadi reads and
decrypts protected PDFs; producing newly-encrypted output is not part of the
high-level API.)

Use `leaveOpen: true` when you need to keep reading from the same stream after
the document is disposed, or when the same stream feeds several documents.

---

## 4. Rendering pages to images and SVG

The simplest correct way to turn a page into an image or SVG is the one-call
render facade in `Chuvadi.Pdf.Reader`. Open a document, call one method.

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Reader;

using PdfDocument doc = PdfDocument.Open("input.pdf");

File.WriteAllBytes("page0.png", doc.RenderPageToPng(0));        // PNG, 150 DPI
File.WriteAllBytes("page0.jpg", doc.RenderPageToJpeg(0, 300));  // JPEG, 300 DPI
File.WriteAllBytes("page0.bmp", doc.RenderPageToBmp(0));        // BMP
File.WriteAllBytes("page0.tif", doc.RenderPageToTiff(0));       // TIFF
File.WriteAllText ("page0.svg", doc.RenderPageToSvg(0));        // SVG (selectable text)
```

Every raster method takes a DPI (default 150 — good for screen; use 300 for
print). Every method also has a `Stream` overload, so you can render straight to
a response body or a file without an intermediate buffer:

```csharp
using FileStream output = File.Create("page0.png");
doc.RenderPageToPng(0, output, dpi: 200);
```

JPEG additionally takes a quality (1–100, default 85):

```csharp
byte[] jpeg = doc.RenderPageToJpeg(0, dpi: 150, quality: 90);
```

To render **every page into a single multi-page TIFF**:

```csharp
File.WriteAllBytes("document.tiff", doc.RenderToTiff(dpi: 150));
```

### When to use SVG vs raster

- **SVG** preserves selectable text and embeds fonts — ideal for an on-screen
  viewer where users select or search text, and for crisp scaling.
- **Raster (PNG/JPEG/BMP/TIFF)** produces fixed pixels — ideal for thumbnails,
  print, or pipelines that consume images.

> **Displaying SVG in HTML:** render the SVG, then show it *isolated* (an
> `<object>`/`<iframe>`, or a sandboxed document) rather than injecting it inline
> into a styled page. Chuvadi's SVG is self-contained; an inline injection lets
> the host page's CSS cascade into the SVG `<text>` elements and shift glyphs.

### Lower-level rendering

If you need direct control, the facade is a thin wrapper over two public
building blocks you can use yourself:

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;

using PdfDocument doc = PdfDocument.Open("input.pdf");

PageRasterizer rasterizer = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = 200 });
PixelBuffer pixels = rasterizer.Rasterize(doc.Pages[0]);
// pixels.Width, pixels.Height, pixels.GetPixelBgra(x, y) ...
```

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Svg;

using PdfDocument doc = PdfDocument.Open("input.pdf");
string svg = new SvgRenderer().RenderPage(doc, 0);
```

---

## 5. Extracting text

`TextExtractor` (`Chuvadi.Pdf.Text`) turns page content into text. It takes the
object store and an extraction strategy.

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Text;

using PdfDocument doc = PdfDocument.Open("input.pdf");

TextExtractor extractor = new TextExtractor(doc.Objects, ExtractionStrategy.Layout);
for (int i = 0; i < doc.PageCount; i++)
{
    string text = extractor.ExtractText(doc.Pages[i]);
    Console.WriteLine(text);
}
```

Strategies:

- **`ExtractionStrategy.Layout`** — reconstructs reading order from glyph
  positions. Handles multi-column layouts, tables, and mixed direction text.
  The default choice for most documents.
- **`ExtractionStrategy.Operator`** — fastest; preserves the order operators
  appear in the content stream. Best for simple single-column text.

---

## 6. Page operations (merge, split, extract, delete, rotate, reorder)

`PageOperations` (`Chuvadi.Pdf.Operations`) provides document-level page
surgery. Each method writes its result to an output stream.

### Merge several documents

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;

using PdfDocument a = PdfDocument.Open("a.pdf");
using PdfDocument b = PdfDocument.Open("b.pdf");
using PdfDocument c = PdfDocument.Open("c.pdf");

using FileStream output = File.Create("merged.pdf");
PageOperations.Merge(output, a, b, c);   // params PdfDocument[]
```

### Split into one document per page

```csharp
using PdfDocument doc = PdfDocument.Open("input.pdf");
List<MemoryStream> pages = PageOperations.SplitPages(doc);
for (int i = 0; i < pages.Count; i++)
{
    File.WriteAllBytes($"page-{i}.pdf", pages[i].ToArray());
    pages[i].Dispose();
}
```

### Extract, delete, rotate, reorder

```csharp
using PdfDocument doc = PdfDocument.Open("input.pdf");

// Extract 3 pages starting at index 0 into a new document
using (FileStream output = File.Create("extracted.pdf"))
{
    PageOperations.ExtractPages(output, doc, startIndex: 0, count: 3);
}

// Delete pages 1 and 3
using (FileStream output = File.Create("trimmed.pdf"))
{
    PageOperations.DeletePages(output, doc, new[] { 1, 3 });
}

// Rotate pages 0 and 1 by 90 degrees
using (FileStream output = File.Create("rotated.pdf"))
{
    PageOperations.RotatePages(output, doc, 90, new[] { 0, 1 });
}

// Reorder: new order is page indices from the source
using (FileStream output = File.Create("reordered.pdf"))
{
    PageOperations.ReorderPages(output, doc, new[] { 2, 0, 1 });
}
```

> Open source documents with `leaveOpen: true` if you intend to run several
> operations from the same `PdfDocument` before disposing it.

---

## 7. Filling and reading forms

`Chuvadi.Pdf.Forms` reads and fills AcroForm fields.

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;

using PdfDocument doc = PdfDocument.Open("form.pdf");

// Read every field's name and current value
foreach (FormField field in FormReader.GetFields(doc))
{
    Console.WriteLine($"{field.FullyQualifiedName} = \"{field.Value}\"");
}

// Fill fields by name and write a filled copy
Dictionary<string, string> values = new Dictionary<string, string>
{
    ["patient.name"] = "Jane Doe",
    ["patient.dob"] = "1985-04-12",
};

using FileStream output = File.Create("form_filled.pdf");
FormFiller.Fill(output, doc, values);
```

`FormFiller.Fill` sets `/NeedAppearances=true` so viewers regenerate field
appearances with their own renderers — the most reliable cross-viewer result.

---

## 8. Redaction

`Chuvadi.Pdf.Redaction` performs **byte-level** redaction: text whose glyphs
fall inside a rectangle is removed from the content stream, and the original
content-stream objects are excluded from the output. This is true removal, not
a black box drawn on top.

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Redaction;

using PdfDocument doc = PdfDocument.Open("patient_chart.pdf");

RedactionOptions options = new RedactionOptions();
options.Rectangles.Add(new RedactionRect(
    pageIndex: 0,
    bounds: new RectangleF(90, 700, 200, 30)));

using FileStream output = File.Create("patient_chart_redacted.pdf");
Redactor.Apply(output, doc, options);
```

Searching the output bytes for the redacted text returns nothing — the data is
gone, not hidden. Coordinates are in PDF user space (origin bottom-left).

---

## 9. Watermarks

`Chuvadi.Pdf.Watermark` overlays a text watermark on every page. The original
page content is untouched; the watermark is appended as an overlay.

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Watermark;

using PdfDocument doc = PdfDocument.Open("input.pdf");

TextWatermarkOptions options = new TextWatermarkOptions("DRAFT")
{
    FontSize = 72.0,
    Color = ColorF.FromGray(0.5f),
    Opacity = 0.25f,
    RotationDegrees = 45.0,
};

using FileStream output = File.Create("watermarked.pdf");
WatermarkStamper.ApplyText(output, doc, options);
```

---

## 10. Annotations

`Chuvadi.Pdf.Annotations` reads existing annotations and adds new ones.

```csharp
using Chuvadi.Pdf.Annotations;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;

using PdfDocument doc = PdfDocument.Open("input.pdf");

// Read
foreach (PdfAnnotation annotation in AnnotationReader.GetAllAnnotations(doc))
{
    Console.WriteLine($"Page {annotation.PageIndex + 1}: {annotation.Type} — {annotation.Contents}");
}

// Add
List<PdfAnnotation> additions = new List<PdfAnnotation>
{
    new TextAnnotation(
        pageIndex: 0,
        rect: new RectangleF(50, 700, 24, 24),
        contents: "Reviewed",
        author: "Dr Smith"),
};

using FileStream output = File.Create("annotated.pdf");
AnnotationWriter.Add(output, doc, additions);
```

Supported subtypes include Text, Link, FreeText, Highlight, Underline, Squiggly,
StrikeOut, Stamp, and Ink. Unsupported subtypes read back as a generic
annotation so nothing is lost.

---

## 11. Outlines (bookmarks)

`OutlineReader` returns the document's outline tree.

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;

using PdfDocument doc = PdfDocument.Open("input.pdf");

foreach (OutlineItem item in OutlineReader.GetOutlines(doc))
{
    Console.WriteLine(item.Title);
    // item has child items for nested bookmarks
}
```

---

## 12. Creating PDFs

`Chuvadi.Pdf.Authoring` builds new documents. Two entry points cover most needs:
a flowing report builder, and an image-to-PDF converter.

### Reports

`ReportBuilder` lays out flowing content with automatic pagination, repeating
table headers, and page numbers.

```csharp
using Chuvadi.Pdf.Authoring;

ReportTable table = new ReportTable();
table.AddColumn("Patient").AddColumn("Ward").AddColumn("Status");
table.AddRow("A. Kumar", "3B", "Discharged");
table.AddRow("S. Devi", "ICU", "Stable");

ReportBuilder.Create()
    .SetTitle("Daily Census")
    .WithFooter(new HeaderFooterStyle { Text = "Page {page} of {total}" })
    .AddHeading("Daily Census")
    .AddParagraph("Generated by Chuvadi.")
    .AddTable(table)
    .SaveToFile("census.pdf");
```

### Images to PDF

`ImagePdfConverter` wraps one or more images into a PDF. Input formats include
JPEG, PNG, TIFF, and BMP; alpha is preserved.

```csharp
using Chuvadi.Pdf.Authoring;

// One image, by file path
ImagePdfConverter.ConvertFile("scan.png", "scan.pdf");

// Several images into one multi-page PDF, from bytes
byte[][] images =
{
    File.ReadAllBytes("page1.jpg"),
    File.ReadAllBytes("page2.jpg"),
};
byte[] pdf = ImagePdfConverter.Convert(images);
File.WriteAllBytes("scans.pdf", pdf);
```

---

## 13. Compressing PDFs

`PdfCompressor` (`Chuvadi.Pdf.Operations`) re-writes a document with compressed
streams and returns a result describing the savings.

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;

using PdfDocument doc = PdfDocument.Open("large.pdf");
using FileStream output = File.Create("smaller.pdf");

CompressionResult result = PdfCompressor.Compress(doc, output);
Console.WriteLine($"Compression complete: {result}");
```

Pass a `CompressionOptions` to tune behaviour:

```csharp
CompressionResult result = PdfCompressor.Compress(doc, output, new CompressionOptions());
```

---

## 14. Signatures: reading, verifying, and signing

`Chuvadi.Pdf.Signatures` both reads existing digital signatures and creates new
ones. For reading, it exposes the exact byte ranges each signature covers so you
can verify them with your own cryptographic stack:

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Signatures;

using PdfDocument doc = PdfDocument.Open("signed.pdf");

foreach (PdfSignature signature in doc.Signatures())
{
    Console.WriteLine($"Signed at: {signature.SigningTimeFromDictionary}");

    // The exact bytes the signature covers — feed these to your verifier.
    byte[] signedBytes = doc.GetSignedBytes(signature);
    Console.WriteLine($"Covers {signedBytes.Length} bytes.");
}
```

For creating signatures, the `Signing` APIs operate on PDF bytes and append an
incremental update (the original bytes are preserved verbatim):

- `PdfCounterSigner.AddSignature(byte[] pdf, ISigner signer, PdfSigningOptions options)`
  — add a signature; you supply an `ISigner` wrapping your key/certificate.
- `PdfDocumentTimestamper.AddDocumentTimestamp(byte[] pdf, Options options)`
  — add an RFC 3161 document timestamp.
- `PdfLtvUpdater.AddLtvMaterial(byte[] pdf, LtvOptions material)`
  — embed long-term-validation material (certificates, CRLs, OCSP responses) so
  a signature stays verifiable after its certificates expire.

Because signing depends on your certificate and signing service, see the API
reference under `docs/api/Signatures/` for the `ISigner`, `PdfSigningOptions`,
and `LtvOptions` details.

---

## 15. Building an interactive viewer

For an interactive reader application (Blazor, WPF, MAUI), the
`Chuvadi.Pdf.Reader` package provides `IPdfReader` — an async facade tailored to
viewers: open, render pages and thumbnails as SVG, traverse the outline, stream
search matches, and read text-run geometry. Depend on the interface so it is
easy to mock in tests; use the supplied `ChuvadiPdfReader` implementation at
runtime.

```csharp
using Chuvadi.Pdf.Reader;
using Chuvadi.Pdf.Rendering.DisplayList;

IPdfReader reader = new ChuvadiPdfReader();

using FileStream fs = File.OpenRead("input.pdf");
using PdfDocument doc = await reader.OpenAsync(fs, "input.pdf");

string pageSvg = await reader.RenderPageSvgAsync(doc, pageIndex: 0);
string thumbnail = await reader.RenderThumbnailAsync(doc, pageIndex: 0);

await foreach (SearchMatch match in reader.SearchAsync(doc, "diagnosis", new SearchOptions()))
{
    Console.WriteLine($"Match on page {match.PageNumber}");
}
```

For the simple "render a page" case outside a viewer, prefer the
[render facade](#4-rendering-pages-to-images-and-svg) (`doc.RenderPageToSvg(i)` /
`RenderPageToPng(i)`), which is synchronous and covers raster formats too.

---

## 16. Advanced: the display list and custom adapters

Internally, rendering is a two-stage pipeline: a page is first turned into a
neutral **`PageDisplayList`** (an ordered list of draw operations), which is then
consumed by an output adapter — the rasterizer for pixels, the SVG renderer for
SVG, the WPF renderer for `DrawingVisual`. You can build the display list once
and feed it to whichever adapter you need.

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering.DisplayList;
using Chuvadi.Pdf.Svg;

using PdfDocument doc = PdfDocument.Open("input.pdf");

PageDisplayList list = doc.BuildDisplayList(0);   // extension on PdfDocument
string svg = new SvgRenderer().Render(list);
```

On Windows, the `Chuvadi.Pdf.Rendering.Wpf` package consumes the same display
list to produce a WPF `DrawingVisual`, so the rendering you see on screen comes
from the identical pipeline as your exported images.

---

## 17. Error handling and patterns

- **Always dispose `PdfDocument`.** Use a `using` declaration. When opening from
  a stream, decide `leaveOpen` deliberately: `false` lets the document own and
  close the stream; `true` keeps the stream open for further use.
- **Transform APIs write to a stream you own.** They never modify the input
  document in place, so you can keep using the source document afterwards (open
  it with `leaveOpen: true` if you will).
- **Wrong password** throws when opening an encrypted document — catch and
  prompt the user.
- **Page indices are zero-based** and validated; out-of-range indices throw
  `ArgumentOutOfRangeException`.
- **Coordinates are PDF user space** (origin bottom-left, points), except raster
  pixel buffers, which are top-left.
- **Thread safety:** a single `PdfDocument` is not designed for concurrent
  mutation; render or extract from one document on one thread at a time, or open
  separate documents per thread.

A typical robust open:

```csharp
using Chuvadi.Pdf.Documents;

try
{
    using PdfDocument doc = PdfDocument.Open(path);
    // ... use doc ...
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not open PDF: {ex.Message}");
}
```

---

## 18. Module map

| You want to…                          | Package                            |
|---------------------------------------|------------------------------------|
| Open / parse documents                | `Chuvadi.Pdf.Documents`            |
| One-call render + interactive viewer  | `Chuvadi.Pdf.Reader`               |
| Rasterize pages (PNG/BMP/TIFF/JPEG)    | `Chuvadi.Pdf.Rendering`            |
| Render to SVG                         | `Chuvadi.Pdf.Svg`                  |
| Extract text                          | `Chuvadi.Pdf.Text`                 |
| Merge / split / rotate / reorder      | `Chuvadi.Pdf.Operations`           |
| Compress documents                    | `Chuvadi.Pdf.Operations`           |
| Read / fill AcroForms                 | `Chuvadi.Pdf.Forms`                |
| Redact (byte-level)                   | `Chuvadi.Pdf.Redaction`            |
| Watermark                             | `Chuvadi.Pdf.Watermark`            |
| Read / add annotations                | `Chuvadi.Pdf.Annotations`          |
| Author reports / images → PDF         | `Chuvadi.Pdf.Authoring`            |
| Read / verify / create signatures     | `Chuvadi.Pdf.Signatures`           |
| Encode / decode images                | `Chuvadi.Pdf.Images`               |
| WPF on-screen rendering (Windows)     | `Chuvadi.Pdf.Rendering.Wpf`        |
| Everything (meta-package)             | `Chuvadi.Pdf`                      |

The library has **zero external NuGet dependencies**; each package depends only
on other Chuvadi packages and the .NET base class library.

---

*Generated for Chuvadi developers. For a focused first-run walkthrough see
`docs/getting-started.md`; for distribution and publishing see
`docs/DISTRIBUTION.md`.*
