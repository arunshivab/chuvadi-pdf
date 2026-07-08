# Chuvadi (சுவடி)

> A zero-dependency, audit-safe PDF library for .NET.

**Chuvadi** (Tamil: சுவடி, "palm-leaf manuscript") is a general-purpose
PDF library written entirely in C#, with **zero NuGet dependencies in
production code**. Every byte read, every pixel rendered, every redacted
string removed — owned by this repository, auditable line by line.

- **License:** Apache-2.0
- **Target:** .NET 10
- **Version:** 3.16.0

**New to Chuvadi?** Start with the [Getting Started guide](docs/getting-started.md)
— 10 minutes from `git clone` to working code.

---

## Why Chuvadi

The .NET PDF ecosystem has three rough categories:

1. **PdfSharp / iTextSharp 4.x** — mature but unmaintained; security CVEs go unpatched.
2. **iText 7+ / Aspose / PDFsharp 6+** — actively maintained, but AGPL or
   commercial-license. Hospital deployments and air-gapped environments
   either can't accept AGPL terms or can't pay per-seat fees.
3. **SkiaSharp-backed wrappers** — pull in a 30 MB native dependency that
   audit teams can't review byte by byte.

Chuvadi is a permissively-licensed (Apache-2.0), zero-dependency,
fully-managed alternative. Designed from the ground up for environments
where **every line of code in the dependency tree matters**: clinical
informatics, financial document processing, government, defence,
air-gap-deployed kiosks.

It is a general-purpose library, not shaped around any single consumer.
Applications compose only the modules they need; each `src/` module ships as
its own NuGet package, with a `Chuvadi.Pdf` meta-package for the common case.

---

## What's in the box

| Capability                               | Module                            |
|------------------------------------------|-----------------------------------|
| Read PDF 1.4–2.0 (classic, stream, and hybrid xref) | Chuvadi.Pdf.IO         |
| Standard filters (incl. CCITT Group 3/4, LZW, Flate) | Chuvadi.Pdf.Filters   |
| Text extraction (operator / layout / glyph)         | Chuvadi.Pdf.Text       |
| Complex-script shaping (OpenType GSUB/GPOS)         | Chuvadi.Pdf.Text.Shaping |
| Page rasterisation → PNG/BMP/TIFF/JPEG   | Chuvadi.Pdf.Rendering             |
| Raster / display-list / walker split     | Chuvadi.Pdf.Rendering.{Raster,DisplayList,Walking} |
| SVG page rendering (selectable text)     | Chuvadi.Pdf.Svg                   |
| TrueType bytecode hinting + autohinter   | Chuvadi.Pdf.Fonts.Rendering       |
| WOFF2 decode (in-house Brotli)           | Chuvadi.Pdf.Fonts.Woff2           |
| Text and image watermarks                | Chuvadi.Pdf.Watermark             |
| **True PHI-safe redaction**              | Chuvadi.Pdf.Redaction             |
| AcroForm read and fill; outlines         | Chuvadi.Pdf.Forms                 |
| XFA form rendering + FormCalc/JS scripting | Chuvadi.Pdf.Xfa                 |
| Merge / split / delete / rotate; compression; imposition | Chuvadi.Pdf.Operations |
| PDF authoring (text, tables, images); report layout; image→PDF | Chuvadi.Pdf.Authoring |
| Image codecs (decode + encode)           | Chuvadi.Pdf.Images                |
| Annotations (read + create)              | Chuvadi.Pdf.Annotations           |
| Digital signatures (verify, sign, timestamp, LTV) | Chuvadi.Pdf.Signatures   |
| Encryption (read + write, RC4/AES-128/AES-256) | Chuvadi.Pdf.Encryption       |
| PDF/A conformance output                 | Chuvadi.Pdf.PdfA                  |
| High-level reader facade                 | Chuvadi.Pdf.Reader                |
| Command-line tool (17 verbs)             | tools/Chuvadi.Pdf.Cli             |

Full module list and dependency graph: see `docs/BASELINE.md`.
Distribution and packaging: see `docs/DISTRIBUTION.md`.
Decision history: see `docs/CHANGE-LOG.md`.

API reference (one Markdown file per public type, auto-generated from XML doc
comments): see [`docs/api/`](docs/api/README.md).

---

## Quick start (library)

Open a document, render a page to any format — one call each:

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Reader;   // one-call rendering

using PdfDocument doc = PdfDocument.Open("input.pdf");

