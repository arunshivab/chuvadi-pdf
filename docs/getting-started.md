# Getting Started with Chuvadi

This guide gets you from `git clone` to working code in roughly 10 minutes.
By the end you'll know how to extract text, render pages, apply a watermark,
perform PHI-safe redaction, and call into the rest of the library from your
own projects.

If you only want the command-line tool, skip to
[Using the CLI](#using-the-cli).

---

## Prerequisites

- **.NET 10 SDK** — `dotnet --version` should report `10.x`.
  Download: <https://dotnet.microsoft.com/download>
- **Git** — for cloning the repository.
- **A test PDF** — any PDF will do. Chuvadi doesn't ship sample documents.

That's the complete dependency list. Chuvadi has zero NuGet packages in
production code; you need nothing else.

---

## Clone and build

```bash
git clone https://github.com/arunshivab/chuvadi-pdf.git
cd chuvadi
dotnet build
dotnet test
```

Expected: 33 packable projects plus tests build, and 2,392 tests pass across 30
in-solution test projects. First build takes 1–2 minutes; later builds are seconds.

If the build fails, the most likely cause is an SDK mismatch. Run
`dotnet --list-sdks` and confirm a 10.x SDK is installed.

---

## Your first 5 minutes: text extraction

The shortest path to seeing Chuvadi work end to end is the text-extraction
example:

```bash
dotnet run --project examples/Chuvadi.Examples.TextExtraction -- path/to/your.pdf
```

This prints every page's text using both the Operator and Layout strategies.
Compare the two outputs — Layout reconstructs reading order from glyph
positions; Operator preserves the raw content-stream order.

If you see your document's text, the entire pipeline is working: parser,
content-stream interpreter, font handling, and text reconstruction.

---

## The eight capabilities

Each capability is a focused module under `src/`. Pick the one you need; the
rest stay out of your dependency tree.

### 1. Text extraction — `Chuvadi.Pdf.Text`

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Text;

using FileStream fs = File.OpenRead("input.pdf");
using PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false);

TextExtractor extractor = new(doc.Objects, ExtractionStrategy.Layout);
for (int i = 0; i < doc.PageCount; i++)
{
    Console.WriteLine(extractor.ExtractText(doc.Pages[i]));
}
```

Strategies:
- `Operator` — fastest. Preserves the order operators appear in the content
  stream. Best for simple single-column documents.
- `Layout` — reconstructs reading order from glyph positions. Handles
  multi-column layouts, tables, mixed RTL/LTR text. **Default choice.**

Runnable demo: [`examples/Chuvadi.Examples.TextExtraction`](../examples/Chuvadi.Examples.TextExtraction/README.md)

### 2. Page rendering (PDF → PNG) — `Chuvadi.Pdf.Rendering`

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering;

using FileStream fs = File.OpenRead("input.pdf");
using PdfDocument doc = PdfDocument.Open(fs, leaveOpen: false);

RenderOptions opts = new() { Dpi = 150.0 };
PageRasterizer rasterizer = new(doc.Objects, opts);

byte[] png = rasterizer.RasterizeToPng(doc.Pages[0]);
File.WriteAllBytes("page1.png", png);
```

Zero native dependencies — every pixel is computed in managed C# via the
built-in scanline rasterizer. Supports the 14 standard PDF fonts plus
embedded TrueType.

DPI guidance: 96 for thumbnails, 150 for readable previews, 300 for print.

Runnable demo: [`examples/Chuvadi.Examples.Render`](../examples/Chuvadi.Examples.Render/README.md)

### 3. Text watermarks — `Chuvadi.Pdf.Watermark`

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Watermark;

using FileStream input = File.OpenRead("input.pdf");
using PdfDocument doc = PdfDocument.Open(input, leaveOpen: false);

TextWatermarkOptions opts = new("DRAFT")
{
    FontSize        = 72.0,
    Color           = ColorF.FromGray(0.5f),
    Opacity         = 0.25f,
    RotationDegrees = 45.0,
};

using FileStream output = File.Create("output.pdf");
WatermarkStamper.ApplyText(output, doc, opts);
```

Watermarks are appended as a content stream overlay; the original page
content is untouched.

Runnable demo: [`examples/Chuvadi.Examples.Watermark`](../examples/Chuvadi.Examples.Watermark/README.md)

### 4. PHI-safe redaction — `Chuvadi.Pdf.Redaction`

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Redaction;

using FileStream input = File.OpenRead("patient_chart.pdf");
using PdfDocument doc = PdfDocument.Open(input, leaveOpen: false);

RedactionOptions opts = new();
opts.Rectangles.Add(new RedactionRect(
    pageIndex: 0,
    bounds:    new RectangleF(90, 700, 200, 30)));

using FileStream output = File.Create("patient_chart_redacted.pdf");
Redactor.Apply(output, doc, opts);
```

