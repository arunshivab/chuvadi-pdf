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
- **ImageMask stencil compositing (raster only)** - `/ImageMask true` images
  composite the stencil with the current fill colour via proper source-over
  alpha rather than copying pixels: raster `BuildStencilFrame` +
  `PageRasterizer.CompositeImage` (v2.8.0); `/Decode` inversion honoured. Tested
  (`RasterRawImageTests`). The **SVG** path does not yet handle `/ImageMask` -
  see open item 6.
- **Image `/SMask` (soft-mask transparency) in SVG** (v3.6.0) - an image with an
  `/SMask` is embedded as an RGBA PNG with the mask applied as alpha
  (`ImageOp.SoftMaskAlpha`, `ImageEncoder` RGBA path), instead of dropping the
  mask and rendering transparent regions as black. `/Decode [1 0]` inversion
  honoured. Tested (`ImageSoftMaskTests`).

---

## Open roadmap

Items 1-18 are independent and may be re-ordered. Status verified against code.

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

**6. ImageMask stencil compositing in the SVG path.** Raster handles
`/ImageMask true` (v2.8.0, see Shipped); the SVG renderer does not - such a
1-bit stencil image is currently skipped. Apply the stencil with the current
fill colour as an RGBA `<image>` (the same RGBA-PNG machinery added for `/SMask`
in v3.6.0 can be reused). Note: `/SMask` (soft-mask alpha) is already handled in
SVG and is a separate feature from `/ImageMask` (a 1-bit stencil).

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

### Content rendering (conformance audit — see `CONFORMANCE-AUDIT.md`, 2026-06-15)
Phase-A code survey found these rendering-pipeline gaps, none previously tracked.
Ordered by real-world impact; each cites evidence in the audit doc.

**12. Shadings / gradients (`sh`).** `sh` is currently a recognised no-op
(`ContentStreamWalker.cs:403`); no ShadingType 1-7 exists, so gradient fills and
backgrounds render blank. Start with axial (type 2) and radial (type 3) — the
common cases — then function-based (1) and mesh (4-7). *High impact: designed
PDFs and gradient logos.*

**13. Graphics-state transparency (`gs`).** `gs` is a no-op
(`ContentStreamWalker.cs:402`), so constant alpha (`ca`/`CA`), blend modes
(`BM`), ExtGState soft masks (`SMask`), and transparency groups are all ignored;
everything paints fully opaque, Normal blend. (Image `/SMask` is separate and
already handled.) *High impact.*

**14. Non-device colour spaces.** `scn` interprets operands by count, so
Separation/DeviceN paint with the wrong colour (tint transform ignored) and
Indexed uses the index as a colour; images in those spaces, plus Lab, raw
DeviceCMYK samples, and ICCBased N=4, are not rendered
(`Raster/DisplayListBuilder.cs:294,314,1772`). Add tint-transform evaluation
(Separation/DeviceN), Indexed lookup, Lab→RGB, and the missing image cases.
*High/medium impact: print & design PDFs.*

**15. Patterns.** Tiling (PatternType 1) and shading (PatternType 2) patterns are
not painted — `scn` with a pattern name is suppressed. Needs pattern-cell replay
and shading-pattern fill. *Medium impact.*

**16. Annotation appearance rendering.** The render pipeline does not draw page
`/Annots` `/AP /N` appearance streams, so form fields, widgets, stamps, and ink
annotations are invisible in rendered output even though they are readable.
*Medium/high impact.*

**17. XFA form rendering.** The big one. `PdfDocument.IsXfa` flags these (v3.6.0)
but XFA content lives outside page content, so they render blank. Needs a
template/layout/scripting engine. *Very large — its own multi-stage effort.*

**18. Type3 fonts.** No `CharProcs`/`d0`/`d1` handling; Type3 text (glyphs defined
as content streams) does not render. *Medium impact.*

### Input robustness / recovery
**19. General xref-offset recovery on load.** When a classic xref entry points at
the wrong byte offset (e.g. a stale offset left by an older writer, or a
duplicate object number whose xref entry references the wrong copy), the affected
object resolves to the wrong primitive. The narrow case — a page-tree `/Kids`
entry that resolves to a non-dictionary — is recovered in-walk and surfaced via
`PdfDocument.Warnings`/`IsRecovered` (shipped alongside this item's creation;
reuses `PdfRepairer`'s definition scan, prefers the `/Page` definition). The
broader, deferred work: validate **any** resolved object against its xref offset
and fall back to a full-file definition scan when the offset is provably wrong,
not just for page-tree kids. Lower-risk than auto-`PdfRepairer` on every load;
should be opt-in or confined to objects that fail a type/role check, so healthy
files keep the fast strict path. Origin: `MRDDFF.pdf`, a 9-page CV watermarked by
the old v3.6 stamper, carried a duplicate object 3 (page vs. watermark stream)
with the xref pointing at the stream; the current writer does not reproduce this.

---

## Pre-1.0 housekeeping (separate track)
Publish to nuget.org, reserve the `Chuvadi` package prefix, add a real
`icon.png`, drop the CS1591 suppression once all public XML docs are complete.

---

## Triage rules
1. Items move to a CHANGE-LOG A-entry when work begins.
2. Open items 1-18 are independent and may be re-ordered.
3. **Verify status against the code before starting** - this file has drifted
   before; a quick grep prevents re-building shipped features.