File.WriteAllBytes("page0.png", doc.RenderPageToPng(0));        // PNG at 150 DPI
File.WriteAllBytes("page0.jpg", doc.RenderPageToJpeg(0, 300));  // JPEG at 300 DPI
File.WriteAllBytes("page0.bmp", doc.RenderPageToBmp(0));        // BMP
File.WriteAllBytes("page0.tif", doc.RenderPageToTiff(0));       // TIFF
File.WriteAllText ("page0.svg", doc.RenderPageToSvg(0));        // SVG (selectable text)
File.WriteAllBytes("all.tif",   doc.RenderToTiff());           // every page → one multi-page TIFF
```

Each method also has a `Stream` overload (e.g. `doc.RenderPageToPng(0, outputStream)`)
and a DPI parameter for raster formats. For full control over the pipeline, build a
`PageDisplayList` and feed it to `SvgRenderer` or `PageRasterizer` directly.

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

```csharp
using Chuvadi.Pdf.Redaction;
using Chuvadi.Pdf.Graphics;

RedactionOptions opts = new()
{
    Rectangles =
    {
        new RedactionRect(0, new RectangleF(90, 100, 200, 30)),
    }
};

using FileStream input = File.OpenRead("patient_chart.pdf");
using PdfDocument doc = PdfDocument.Open(input, leaveOpen: false);
using FileStream output = File.Create("patient_chart_redacted.pdf");
Redactor.Apply(output, doc, opts);
```

The redacted text is **byte-by-byte absent** from `patient_chart_redacted.pdf`.
No content-stream operator and no indirect object holds the removed data.
See `docs/BASELINE.md` §B15 for the formal definition.

```csharp
using Chuvadi.Pdf.Authoring;

// A flowing multi-page report: pagination, repeating table headers,
// and page numbers are automatic.
ReportTable table = new();
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

// One call from image to PDF (JPEG, PNG, TIFF, BMP; alpha preserved).
ImagePdfConverter.ConvertFile("scan.png", "scan.pdf");
```

XFA forms render with their embedded scripts run by default, so computed
fields fill in:

```csharp
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Xfa;

using PdfDocument doc = PdfDocument.Open("livecycle-form.pdf");
using FileStream output = File.Create("form-rendered.pdf");

// ScriptMode defaults to Full: initialize/calculate/validate scripts execute.
// Pass new XfaRenderOptions { ScriptMode = XfaScriptMode.None } to opt out.
XfaRenderer.Render(output, doc, XfaRenderOptions.Default);
```

---

## Quick start (CLI)

After `dotnet build`, the `chuvadi` executable is in
`tools/Chuvadi.Pdf.Cli/bin/Debug/net10.0/`.

```bash
chuvadi info patient_chart.pdf
chuvadi watermark in.pdf --output out.pdf --text DRAFT --opacity 0.3
chuvadi redact in.pdf --output out.pdf --rect 0,90,100,200,30
chuvadi extract-text in.pdf --strategy layout
chuvadi render in.pdf --output page0.png --page 0 --dpi 150
chuvadi form-fill in.pdf --output filled.pdf --field name=Jane --field dob=1985-04-12
chuvadi outlines in.pdf
chuvadi merge a.pdf b.pdf --output merged.pdf