**This is byte-level removal, not visual cover-up.** Text-showing operators
whose glyphs fall inside the rectangle are deleted from the content stream,
and the original content-stream indirect objects are excluded from the
output. Searching the output bytes for the redacted text returns nothing.

This guarantee is formalised in [BASELINE.md §B15](BASELINE.md).

Runnable demo: [`examples/Chuvadi.Examples.Redaction`](../examples/Chuvadi.Examples.Redaction/README.md)
— includes a byte-search verifier.

### 5. AcroForms — `Chuvadi.Pdf.Forms`

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;

using FileStream input = File.OpenRead("form.pdf");
using PdfDocument doc = PdfDocument.Open(input, leaveOpen: false);

// Read
foreach (FormField f in FormReader.GetFields(doc))
{
    Console.WriteLine($"{f.FullyQualifiedName} = \"{f.Value}\"");
}

// Fill
Dictionary<string, string> values = new()
{
    ["patient.name"] = "Jane Doe",
    ["patient.dob"]  = "1985-04-12",
};

using FileStream output = File.Create("form_filled.pdf");
FormFiller.Fill(output, doc, values);
```

`FormFiller.Fill` writes `/NeedAppearances=true` so viewers regenerate
appearance streams using their own renderers — the most reliable
cross-viewer behaviour.

Runnable demo: [`examples/Chuvadi.Examples.FormFill`](../examples/Chuvadi.Examples.FormFill/README.md)

### 6. Page operations — `Chuvadi.Pdf.Operations`

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Operations;

// Merge
using FileStream a = File.OpenRead("a.pdf");
using FileStream b = File.OpenRead("b.pdf");
using PdfDocument docA = PdfDocument.Open(a, leaveOpen: true);
using PdfDocument docB = PdfDocument.Open(b, leaveOpen: true);
using FileStream merged = File.Create("merged.pdf");
PageOperations.Merge(merged, docA, docB);

// Delete pages
using FileStream input = File.OpenRead("input.pdf");
using PdfDocument doc = PdfDocument.Open(input, leaveOpen: false);
using FileStream trimmed = File.Create("trimmed.pdf");
PageOperations.DeletePages(trimmed, doc, new[] { 0, 3, 7 });
```

Also available: `SplitPages`, `ExtractPages`, `RotatePages`, `ReorderPages`.

Runnable demo: [`examples/Chuvadi.Examples.PageOps`](../examples/Chuvadi.Examples.PageOps/README.md)

### 7. Annotations — `Chuvadi.Pdf.Annotations`

```csharp
using Chuvadi.Pdf.Annotations;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;

using FileStream input = File.OpenRead("input.pdf");
using PdfDocument doc = PdfDocument.Open(input, leaveOpen: true);

// Read
foreach (PdfAnnotation a in AnnotationReader.GetAllAnnotations(doc))
{
    Console.WriteLine($"Page {a.PageIndex + 1}: {a.Type} — {a.Contents}");
}

// Add
List<PdfAnnotation> additions = new()
{
    new TextAnnotation(
        pageIndex: 0,
        rect:      new RectangleF(50, 700, 24, 24),
        contents:  "Reviewed",
        author:    "Dr Smith"),
};

using FileStream output = File.Create("annotated.pdf");
AnnotationWriter.Add(output, doc, additions);
```

Supported subtypes: Text, Link, FreeText, Highlight, Underline, Squiggly,
StrikeOut, Stamp, Ink. Other subtypes read as `GenericAnnotation`.

Runnable demo: [`examples/Chuvadi.Examples.Annotations`](../examples/Chuvadi.Examples.Annotations/README.md)

### 8. XFA forms — `Chuvadi.Pdf.Xfa`

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Xfa;

using PdfDocument doc = PdfDocument.Open("livecycle-form.pdf");

