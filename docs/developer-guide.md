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
18. [XFA forms](#18-xfa-forms)
19. [PDF/A output](#19-pdf-a-output)
20. [Module map](#20-module-map)
21. [Behaviour changes within 3.x](#21-behaviour-changes-within-3x)

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

Since 3.34.0 a merge keeps the inputs' form fields: a top-level field whose
name an earlier input already uses is renamed `name_2`, `name_3`, and so on.
`MergeOptions` chooses otherwise, and can encrypt the result.
`ShareSameName` makes fields with the same full name one field shown on
every page (`patient.name` in two inputs is one field, below one
`patient`):

```csharp
MergeOptions options = new MergeOptions
{
    FormFields = MergeFormFields.ShareSameName,   // or Drop, RenameOnConflict
    Encryption = EncryptionOptions.Aes256("user-password", "owner-password"),
};
PageOperations.Merge(output, new[] { a, b }, options);
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

Each field also lists its widgets (page, rectangle, on-state, export value),
its choice options, its flags and its maximum length (3.34.0):

```csharp
foreach (FormField field in FormReader.GetLeafFields(doc))
{
    foreach (FormWidget widget in field.Widgets)
    {
        Console.WriteLine($"{field.FullyQualifiedName} on page {widget.PageIndex}, on-state {widget.OnState}");
    }

    bool required = (field.Flags & FormFieldFlags.Required) != 0;
}
```

Values for buttons: a check box takes its own on-state, or `"Yes"`, `"On"` or
`"true"` (any case), to tick it; a radio group takes the on-state or the
export value (`/Opt`) of the button to choose. `"Off"`, `"false"` or an empty
value clears either. Any other value throws `FormException`, naming the
states the button has; pushbuttons are left alone. Text values in any script
(Tamil included) are written as UTF-16 when they are not Latin-1.

### Several values, checks and a result (3.34.0)

A multi-select list box takes several export values through the overload
that takes a list per field and returns a `FormFillResult`:

```csharp
Dictionary<string, IReadOnlyList<string>> many = new Dictionary<string, IReadOnlyList<string>>
{
    ["allergies"] = new[] { "PEN", "LAT" },
    ["patient.name"] = new[] { "அருண்" },
};
FormFillResult result = FormFiller.Fill(output, doc, many, new FormFillOptions
{
    Validate = true,
    UnknownFields = FormUnknownFields.Report,
});
foreach (string name in result.UnknownFields)
{
    Console.WriteLine($"No field named {name}");
}
```

- Choice values are export values (`FormChoiceOption.ExportValue`), not
  display texts. `/I` is set to the chosen options' indices.
- `Validate` (default false in 3.x) refuses a choice value that is not an
  option (an editable combo box takes any value) and text longer than
  `MaxLength`, counted in Unicode code points. Nothing is ever truncated.
- `UnknownFields`: `Ignore` (default), `Report` (this overload only) or
  `Throw`.
- A refused value throws `FormException` with `Reason`, `FieldName`,
  `Value`, `ValidStates` and `MaxLength`: build your own message from them,
  in the reader's language, rather than showing the English `Message`.
- A text or choice fill removes the field's rich-text `/RV`.

### Fill and flatten

To hand on a form whose values can no longer be edited, fill it, then
flatten the filled copy. The flattened values are page content: they print,
extract as text and survive any viewer.

```csharp
using Chuvadi.Pdf.Operations;

byte[] filled;
using (MemoryStream buffer = new MemoryStream())
{
    FormFiller.Fill(buffer, doc, values);
    filled = buffer.ToArray();
}

using PdfDocument filledDoc = PdfDocument.Open(new MemoryStream(filled));
using FileStream flat = File.Create("form_final.pdf");
AnnotationFlattener.Flatten(flat, filledDoc);
```

The filled copy keeps the source's document information and file identifier;
pass `new FormFillOptions { Encryption = ... }` to encrypt it (an encrypted
source is otherwise written unencrypted).

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

`Redactor.ApplyWithReport` writes the same output and reports, per page, how
many glyphs, images, paths, annotations, field values and alternate texts
were removed (3.34.0):

```csharp
RedactionReport report = Redactor.ApplyWithReport(output, doc, options);
foreach (PageRedactionReport page in report.Pages)
{
    Console.WriteLine($"page {page.PageIndex}: {page.GlyphsRemoved} glyphs, {page.ImagesBlanked + page.ImagesRemoved} images");
}
```

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

By default the watermark is centred on the unrotated MediaBox. Set
`PlaceInDisplaySpace = true` (3.34.0; also on `ImageWatermarkOptions` and
`HeaderFooterOptions`) to centre it on the page as a viewer shows it: on the
CropBox, with `RotationDegrees` measured from the upright view of a page
that has `/Rotate`.

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

## 18. XFA forms

`Chuvadi.Pdf.Xfa` renders XFA (Adobe LiveCycle) forms — the dynamic form
technology whose content lives in `/AcroForm /XFA`, outside the normal page
content. Chuvadi performs data merge, positioned and flowing layout, pagination,
and widget/table painting, and runs the form's embedded scripts.

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Xfa;

using PdfDocument doc = PdfDocument.Open("livecycle-form.pdf");

if (doc.IsXfa)
{
    using FileStream output = File.Create("form-rendered.pdf");
    XfaRenderer.Render(output, doc, XfaRenderOptions.Default);
}
```

`XfaRenderOptions.ScriptMode` **defaults to `Full`**: the form's initialize,
calculate, and validate scripts run, so computed fields fill in. Chuvadi ships
two script engines — a FormCalc interpreter and a JavaScript interpreter (both
written from scratch, zero dependencies). To render the static form without
executing scripts:

```csharp
XfaRenderOptions noScripts = new() { ScriptMode = XfaScriptMode.None };
XfaRenderer.Render(output, doc, noScripts);
```

Unsupported script constructs fail soft, leaving form state untouched, so a
single unsupported script never aborts the render. Interactive events
(click/change/etc.) are parsed but dormant — a static render has no event source.

---

## 19. PDF/A output

`Chuvadi.Pdf.PdfA` writes PDF/A-conformant output for archival requirements.

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.PdfA;

using PdfDocument doc = PdfDocument.Open("input.pdf");
using FileStream output = File.Create("archival.pdf");

PdfAOptions options = new() { Conformance = PdfAConformance.PdfA2B };
PdfAResult result = PdfAWriter.Write(output, doc, options);
```

`PdfAConformance` offers `PdfA1B` and `PdfA2B` (level B, visual conformance).
The writer embeds the required output intent, ensures fonts are embedded, and
sets the document metadata PDF/A expects; `PdfAResult` describes what it did.
Note that generating a `/StructTreeRoot` for PDF/UA (level A tagging) is not yet
implemented — see `docs/BACKLOG.md`.

PDF/A-1b dates (3.34.0): `xmp:CreateDate` and `xmp:ModifyDate` follow the
`/Info` dates when those give a time zone. A date without one is left out of
the XMP, because validators read a zone-less XMP date in their own local
zone, and `PdfAResult.Warnings` says which date was left out and why. To
write such dates as UTC instead (also with a warning), set the option:

```csharp
PdfAOptions options = new()
{
    Conformance = PdfAConformance.PdfA1B,
    AssumeUtcForZonelessDates = true,
};
PdfAResult result = PdfAWriter.Write(output, doc, options);
foreach (string warning in result.Warnings)
{
    Console.WriteLine(warning);
}
```

---

## 20. Module map

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
| Render XFA (LiveCycle) forms          | `Chuvadi.Pdf.Xfa`                  |
| PDF/A archival output                 | `Chuvadi.Pdf.PdfA`                 |
| Complex-script (Indic) shaping        | `Chuvadi.Pdf.Text.Shaping`         |
| Encode / decode images                | `Chuvadi.Pdf.Images`               |
| WPF on-screen rendering (Windows)     | `Chuvadi.Pdf.Rendering.Wpf`        |
| Everything (meta-package)             | `Chuvadi.Pdf`                      |

The library has **zero external NuGet dependencies**; each package depends only
on other Chuvadi packages and the .NET base class library.

---

## 21. Behaviour changes within 3.x

These 3.x releases changed what existing calls produce. `CHANGELOG.md` has
the details; where a release gives a way back to the earlier output, it is
named here.

| Release | What changed | Earlier output |
|---|---|---|
| 3.34.0 | `PageOperations.Merge` keeps the inputs' form fields (clashing names renamed `name_2`, ...) | `MergeOptions.FormFields = MergeFormFields.Drop` |
| 3.34.0 | Text watermarks are centred by the real text width, so each one moves slightly | none |
| 3.34.0 | `FormFiller` keeps `/Info` and the file `/ID`; non-Latin-1 values are written as UTF-16; check boxes and radios get their real on-states, and an unknown button value throws `FormException` | none (fixes) |
| 3.34.0 | `FormFiller` removes a field's `/RV` when it writes a text or choice value, and sets `/I` to the chosen options (multi-select lists, or fields that had `/I`) or removes it when the value matches no option | none |
| 3.34.0 | `FormFiller` writes the objects the catalog and `/Info` name (bookmarks, XMP, name trees), which a fill straight after opening used to leave out | none (fix) |
| 3.34.0 | `PdfAWriter` (PDF/A-1b) writes `xmp:CreateDate` and `xmp:ModifyDate` from `/Info` dates that give a time zone | none |
| 3.34.0 | Write paths apply the documents' `PdfLimits` and the caller's `PdfWorkScope`; unexpected failures are `PdfCorruptionException` | none |
| 3.34.0 | Page operations write a reference to an object they did not copy as null | none (fix) |
| 3.34.0 | The stamper and composer also write such references as null, and no longer follow a form XObject's `/Parent` | none (fix) |
| 3.34.0 | Damaged content, fonts, forms, `ToUnicode` and object syntax are read past with a warning; a missing or invalid MediaBox reads as US Letter (it threw); a looping page tree's looping entry is skipped | none |
| 3.34.0 | Crypt filters (`/StmF`, `/StrF`, `/EFF`, a stream's `/Crypt`) are honoured, and a file with only encrypted attachments opens without a password | none (fix) |
| 3.34.0 | With `AnnotationMode.Print` the layers' print state applies; `AnnotationFlattener` keeps an annotation's layer | `LayerVisibility` overrides |
| 3.34.0 | Raster: images are drawn through their full matrix; round joins, elliptical pens, and stroke widths in scaled forms; a soft mask set before a form's `Do` masks it; substitute fonts draw the encoding's glyph | none (fixes) |
| 3.34.0 | SVG: shadings in soft-mask groups paint; an embedded TrueType font gets a cmap matching the raster glyphs when its own would differ | none (fixes) |
| 3.34.0 | Redaction carries bookmarks and XMP, scrubbed by page-less patterns | none |
| 3.34.0 | `PdfAWriter`: annotations made printable (Hidden ones removed), Type 1 subsets get `/CharSet`, XMP dates follow `/Info` | none |
| 3.34.0 | A JBIG2 region of 64 KiB or more counts against `MaxMemoryBytes` (one byte per pixel while decoding) | none |
| 3.34.0 | `PdfAWriter` (PDF/A-1b): an `/Info` date with no time zone is reported in `PdfAResult.Warnings`; `AssumeUtcForZonelessDates` writes it as UTC | default: left out of the XMP, as before |
| 3.34.0 | Inline images are drawn (raster, SVG, WPF), and inline-image data no longer ends the rest of the page | none (fix) |
| 3.34.0 | CMYK JPEGs are read through `/Decode`, not Adobe-inverted: files that do not invert now draw right; SVG embeds them as PNG | none (fix) |
| 3.34.0 | Raster: text whose matrices scale it by more than 2 or less than 0.5 is hinted at its drawn size (Tf x text matrix x CTM; other text at Tf as before), and rotated or skewed text is not hinted; `/Mask` is applied; an embedded Type 1 font with a standard-14 name draws its own glyphs; image data that ends early is drawn | `Hinting = HintingMode.Off` (hinting only) |
| 3.34.0 | LZW: real streams decode past 511 codes, and `FilterPipeline.Encode` writes standard LZW | none (fix) |
| 3.34.0 | Merge, split and assemble write inherited `/Resources`, `/MediaBox` and `/CropBox` on each copied page | none (fix) |
| 3.34.0 | Raster: a fill closes open sub-paths (`m l l f` without `h` now fills); a transparency-group form keeps the outer `/ca` | none (fixes) |
| 3.34.0 | A skipped image is reported (`PdfWarningKind.ImageSkipped`); an indirect `/DecodeParms` is used; a page-tree kid that is not a dictionary is skipped with a warning; `xref` after white space or on one line is read without repair; a stream whose `/Length` does not end at `endstream` is read up to `endstream` | none (fixes) |
| 3.32.0 | Raster, SVG and WPF output show the CropBox (intersected with the MediaBox), not the MediaBox: a different size and origin where the two differ. Use `PageRasterizer.GetOutputSize`, `PdfPage.GetDisplayBox` and `GetDisplayTransform` (3.34.0) to follow it | `DisplayBox = PdfDisplayBox.MediaBox` |
| 3.32.0 | Redaction keeps the parts of a curved stroke outside the box | none |
| 3.32.0 | Embedded font descriptors (`/StemV`, `/Flags`) follow one rule; invalid Flate streams are rejected | none |
| 3.31.0 | Default (View) output shows form values when `/NeedAppearances` is true and draws widgets and comments that have no `/AP`; text extraction returns them | none |
| 3.30.0 | The raster renderer draws text in fonts the PDF does not embed; font resource names are written as valid PDF names | none |
| 3.29.0 | Layout-mode text follows the structure tree, reads columns and groups lines differently; superscripts and subscripts are marked | none |
| 3.28.0 | `TextFragment` positions, widths and font sizes are in user space (they were in text space); rotated text reads as lines of its own | none |
| 3.26.0, 3.27.0 | `TextShaper` became a port of HarfBuzz's layout engine; `ShapingFeatures.Cpsp` was false in 3.26.0 and is true again from 3.27.0 | none |
| 3.21.0, 3.22.0 | Damaged cross-reference recovery and scrub residuals changed; redacted pages drop `/Thumb` and `/PieceInfo` | none |
| 3.16.0 | `XfaRenderOptions.ScriptMode` defaults to `Full`: XFA scripts run | `ScriptMode = XfaScriptMode.None` |

---

*Generated for Chuvadi developers. For a focused first-run walkthrough see
`docs/getting-started.md`; for distribution and publishing see
`docs/DISTRIBUTION.md`.*