# debug verbs
chuvadi tokenize in.pdf --page 0
chuvadi dump-objects in.pdf
chuvadi inspect-xref in.pdf
chuvadi validate-fonts in.pdf
```

Run `chuvadi help` for the full verb surface.

---

## Architectural invariants

The library is structured around a set of invariants that NEVER change without
an explicit CHANGE-LOG entry superseding them. The most consequential:

- **B01** — zero NuGet packages in `src/`.
- **B02** — strict bottom-up dependency direction. No circular references.
- **B15** — redaction is byte-level removal, not visual cover-up.
- **B16** — preload the object graph before iterating for rewrites.

Full list: `docs/BASELINE.md`.

---

## Repository layout

```
chuvadi/
├── src/                       # production code (no NuGet deps), 33 packages
│   ├── Chuvadi.Pdf.Primitives/        # tokens, names, primitive objects
│   ├── Chuvadi.Pdf.Filters/           # Flate, LZW, ASCII*, RunLength, CCITT
│   ├── Chuvadi.Pdf.Objects/           # indirect object store, resolution
│   ├── Chuvadi.Pdf.IO/                # xref (classic/stream/hybrid), reader/writer
│   ├── Chuvadi.Pdf.Documents/         # PdfDocument, pages, catalog, metadata
│   ├── Chuvadi.Pdf.Encryption/        # RC4/AES read + write
│   ├── Chuvadi.Cryptography/          # managed crypto primitives
│   ├── Chuvadi.Pdf.Content/           # content-stream tokenizer/parser
│   ├── Chuvadi.Pdf.Fonts/             # font program inspection
│   ├── Chuvadi.Pdf.Fonts.Rendering/   # glyph outlines + TrueType hinting
│   ├── Chuvadi.Pdf.Fonts.Woff2/       # WOFF2 decode (in-house Brotli)
│   ├── Chuvadi.Pdf.Text/              # text extraction strategies
│   ├── Chuvadi.Pdf.Text.Shaping/      # complex-script shaping (GSUB/GPOS)
│   ├── Chuvadi.Pdf.Graphics/          # geometry, color, path primitives
│   ├── Chuvadi.Pdf.Color/             # color spaces
│   ├── Chuvadi.Pdf.Images/            # image codecs (decode + encode)
│   ├── Chuvadi.Pdf.Rendering.Walking/ # shared content-stream walker
│   ├── Chuvadi.Pdf.Rendering.DisplayList/ # text/search display list
│   ├── Chuvadi.Pdf.Rendering.Raster/  # raster display list
│   ├── Chuvadi.Pdf.Rendering/         # scanline rasterizer → PNG/BMP/TIFF/JPEG
│   ├── Chuvadi.Pdf.Rendering.Wpf/     # WPF surface (Windows-only)
│   ├── Chuvadi.Pdf.Svg/               # SVG page rendering
│   ├── Chuvadi.Pdf.Operations/        # merge/split/rotate, compression, imposition
│   ├── Chuvadi.Pdf.Authoring/         # authoring, report layout, image→PDF
│   ├── Chuvadi.Pdf.Watermark/         # text and image watermarks
│   ├── Chuvadi.Pdf.Redaction/         # PHI-safe byte-level redaction
│   ├── Chuvadi.Pdf.Annotations/       # annotation read + create
│   ├── Chuvadi.Pdf.Forms/             # AcroForm read/fill, outlines
│   ├── Chuvadi.Pdf.Xfa/               # XFA rendering + FormCalc/JS scripting
│   ├── Chuvadi.Pdf.Signatures/        # verify, sign, timestamp, LTV
│   ├── Chuvadi.Pdf.PdfA/              # PDF/A conformance output
│   ├── Chuvadi.Pdf.Reader/            # high-level facade
│   └── Chuvadi.Pdf/                   # meta-package
├── tests/                     # xUnit, FluentAssertions (32 test projects)
├── examples/                  # 11 runnable example projects
├── tools/
│   ├── Chuvadi.Pdf.Cli/       # the `chuvadi` executable
│   ├── check_style.py         # in-repo style checker
│   └── gen_api_docs.py        # API-doc generator
└── docs/
    ├── BASELINE.md            # invariants (B01–B16)
    ├── CHANGE-LOG.md          # decision history (A01–ANN)
    ├── SESSION-STATE.md       # current build state
    ├── BACKLOG.md             # open roadmap
    ├── DISTRIBUTION.md        # packaging and versioning
    ├── getting-started.md     # 10-minute onboarding
    ├── developer-guide.md     # deeper API tour
    └── archive/               # point-in-time planning snapshots
```

---

## Building

```bash
dotnet build Chuvadi.slnx -c Release
dotnet test  Chuvadi.slnx -c Release
```

Requires the .NET 10 SDK. **2,392 tests across 30 test projects, 0 failures.**

---

## Contributing

This is currently a single-author project. PRs welcome once contribution
guidelines are written.

Style rules and pitfall list: `CLAUDE.md` (root). The repository uses a
zero-warnings policy and an in-repo style checker (`tools/check_style.py`).
Run it on every changed file before committing.

---

## Roadmap

See `docs/BACKLOG.md` for the open roadmap. Major recent milestones — XFA form
rendering with FormCalc/JavaScript scripting, TrueType bytecode hinting, digital
signing with LTV, PDF/A output, and complex-script shaping — have all shipped;
the changelog records the release-by-release detail.

---

## License

Apache-2.0. See `LICENSE`.

Chuvadi is and will remain free for all use including commercial.
There is no dual-licensing tier and no premium edition.