if (doc.IsXfa)
{
    using FileStream output = File.Create("form-rendered.pdf");

    // ScriptMode defaults to Full: the form's initialize/calculate/validate
    // scripts run, so computed fields fill in. Pass
    // new XfaRenderOptions { ScriptMode = XfaScriptMode.None } to opt out.
    XfaRenderer.Render(output, doc, XfaRenderOptions.Default);
}
```

Chuvadi renders XFA (Adobe LiveCycle) forms — data merge, positioned and flowing
layout, pagination, tables, and widgets — and runs the embedded FormCalc and
JavaScript form scripts. Unsupported script constructs fail soft, leaving form
state untouched, so one bad script never aborts a render.

---

## Using the CLI

The `Chuvadi.Pdf.Cli` project produces a `chuvadi` executable with verbs for
the common operations. After `dotnet build`, the binary is at
`tools/Chuvadi.Pdf.Cli/bin/Debug/net10.0/chuvadi`.

```bash
chuvadi info patient.pdf
chuvadi extract-text patient.pdf --strategy layout
chuvadi render report.pdf --output page1.png --page 0 --dpi 150
chuvadi watermark report.pdf --output draft.pdf --text DRAFT --opacity 0.3
chuvadi redact patient.pdf --output redacted.pdf --rect 0,90,100,200,30
chuvadi form-fill form.pdf --output filled.pdf --field name=Jane
chuvadi outlines manual.pdf
chuvadi merge a.pdf b.pdf --output merged.pdf
chuvadi split big.pdf --output-dir pages/
chuvadi delete input.pdf --output trimmed.pdf --pages 0,3,7
chuvadi rotate input.pdf --output rotated.pdf --page 2 --degrees 90
```

Run `chuvadi help` for the full verb list. Debug verbs (`tokenize`,
`dump-objects`, `parse-content`, `decode-stream`, `inspect-xref`,
`validate-fonts`) are useful when investigating a problematic PDF.

---

## Using Chuvadi from your own project

Chuvadi is packaged (33 mono-versioned packages) but published to **local folder
feeds only**, not nuget.org. Three ways to integrate:

### Option A — Local NuGet feed (recommended)

`build\pack.ps1 -Version x.y.z` writes the full `.nupkg` set to
`artifacts\nupkg`. Copy those files into a folder your project treats as a NuGet
source (a `nuget.config` `<packageSources>` entry pointing at the folder), then
reference packages normally:

```xml
<ItemGroup>
  <PackageReference Include="Chuvadi.Pdf.Documents" Version="3.16.0" />
  <PackageReference Include="Chuvadi.Pdf.Text"      Version="3.16.0" />
</ItemGroup>
```

Or install the `Chuvadi.Pdf` meta-package to pull in the whole cross-platform
stack at once.

### Option B — Project reference

If your project lives alongside Chuvadi (e.g. you cloned it as a sibling):

```xml
<ItemGroup>
  <ProjectReference Include="..\chuvadi\src\Chuvadi.Pdf.Documents\Chuvadi.Pdf.Documents.csproj" />
  <ProjectReference Include="..\chuvadi\src\Chuvadi.Pdf.Text\Chuvadi.Pdf.Text.csproj" />
</ItemGroup>
```

Reference only the modules you actually use. Their dependencies are pulled
in transitively, but the explicit references document your contract.

### Option C — Compile to DLLs and reference them

```bash
cd chuvadi
dotnet publish src/Chuvadi.Pdf.Documents -c Release -o /path/to/your-libs
dotnet publish src/Chuvadi.Pdf.Text      -c Release -o /path/to/your-libs
```

Then reference the resulting `.dll` files from your project.

Public nuget.org publishing is a future step — see [DISTRIBUTION.md](DISTRIBUTION.md)
and [BACKLOG.md](BACKLOG.md).

---

## Module dependency map

Bottom-up only. If you reference a module, everything below it comes too.

```
                Documents
              /     |      \
       Content    Fonts    IO
          |        |       |
       Filters    Primitives
          |        |
       Primitives Objects
                    |
                Primitives

Higher-level (depend on Documents + others):
  Text, Text.Shaping, Operations, Rendering (Walking/DisplayList/Raster),
  Svg, Forms, Annotations, Watermark, Redaction, Authoring, Xfa,
  Signatures, PdfA, Reader, Graphics, Color, Images, Fonts.Rendering
```

Strict bottom-up direction is invariant [B02](BASELINE.md). No circular
references anywhere in the library.

---

## Troubleshooting

**"PdfReaderException: Invalid PDF header"** — the file isn't a PDF, or it's
truncated. Check the first 4 bytes are `%PDF`.

**"Cannot decode font encoding"** — the PDF uses a font with a non-standard
encoding that Chuvadi's font subsystem doesn't yet handle. Try
`chuvadi validate-fonts <file>` to see which font is the culprit. Open
a backlog entry if it's a common encoding worth supporting.

**Text extraction produces garbled output** — the document is likely using
custom font encodings that need a ToUnicode CMap. Run
`chuvadi extract-text --strategy operator` for the rawest output and
`chuvadi parse-content <file> --page 0` to inspect the content stream.

**Rendered image looks wrong** — Chuvadi's rasterizer handles the common
operators but not Form XObjects with full transparency groups. If your
input has a complex appearance stream, raster fidelity may be lower than
PDFium-class renderers. Filing an issue with a sample helps.

**`Redactor.Apply` succeeds but the text is still visible in my viewer** —
viewer cache. The output PDF has the bytes removed; close and reopen the
file to confirm.

**Slow first build** — first build resolves ~20 csproj files and compiles
~75,000 lines. Expect 60–120 seconds. Incremental builds are seconds.

---

## Where to read next

- **API patterns and architectural invariants:** [BASELINE.md](BASELINE.md)
  (16 invariants the library never breaks without an explicit decision log entry)
- **Decision history:** [CHANGE-LOG.md](CHANGE-LOG.md)
- **What's planned for 1.1+:** [BACKLOG.md](BACKLOG.md)
- **Auto-generated API reference:** [api/](api/) (one Markdown file per public type)
- **Runnable examples:** [`examples/`](../examples/README.md)
