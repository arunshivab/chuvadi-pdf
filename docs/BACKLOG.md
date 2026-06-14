# BACKLOG.md — Roadmap

> Tracks capabilities not yet shipped. Items move to CHANGE-LOG.md when work
> begins (creating a new A-entry for the decision to take it on).
> **Reconciled against the actual code at v3.2.0** — several items previously
> marked "Not started" were in fact shipped (pattern redaction, non-text image
> redaction, optional content, linearization). The duplicate N.5-N.8 numbering
> from earlier revisions has been replaced with the single 1-13 scheme below.

---

## Shipped (kept for traceability)

Phase 1.1/1.2 roadmap:
- **Annotations (read + create)** - `Chuvadi.Pdf.Annotations`.
- **Digital signatures** (create, timestamp, LTV, verify) - `Chuvadi.Pdf.Signatures` (+ `Chuvadi.Cryptography`).
- **Encryption (read + write)** - `Chuvadi.Pdf.Encryption`.
- **CMYK render output**; **TIFF encoder/decoder** - `Chuvadi.Pdf.Images`.
- **Vector page creation** - `Chuvadi.Pdf.Authoring` (`PdfDocumentBuilder` / `PageBuilder`).
- **Image to PDF conversion** (v2.7.0) - `ImagePdfConverter`.
- **Report layout** (v2.7.0) - `ReportBuilder`.
- **TrueType bytecode hinting** (v2.2-v2.6); **geometric autohinter** Y-fitting (v2.7.0).

Reconciled as shipped (were wrongly listed open):
- **Pattern-based redaction** - `RedactionOptions.Patterns`, `PatternMatcher`,
  `CommonPatterns` (SSN, phone, email, ICD-10); tested. Regex matches resolve to
  device-space rectangles via extracted-text positions.
- **Non-text redaction - image & form XObjects** - `Do` paints whose CTM-mapped
  unit square intersects a redaction rect are dropped. (Inline images and nested
  form recursion remain - see open item 1.)
- **Optional content (layers)** - read (`OptionalContentReader`) + toggle
  (`OptionalContentWriter.SetVisibility`), v3.1.0.
- **Linearization (Fast Web View)** - `LinearizedWriter.Write` wired via
  `PdfWriter.WriteLinearized`; `LinearizationReader`; tested.
- **One-call render facade** (v3.0.0); **streaming page enumeration**,
  **parallel redaction**, **rasterizer benchmark** (v3.1.0).
- **Custom TrueType font embedding in authoring** (v3.2.0) - `AddTrueTypeFont`,
  Type0/CIDFontType2, Indic-capable, logical order.
- **Copy-with-format / per-run text style** (v3.5.0) - `TextRun` carries
  `FontFamily`/`FontWeight`/`Slant`/`FontSize` via a shared `FontStyleClassifier`
  (name + descriptor); SVG styling uses the same classifier.
- **Glyph subsetting for embedded fonts** (v3.4.0) - `TrueTypeSubsetter`; embeds
  only used glyphs and drops non-rendering tables (GSUB/GPOS/cmap/post). ~98%
  smaller FontFile2; numbering preserved so the Identity CID-to-GID map holds.
- **ImageMask stencil compositing** - `/ImageMask true` images composite the
  stencil with the current fill colour via proper source-over alpha rather than
  copying pixels: raster `BuildStencilFrame` + `PageRasterizer.CompositeImage`
  (v2.8.0); SVG `EmitXObject` + `ImageEncoder.BuildStencil` emit an RGBA data
  URL. `/Decode` inversion honoured. Tested (`ImageMaskStencilTests`,
  `RasterRawImageTests`).

---

## Open roadmap

Items 1-11 are independent and may be re-ordered. Status verified against code.

### Redaction
**1. Nested-form redaction (recursion).** Recurse into form XObjects'
content streams so a partially-intersecting form is redacted internally rather
than dropped wholesale. (Inline-image `BI/ID/EI` redaction shipped in v3.3.0;
top-level `Do` image/form paints already drop on intersection.)

### Fonts (authoring & rendering)
**2. Complex-script (Indic) shaping.** GSUB ligatures/conjuncts, GPOS mark
positioning, and Indic reordering so authored Tamil/Devanagari words are shaped
correctly. The embedded fonts already carry the tables; this is the engine that
uses them. *Large - HarfBuzz-class, its own effort.*

**3. Autohinter follow-ups.** Composite-glyph Y-fitting in unhinted fonts;
optional X-axis stem fitting for mono/low-DPI.

### Image codecs (rendering)
**4. JBIG2Decode.** Scanned bilevel images render blank. *Large (arithmetic
coding, symbol dictionaries, generic regions).*

**5. JPXDecode (JPEG 2000).** Renders blank. *Very large (wavelets, EBCOT).*

**6. ImageMask stencil compositing.** SHIPPED - see "Reconciled as shipped"
above (raster v2.8.0 `BuildStencilFrame` + `CompositeImage`; SVG `EmitXObject` +
`ImageEncoder.BuildStencil`). Slot retained to avoid renumbering 7-11.

### Compression / output
**7. Dynamic-Huffman DEFLATE.** The deflater uses fixed Huffman (~85-90% of zlib
ratios); dynamic Huffman closes the gap.

**8. Object streams + xref streams (writer).** `PdfWriter` emits classic xref
tables; object/xref streams shrink files and let `PdfCompressor` pack non-stream
objects.

### Accessibility / archival
**9. Tagged PDF / PDF-A.** Generate `/StructTreeRoot` on page creation;
PDF/UA-1 first, then PDF/A. *Large.*

### Performance
**10. Automated benchmark regression diffing.** The BenchmarkDotNet suite (Brotli,
parser-open, rasterizer) exists; per-release baseline capture + auto-compare in
CI remain.

### Rendering fidelity (small - one tidy PR)
**11. SVG sink follow-ups.** SVG sink ignores `cs`/`scn` colour operators (raster
implements them) and does not recurse into form XObjects; raster quote operators
(`'`/`"`) bypass composite-font routing.

---

## Pre-1.0 housekeeping (separate track)
Publish to nuget.org, reserve the `Chuvadi` package prefix, add a real
`icon.png`, drop the CS1591 suppression once all public XML docs are complete.

---

## Triage rules
1. Items move to a CHANGE-LOG A-entry when work begins.
2. Open items 1-11 are independent and may be re-ordered.
3. **Verify status against the code before starting** - this file has drifted
   before; a quick grep prevents re-building shipped features.
