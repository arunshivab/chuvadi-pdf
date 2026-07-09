# Changelog

All notable changes to Chuvadi will be documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This file records release-by-release notes. Architectural decisions and
rationale live in `docs/CHANGE-LOG.md` (an append-only decision log,
numbered A01..ANN).

---
## [3.17.1] - 2026-07-09

### Fixed
- **Redaction rectangles no longer capture neighbouring lines they never
  touched.** The hit test inflated every glyph's box by 0.25 em + 1.5 pt below
  its baseline, so a rectangle drawn around one word — with its top edge in
  the blank gap under the line above — "intersected" that line's phantom
  descent and silently deleted words the user never covered (redacting a body
  word ate part of the section heading above it). Hit testing now uses tight
  per-glyph ink bounds: descender glyphs (g j p q y, low punctuation,
  brackets) keep a deep bottom, x-height-only lowercase a shorter top, and
  everything else spans baseline-guard to ascender height. Removal of
  anything genuinely hit is unchanged (still over-redacting, B15): a
  rectangle covering only a descender tail still removes those glyphs, and
  the generous box is still used for the drawn overlay. A rectangle placed
  entirely in inter-line whitespace now removes nothing.

### Tests
- 4 new tests (2,417 total): gap-box leaves the line above intact,
  baseline-crossing box still redacts it, descender-tail-only box still hits,
  pure-whitespace box removes nothing.

---
## [3.17.0] - 2026-07-08

Hybrid XFA documents "just work": a consumer opens a PDF through the ordinary
render / extract / flatten APIs and gets the finished document — no
XFA-awareness required. Driven by the Government-of-India MCA Certificate of
Incorporation class of files (hybrid XFA, signed, incrementally updated).

### Added
- **Annotation appearance rendering (PDF 32000-1:2008 §12.5.5).** All render
  sinks — raster, SVG, and the text display list — now draw each visible
  annotation's normal appearance (`/AP /N`, honouring the `/AS` state
  selector) placed onto its `/Rect` by the §12.5.5 algorithm. Form-field
  widgets, stamps, and ink annotations paint the way an interactive viewer
  paints them; hybrid XFA/AcroForm files show their filled field values.
  New public API in `Chuvadi.Pdf.Documents`: `PageAnnotationAppearances.Collect`
  and `AnnotationAppearance` — the single shared resolver consumed by
  rendering, extraction, and flattening, so an annotation paints, extracts,
  and flattens at exactly the same place.
- **Text extraction includes annotation appearance text.** `TextExtractor`
  (`ExtractText` and `ExtractFragments`) appends fragments from each visible
  appearance stream, transformed into page space, so field values are
  searchable and copyable. On the reference certificate, extraction grows from
  the bare template labels to the full document including company name, CIN,
  PAN, TAN, and signatory.
- **`XfaDataField.Geometry` populated via the template bind map.** The XFA
  template's `<field name="…"><bind ref="$record.…"/>` pairs are parsed and
  correlated with the AcroForm widget rects, so dataset nodes whose names
  differ from their widget names (COMPANY_NAME vs CompanyName[0]) still
  resolve a page index and rectangle — enabling search-hit highlighting and
  per-field redaction.
- **`XrefTable.ContainsAny`** — presence check for an entry of any kind
  (in-use, compressed, or free), used by the chain merge below.
- **`PdfString.DecodeLiteralToken` / `PdfString.DecodeHexToken`** — the single
  authoritative §7.3.4 string-token decoders (full escape set: octal `\ddd`,
  `\b`, `\f`, line continuations, unescaped end-of-line normalization).

### Fixed
- **Cross-reference chain precedence (§7.5.6).** The entry for an object in
  the most recent incremental-update section now supersedes all earlier ones
  regardless of kind: a free entry in a newer section marks a deleted object
  and shadows older definitions (no resurrection), and a compressed (Type 2)
  entry is no longer replaced by an older uncompressed one. In a hybrid
  section, `/XRefStm` entries merge before the classic table's, per §7.5.8.4.
  Previously, signed and incrementally-filled files (e.g. government
  certificates) resolved their pre-fill page tree — widgets without `/V` or
  `/AP` — so values were invisible on every path.
- **Literal-string escape decoding unified.** The content-stream parser
  passed string bytes through unescaped (octal `\050` surfaced verbatim in
  extracted text), and the object parser lacked octal/`\b`/`\f`/continuation
  handling. Both now delegate to `PdfString`, as does the content-stream
  walker.
- **Layout extraction no longer double-spaces.** A positional word gap after a
  fragment that already ends in a space (Adobe appearance generators end each
  word-run with one) inserted a second separator, breaking exact-substring
  search over extracted text.
- **Standard 14 font metrics are now exact.** `Standard14Widths` previously
  returned a single per-font *average* for every non-space character (a
  documented v2.0.0 stopgap), so unembedded Times/Helvetica text — the norm in
  LiveCycle/government documents — rendered with jumbled word spacing, lines
  overflowing the page edge, and a distorted type impression. The class now
  carries the complete Adobe Core 14 AFM tables (codegenned from the canonical
  URW base-35 AFM files, WinAnsi-indexed via the Adobe Glyph List);
  `Standard14GlyphWidths` delegates to the same data, so the raster,
  display-list, and redaction width paths can never disagree. Rendering the
  reference certificate now matches a poppler reference at 97.8% binary-ink
  agreement with every text line's extents within ±2 px at 110 dpi.
- **`AnnotationFlattener` content loss (BASELINE B16).** The flattener
  iterated the lazy object store without a preload, silently dropping every
  not-yet-resolved object — a 536 KB certificate flattened to a 6.7 KB empty
  page. The worker now force-resolves the object graph first.
- **Flatten produces a truly static PDF.** Widgets selected for flattening
  that have no usable normal appearance are dropped (industry-standard
  semantics) instead of kept live, so the AcroForm — and the `/XFA` entry
  inside it — is removed: flattening a hybrid XFA document yields
  `IsXfa == false` with all values baked into the page content.

### Tests
- 21 new tests (2,413 total): xref precedence (update supersedes, free
  shadows), appearance collection and §12.5.5 placement, geometry binding,
  extraction (values, octal escapes, spacing), SVG and raster appearance
  output, flatten content preservation and XFA removal, and an end-to-end
  suite over a sanitised MCA certificate fixture.
- New fixtures: `hybrid_xfa_widget.pdf` (minimal hybrid XFA with a widget
  appearance), `incremental_object_update.pdf` and
  `incremental_free_shadowing.pdf` (hand-built xref chains).

---
## [3.16.0] - 2026-07-07

### Added
- **XFA form scripting — FormCalc and JavaScript engines.** Completes the XFA
  renderer arc: embedded form scripts now execute during rendering, so real
  LiveCycle forms fill their computed fields instead of rendering blank. New
  public types in `Chuvadi.Pdf.Xfa` (`Scripting` namespace): `XfaScriptHost`
  (SOM reference resolution and property get/set), `XfaScriptValue` (a dynamic
  value type shared by both engines), `XfaJavaScriptEngine` (a from-scratch
  lexer, parser, and tree-walking interpreter for the JavaScript subset XFA
  forms use), `XfaFormCalcEngine` (a full FormCalc interpreter: `&` concat,
  `if/then/endif`, `for/upto/endfor`, `while/endwhile`, and the common builtins
  Concat/Left/Right/Len/Substr/Upper/Lower/Sum/Avg/Min/Max/Round/Abs/At/Replace/
  Stuff/Space), `XfaScriptRunner` (fires initialize/calculate/validate across
  the template tree, failing soft per script), `XfaScript`, `XfaScriptException`,
  and `XfaValidationResult`. Interactive events (click/change/etc.) are parsed
  and attached but dormant — a static render has no event source. Unsupported
  script constructs fail soft, leaving form state untouched, so one bad script
  never aborts a render. (#171)

### Changed
- **`XfaRenderOptions.ScriptMode` now defaults to `Full`.** Rendering an XFA
  document via `XfaRenderOptions.Default` runs its initialize, calculate, and
  validate scripts, so scripted fields fill without the caller opting in. Pass
  `new XfaRenderOptions { ScriptMode = XfaScriptMode.None }` to restore the
  previous no-script behaviour. This is a behavioural change to the default, not
  an API break — no signatures changed. (#172)
- **Rendering split into three single-namespace projects.** The former
  `Chuvadi.Pdf.Rendering.DisplayList` project housed three namespaces; it is now
  three projects, one namespace each, with acyclic layering
  Walking → DisplayList → Raster: `Chuvadi.Pdf.Rendering.Walking` (the shared
  content-stream walker, a leaf depending only on Filters/Objects/Primitives and
  exposing its internals to the two builders via `InternalsVisibleTo`),
  `Chuvadi.Pdf.Rendering.DisplayList` (the text/search display list), and
  `Chuvadi.Pdf.Rendering.Raster` (the raster display list for the scanline
  rasterizer). Behaviour is unchanged; consumers of the raster or walker types
  reference the new packages, which are pulled in transitively by the meta
  package and `Chuvadi.Pdf.Rendering`. (#174)

### Fixed
- **Compressed objects in hybrid-reference PDFs now resolve
  (`HasStructTree`/`IsTagged` work on Word/Office output).** A hybrid-reference
  file (PDF 32000-1:2008 §7.5.8.4) has a classic `xref` table whose trailer also
  carries `/XRefStm`, pointing to a cross-reference stream that lists the
  compressed (Type 2) objects the classic table cannot represent — for many
  Office writers, that includes `/StructTreeRoot` and `/MarkInfo`.
  `PdfReader.LoadXrefChain` followed `/Prev` but ignored `/XRefStm`, so those
  objects were invisible and `HasStructTree`, `IsTagged`, and `StructTreeRoot`
  wrongly reported absent/null. The classic-xref branch now also reads
  `/XRefStm`, merging its entries without overriding the classic ones (classic
  wins, per the spec). (#173)

---
## [3.14.0] - 2026-06-19

### Added
- Glyph-level text redaction: a redaction overlapping part of a Tj run now
  removes only the matched glyphs and keeps neighbours in their exact
  positions, instead of dropping the whole operator. (#118)
- In-place redaction replacement: `RedactionRect.ReplacementText` draws a
  replacement in the removed span (no box over it); a replacement wider than
  the span is rejected with `RedactionException`. (#118)
- `Standard14GlyphWidths`: accurate per-glyph Standard-14 widths promoted to a
  public type in `Chuvadi.Pdf.Fonts.Rendering`. (#118)
- Checksum-validated redaction patterns: `PatternRule.Validator` plus
  `PatternValidators` (Luhn, Verhoeff, IBAN mod-97, ABA routing, NPI), new
  `CommonPatterns` (India PAN/Aadhaar, IBAN, ABA, SWIFT, EIN, ITIN, NPI, IPv4),
  a `LabeledValue` matcher, and ready-to-use `PatternSets`
  (Financial/Medical/GeneralPii). (#119)
- Redaction R1: physical removal of in-region annotations and their link URLs,
  form-field values, and vector graphics. (#117)
- PageStamper lifecycle: scan-and-continue stamp naming (fixes silent data loss
  on re-stamp), plus `RemoveStamp` and `ReplaceStamp`. (#116)
- Rendering Phase 2: PDF function and shading evaluators (axial/radial, the `sh`
  operator) in the SVG sink, and form XObject rendering. New public
  `PdfFunction`, `PdfShading`, `ShadingOp`, `ShadingStop`. (#115)

### Fixed
- SVG/raster rendering: un-premultiply image SMask `/Matte`. (#115)

---
## [3.9.0] - 2026-06-15

### Fixed
- **Watermarking preserved document integrity.** `WatermarkStamper` (text and
  image) now force-loads the full object graph before numbering, so freshly
  opened documents no longer collide watermark object numbers with existing
  objects, and catalog-only objects (metadata, outlines, names, attachments,
  struct tree) are carried into the output instead of being dropped. The trailer
  `/Info` dictionary is also preserved, so Title/Author/Subject/Keywords and
  dates survive a watermark pass.

### Added
- **Compression safety guard.** `PdfCompressor.Compress` now skips digitally
  signed and encrypted documents by default rather than silently invalidating a
  signature or emitting decrypted content. It writes nothing and reports
  `CompressionResult.SkipReason` (`CompressionSkipReason.Signed` / `Encrypted`),
  so a batch run over a mixed corpus continues instead of throwing. Opt in per
  hazard with `CompressionOptions.AllowSignedRewrite` /
  `AllowEncryptedRewrite`.
- **Compression benchmark and ratio/quality baseline.** A new
  `Chuvadi.Benchmarks.Compression` support library provides a deterministic
  synthetic corpus, ratio measurement, and a global-SSIM quality metric for the
  lossy image path. A committed `compression-baseline.json` plus a CI gate test
  (`CompressionRatioBaselineTests`) fail the build on any ratio or quality
  regression. The BenchmarkDotNet suite gains a `CompressionBench` timing
  scenario, and the runner gains `--compression-report` and
  `--update-compression-baseline` for inspecting and regenerating the baseline.

## [3.8.0] - 2026-06-15

### Added
- **Page composition.** High-level operations for placing existing pages under
  arbitrary affine transforms, all keeping vector and text content intact and
  selectable:
  - `PageComposer` — build a new document by placing pages onto target sheets
    (standard or custom size): rotate by any angle, resize between paper sizes,
    and N-up / imposition (`AddPage`, `AddPageMatching`, `PlacePage`, `Write`).
  - `PageStamper` — overlay or underlay a source page onto one, several, or all
    pages of an existing document (`Place`, `PlaceOnAll`), preserving the rest.
  - `Placement` — convenience transforms (`ScaleToFit`, `Center`,
    `RotatedSize`, `RotateIntoBox`, `RotateAboutCenter`).
  - `PdfPage.EffectiveSize` — the page's displayed size, accounting for
    `/Rotate`.
- **Text extraction follows form XObjects.** `TextExtractor` now recurses into
  form XObjects invoked with `Do`, so text on composed or stamped pages is
  extractable — along with any other form-XObject text previously missed.
- **`SvgExportOptions.Background`** (`ColorF?`, default white). SVG export now
  emits an opaque full-page background rectangle, matching the rasteriser's
  default paper; set to null for a transparent SVG.

### Fixed
- Page import forces a full object-graph load before numbering, fixing a latent
  lazy-loading bug where newly added object numbers could collide with the
  catalog (or drop unloaded objects) on large documents.
- **TIFF output now decodes in Windows/WIC viewers.** PackBits is packed per
  scanline across multiple ~8 KB strips, with explicit Orientation and
  PlanarConfiguration tags. Whole-image single-strip packing previously shifted
  rows and left a black band in Photos, Photo Viewer, WPS, and Paint, even
  though libtiff-family decoders tolerated it.
- **SVG pages render correctly aligned in every engine.** The root width/height
  now carry an explicit `pt` unit; emitting them unitless let some viewers treat
  the point-sized values as points and rescale the canvas by 96/72 while leaving
  the content at 1 unit = 1px, pushing the page off-centre.

## [3.7.0] - 2026-06-15

### Fixed
- **Cross-reference entries are now exactly 20 bytes (ISO 32000-1 §7.5.4).**
  Each `xref` entry was written with a stray space before its CRLF
  (`nnnnnnnnnn ggggg n \r\n`), making it 21 bytes. Lenient readers (qpdf,
  pikepdf, and Chuvadi's own reader) scan past the misalignment, but Adobe
  Acrobat rejects the table, rebuilds it on open, and marks the file modified —
  so it prompts "save changes?" on close even after the file was only viewed.
  The space is removed; entries are now 20 bytes. Affects every written PDF
  (merge, extract, split, authoring, watermarking).

### Added
- **Document metadata on every written PDF.** Output now carries an `/Info`
  dictionary (`/Producer`, `/Creator`) and an XMP `/Metadata` stream on the
  catalog. Both are deterministic — a fixed producer plus identifiers derived
  from the file id, with no timestamps — so identical input still yields
  byte-identical output, and any caller-supplied `/Info` or `/Metadata` is
  preserved.

## [3.6.0] - 2026-06-14

### Added
- **`PdfDocument.IsXfa`.** True when the document is an XFA form (its
  `/AcroForm` carries an `/XFA` entry). XFA content lives outside standard page
  content, so such documents render essentially blank; consumers can use this
  flag to show a notice instead of reaching into the catalog themselves.

### Fixed
- **SVG rendering now applies image `/SMask` (soft-mask transparency).** An image
  XObject with an `/SMask` was embedded as opaque colour with the mask dropped,
  so transparent regions — whose colour bytes are conventionally black —
  rendered as a solid black box (affecting any transparent logo, watermark, or
  signature image). The renderer now decodes the soft mask and embeds the image
  as an RGBA PNG with the mask applied as alpha (honouring the mask's `/Decode`).

## [3.5.1] - 2026-06-14

### Fixed
- **Merge no longer corrupts pages when inputs reuse object numbers.**
  `PageOperations.Merge` renumbered referenced objects through a table keyed on
  the bare object number. Object numbers are per-document, so two inputs that
  reused the same number for different objects (common with shared
  letterhead/forms) collided: distinct content streams collapsed onto one, and
  pages from the second document came out blank, doubled, or failed with "stream
  ended unexpectedly". The remap is now keyed by `(source document, object
  number)`, so each input keeps its own objects while shared objects within a
  single document still de-duplicate.
- **Every written PDF now carries a trailer `/ID`.** `PdfWriter.Write` emitted
  `/ID` only on the encryption path, so Merge, ExtractPages, and Split produced
  files without one; some viewers (e.g. Adobe) synthesised an ID on open and then
  prompted to save on close even after only viewing. The writer now derives a
  stable, content-based `/ID` (ISO 32000-1 §14.4) for every file; output stays
  deterministic and any caller-supplied `/ID` is preserved.

## [3.5.0] - 2026-06-14

### Added
- **Per-run font style on extracted text (copy-with-format).** `TextRun` now
  carries `FontFamily`, `FontWeight` (CSS-style numeric), `Slant`
  (normal/italic/oblique), and `FontSize`, so callers can reconstruct formatted
  text. A new shared `FontStyleClassifier` derives these from the base font name
  combined with the FontDescriptor `/Flags`, `/ItalicAngle`, and `/StemV`; the
  resolved `FontStyle` is carried on `TextOp` and surfaced on `TextRun`.

### Changed
- **SVG text styling now descriptor-aware.** `SvgRenderer` resolves bold/italic
  through the shared `FontStyleClassifier` (`TextOp.Style`) instead of a
  name-only substring check, so fonts that signal style only through their
  descriptor are now rendered with the correct weight/slant.

## [3.4.0] - 2026-06-14

### Added
- **Glyph subsetting for embedded fonts.** `AddTrueTypeFont` now embeds only the
  glyphs actually drawn (plus their composite components), via the new
  `TrueTypeSubsetter`. Non-rendering tables (`GSUB`/`GPOS`/`GDEF`/`cmap`/`post`)
  are dropped, since a CIDFontType2 with an Identity CID-to-GID map never
  consults them and the layout tables are large in complex-script fonts. Glyph
  numbering is preserved, so the Identity mapping, per-CID widths, and ToUnicode
  are unchanged. A two-font Tamil + Devanagari page dropped from ~309 KB to
  ~7 KB (~98% smaller); CFF/OpenType-CFF fonts are embedded whole.

## [3.3.0] - 2026-06-14

### Added
- **Inline-image redaction.** The redactor now removes inline images
  (`BI … ID <binary> EI`) whose CTM-mapped unit square intersects a redaction
  rectangle, matching the existing behaviour for `Do` image/form XObjects.

### Fixed
- **Inline-image parsing in redaction.** Inline-image binary data was previously
  fed to the content tokenizer as if it were operators, which could corrupt the
  rewrite of any content stream containing an inline image. The redactor now
  consumes `BI … EI` as a single unit and resumes parsing after `EI`.

### Changed
- **Backlog reconciled against the code.** `docs/BACKLOG.md` was rewritten: items
  wrongly marked "Not started" but in fact shipped (pattern redaction, non-text
  image redaction, optional content, linearization) were moved to Shipped, and
  the duplicate N.5–N.8 numbering was replaced with a single 1–13 open-roadmap
  scheme.

## [3.2.0] - 2026-06-14

### Added
- **Custom TrueType font embedding in authoring.** `PdfDocumentBuilder.AddTrueTypeFont(name, ttfBytes)`
  registers a static TrueType (glyf) font; `PageBuilder.DrawText` then draws text
  in it. The font is embedded as a composite Type0 / CIDFontType2 font with
  Identity-H encoding, a `/W` width array and `/ToUnicode` CMap covering the
  glyphs actually used, and a FontDescriptor whose metrics are read from the
  font's sfnt tables. A font used on multiple pages is embedded once and shared;
  a registered-but-unused font is not embedded. This enables authoring of
  non-Latin scripts (e.g. Tamil, Devanagari) and any custom Latin font.

### Notes
- Text is emitted in logical order **without** complex-script shaping (no
  GSUB/GPOS or reordering), so Latin renders correctly and Indic renders
  correctly for isolated or already-ordered glyphs; conjunct/matra shaping is a
  separate future effort. Variable fonts must be instantiated to a static
  instance first (see `docs/custom-fonts.md`). Glyph subsetting (embedding only
  used glyphs) is a planned follow-up; this version embeds the whole font program.

## [3.1.0] - 2026-06-14

### Added
- **Streaming page enumeration.** `PdfPageCollection.EnumerateStreaming()` walks
  the page tree once and yields pages without retaining them, for
  constant-memory traversal of very large documents (the indexer stays lazy and
  cached for random access).
- **Optional content (layer) toggling.** `OptionalContentWriter.SetVisibility`
  writes a copy of a document with named layers shown or hidden, complementing
  the existing `OptionalContentReader`.
- **Parallel redaction.** Opt-in `RedactionOptions.MaxDegreeOfParallelism` runs
  the per-page redaction interpreter and overlay generation in parallel. Loading
  and final assembly stay sequential, so the output is byte-for-byte identical
  to the sequential path; the default (1) is unchanged single-threaded behaviour.
- **Rasterizer benchmark.** A BenchmarkDotNet rasterize hot-path scenario
  (150/300 DPI) added to `Chuvadi.Benchmarks`.

### Fixed
- Developer guide: corrected the signatures section — signature *creation*,
  timestamping, and LTV are supported (`PdfCounterSigner`,
  `PdfDocumentTimestamper`, `PdfLtvUpdater`), not read-only as previously stated.

## [3.0.0] - 2026-06-14

### Added
- **One-call render facade.** `PdfDocument` extension methods in
  `Chuvadi.Pdf.Reader` render a page to any format in a single call:
  `RenderPageToSvg`, `RenderPageToPng`, `RenderPageToJpeg`, `RenderPageToBmp`,
  `RenderPageToTiff` (each with `byte[]` and `Stream` forms and a DPI parameter),
  plus `RenderToTiff()` for an all-pages multi-page TIFF. Open a document, call
  one method, get the result — no manual pipeline assembly.

### Removed
- **BREAKING: `SvgExporter` removed.** The obsolete content-stream SVG exporter
  (and its internal-only helpers `TextDispatcher`, `ImageDispatcher`,
  `SvgGraphicsState`) has been deleted. It produced incorrect output —
  vertically flipped images and overlapping text — and was superseded by
  `SvgRenderer`, which renders the neutral `PageDisplayList` correctly. Migrate
  to `document.RenderPageToSvg(pageIndex)` (simplest) or
  `new SvgRenderer(options).RenderPage(document, pageIndex)`.

### Notes
This release was driven by feedback from the first application built on the
library: the easy-to-find call was the deprecated, broken one. The fix makes the
obvious path the correct path and removes the broken path entirely.

## [2.8.4] - 2026-06-13

### Added
- **Progressive JPEG (SOF2) decoding.** Embedded `DCTDecode` images encoded as
  progressive JPEG previously failed to decode and rendered blank (e.g. a
  scanned/exported masthead). The decoder now handles progressive scans —
  spectral selection and successive approximation (DC and AC, first and
  refinement passes) — accumulating coefficients across all scans before the
  inverse DCT.
- **4-component JPEG (CMYK and YCCK).** Handles the Adobe APP14 colour
  transform and the Adobe inverted-channel convention, converting to RGB for
  display. Grayscale (1-component) and YCbCr/RGB (3-component) baseline and
  progressive are all supported, with chroma subsampling and restart intervals.

### Fixed
- The decoder was rebuilt around a coefficient-buffer architecture shared by
  baseline and progressive paths; baseline output is unchanged (verified
  against the existing image tests).

### Notes
Verified against a reference decoder across baseline/progressive, 4:4:4/4:2:0,
grayscale, and CMYK fixtures (max per-channel difference within IDCT rounding).
Not supported: 12-bit precision, arithmetic coding, and lossless JPEG.

---

## [2.8.3] - 2026-06-13

### Fixed
- **Embedded subset fonts now render the correct glyphs.** Documents whose
  fonts are embedded and subsetted (e.g. produced by LibreOffice/eLORA) showed
  blank or garbled text. Two causes: the TrueType loader only parsed `(3,1)`
  format-4 cmaps and ignored the `(1,0)` format-0 tables these fonts use; and
  the rasterizer selected glyphs by decoded Unicode rather than by the
  content-stream character code. The raster text path now selects glyphs by
  code (symbol/Mac/Unicode cmaps as the encoding requires, with a subset
  code-as-index fallback), keeping the Unicode path for standard fonts.
- **Embedded Type1 (`FontFile`) fonts now render.** Added a Type1 program
  interpreter (eexec/charstring decryption, the Type1 charstring operators
  including seac and flex, built-in and `/Differences` encodings). Previously
  these glyphs did not render at all.
- **Simple-font advances now come from the font's `/Widths` array** (the
  authoritative source per PDF §9.2.4) when present, falling back to the font
  program's metrics — fixing letter spacing for both TrueType and Type1.

### Added
- TrueType cmap formats 0 and 6, and separate retention of Unicode, symbol,
  and Macintosh cmap subtables.
- Word spacing is applied to single-byte code 32 per §9.3.3.

### Known gap
Embedded JPEG (`DCTDecode`) image XObjects are not yet decoded for display, so
documents whose graphics are baseline-JPEG (e.g. a scanned masthead) show those
images blank. Text renders fully. A JPEG decoder is planned separately.

---

## [2.8.2] - 2026-06-13

### Fixed
- **Non-embedded Standard-14 fonts now render text.** Pages using the
  standard Helvetica/Times/Courier/Symbol/ZapfDingbats fonts without an
  embedded font program previously rasterised with no text (only vector
  graphics appeared). The rasterizer's font resolver returned nothing when a
  font had no embedded program, so glyph emission was skipped. It now falls
  back to the embedded substitute-outline bundle for the 14 standard fonts.

### Changed
- **The Standard-14 outline bundle is now built from real font data.**
  `Standard14.bin` was previously a header-only placeholder. It is now
  generated from the Liberation (SIL OFL) and URW (AGPL-with-font-exception)
  substitute fonts: all 14 fonts, ASCII plus Latin-1, ~191 glyphs each.
  Glyph outlines are normalised to a 1000-unit em on load.
- **The bundle build tool** (`tools/build_standard14_bundle.py`) now converts
  quadratic TrueType curves to cubic via fontTools' Qu2CuPen (the loader
  renders single-segment cubics; raw multi-point `qCurveTo` was unsupported),
  resolves symbol-encoded fonts that lack a Unicode cmap, and covers
  0x20–0xFF.

### Note
Glyph shapes are metric-compatible substitutes (Liberation/URW), not Adobe's
original outlines — standard practice for headless PDF rendering.

---

## [2.8.1] - 2026-06-13

### Fixed
- **Page operations no longer corrupt the source document.** `SplitPages`,
  `Merge`, `ExtractPages`, `DeletePages`, `RotatePages`, and `ReorderPages`
  shared the source document's nested resource dictionaries (e.g.
  `/Resources`) with their output and remapped references in place. This
  mutated the original document — so a second operation on the same
  `PdfDocument` (for example compressing a document that had just been
  split) saw scrambled references and dropped fonts and images — and it
  scrambled references for multi-page documents whose pages share resource
  objects. The page builder now performs a true deep copy and never mutates
  shared state.
- **References nested inside arrays are now remapped.** The array branch of
  the old reference-remapper was a no-op, so indirect references inside
  arrays (such as `/Annots` or an array `/Contents`) kept their original
  object numbers after a page operation. They are now renumbered correctly.

---

## [2.8.0] - 2026-06-12

### Added
- **JPEG encoder** (`Chuvadi.Pdf.Images.JpegEncoder`): baseline JFIF export
  with IJG quality 1–100, completing PDF→image support across PNG, BMP,
  TIFF, and JPEG. Colour encodes as YCbCr 4:4:4; grayscale frames encode
  single-component.
- **CCITTFaxDecode filter** (`Chuvadi.Pdf.Filters.CcittFaxFilter`): Group 3
  one- and two-dimensional and Group 4 fax decoding — scanned-document PDFs
  now render. Honours K, Columns, Rows, BlackIs1, EncodedByteAlign, and
  EndOfBlock; registered with the `CCF` alias.
- **Raw raster images.** The rasterizer now renders raw-sample images
  (FlateDecode RGB/Gray, CCITT bilevel, ICCBased 1/3-component), honouring
  per-filter `/DecodeParms` and `/Decode [1 0]` inversion. These were
  previously dropped silently.
- **PDF compression** (`Chuvadi.Pdf.Operations.PdfCompressor`):
  garbage-collects unreachable objects, Flate-compresses raw streams, and
  optionally re-encodes photographic images as JPEG
  (`CompressionOptions.RecompressImages`). The catalog graph (outlines,
  forms, metadata) is preserved.

### Changed
- **Real DEFLATE compression.** The Flate encoder previously emitted stored
  (uncompressed) blocks; it now performs LZ77 with fixed-Huffman coding.
  PNG exports, authored documents, and compressed streams shrink
  dramatically (typical content streams to 10–20% of raw size).

### Removed
- **`Chuvadi.Pdf.Images.Jpeg` project.** An orphaned duplicate JPEG encoder
  with no consumers or tests; superseded by
  `Chuvadi.Pdf.Images.JpegEncoder`.

---

## [2.7.1] - 2026-06-12

### Fixed
- **Octal string escapes on the raster path.** The rasterizer's content-stream
  string decoder did not handle `\nnn` octal escapes (PDF 32000-1 §7.3.4.2),
  so literal strings written with octal-escaped bytes — including the WinAnsi
  bullets and ellipses Chuvadi's own ReportBuilder emits — rendered the escape
  characters verbatim when rasterized. Octal escapes now decode correctly.
- **Multi-element dash patterns in SVG output.** The SVG display-list parser
  kept only the first dash length and treated later array entries as the
  phase, so patterns like `[3 2] 0 d` rendered with the wrong gap lengths.
  The full dash array is now honoured.

### Changed
- **DisplayList consolidation (internal).** The two content-stream
  interpreters behind the SVG/Reader display list and the raster display list
  now share one walker (tokenisation, operand parsing, operator dispatch) with
  the two builders as event sinks. No public API changed. Malformed numeric
  operands on the SVG path are now read as 0 instead of aborting the page
  build, matching the raster path's tolerance.

---

## [2.7.0] - 2026-06-12

### Added
- **Image → PDF conversion.** New `ImagePdfConverter` turns JPEG, PNG, TIFF,
  and BMP images into PDF documents in one call: page sized to the image at a
  chosen DPI or fitted to a paper size with margins and centring, multi-image
  → multi-page, multi-frame TIFF → one page per frame (optional), and document
  metadata. New `BmpDecoder` (headers 12/40–124; depths 1/4/8/16/24/32;
  BI_RGB, RLE8, RLE4, BI_BITFIELDS; top-down and bottom-up) completes the
  decoder set.
- **Image embedding rework.** `PageBuilder.DrawImage` now accepts JPEG, PNG,
  TIFF, and BMP (plus a decoded `ImageFrame` overload). Alpha channels are
  preserved via PDF soft masks (`/SMask`); grayscale sources embed as
  DeviceGray. Baseline JPEG and 8-bit truecolour PNG still embed without
  recompression; palette / grayscale / alpha / 16-bit PNG, TIFF, and BMP
  decode and re-embed as Flate-compressed samples. Fixes the previous
  RGBA-PNG embedding (4-component data declared as DeviceRGB).
- **Report layout.** New `ReportBuilder`: flowing multi-page composition with
  headings, paragraphs (alignment incl. justification, indents, spacing),
  bulleted and numbered lists (Arabic / Roman / letter numbering), span-aware
  tables (fixed / fractional / auto column widths, col- and row-spans,
  per-cell style overrides, alternating row fills, wrap / truncate / ellipsis
  overflow, images in cells, repeating headers across pages, five border
  modes), images, rules, spacers, page breaks, styled headers/footers with
  `{page}` / `{total}` / `{title}` / `{date}` tokens, and page numbers in
  Arabic, Roman, or letter form.
- **Geometric autohinter fallback.** Fonts that carry no hinting programs are
  now grid-fitted on the Y axis by a geometric autohinter (blue-zone
  anchoring with classic overshoot suppression, horizontal-stroke weight
  fitting, untouched-point interpolation). On by default; opt out with
  `RenderOptions.AutohintUnhintedFonts = false` (plumbed through
  `DisplayListBuilder` and `FontRenderer.GetHintedGlyphOutline`).

### Fixed
- **SSW interprets FUnits.** The Set Single Width instruction now converts
  its argument from font units to pixels via the current scale (matching
  WCVTF and the FreeType v35 reference); previously the raw value was stored.
- **MIRP twilight originals.** When zp1 is the twilight zone, MIRP now seeds
  the point's original (and current) position from rp0 plus the CVT distance
  along the freedom vector — the undocumented MS-rasterizer behaviour the
  conformance reference implements.
- **MIRP cut-in zone gating.** The control-value cut-in test now applies only
  when both zone pointers reference the same zone, per the reference.

### Changed
- **Engine compensation plumbed.** ROUND/NROUND and MDRP/MIRP now key engine
  compensation off the opcode's distance-type bits, applying it in both the
  rounded and unrounded (Round_None) branches. All four compensation values
  default to zero — the conformance reference's behaviour — and are settable
  via `HintingInterpreter.SetEngineCompensation`.
- **Single-width per-instruction forms.** MDRP and MIRP now use their own
  single-width snap forms (window around the single-width value for MDRP;
  CVT-distance proximity for MIRP), replacing the previous shared
  approximation.
- **MPS point-size option.** MPS keeps pushing the ppem by default (FreeType
  v35 classic behaviour); embedders can supply the spec-true point size via
  `HintingInterpreter.MeasuredPointSize`.

---

## [2.6.0] - 2026-06-10

### Added
- **Composite glyph hinting (Light and Full).** Composite glyphs - accented
  letters and other glyphs assembled from components - are now grid-fitted
  instead of falling back to the unhinted outline. Each component is hinted as
  its own glyph, translated by its component offset (rounded to the grid when
  the component sets `ROUND_XY_TO_GRID`), and merged into one point set; the
  composite's four phantom points are appended, and the composite's own
  instruction stream (when `WE_HAVE_INSTRUCTIONS` is set) is then executed
  over the assembled points. Light keeps its unfitted X and grid-fitted Y for
  composites exactly as for simple glyphs.
- **Composite hinting test coverage.** A synthetic in-memory font with a base
  glyph and a composite that references it (grid-rounded offset plus an
  instruction stream) exercises the hinted assembly end to end, including a
  regression lock on the composite-program org-from-current baseline and the
  scaled-component fallback.
### Fixed
- **SHC / SHZ no longer move the reference point a second time.** The shift
  applied by `SHC` (shift contour) and `SHZ` (shift zone) now skips the
  reference point itself, which has already moved. Previously, when the
  reference point's displacement was non-zero, it received that displacement
  twice. The defect was masked for simple glyphs (their programs begin with
  current equal to original positions) and surfaced only with composite
  instruction streams.

### Notes
- **Composite hinting scope.** Components placed by XY offset - the common case,
  including every composite in the standard text fonts tested - are fully
  hinted. Composites that use scaled components, a 2x2 transform, or
  anchor-point (point-matching) placement fall back to the unhinted outline, as
  does nesting deeper than three levels. This matches or exceeds the coverage of
  the existing unhinted composite path, so no glyph renders worse than before.

---

## [2.5.1] - 2026-06-09

### Fixed
- **Full-mode hinting now uses the hinted advance width.** In
  `HintingMode.Full`, the pen advance is taken from the hinted horizontal
  phantom points (pp2 - pp1) rather than the scaled static `hmtx` advance.
  The glyph program grid-fits the advance phantom, so the hinted advance can
  differ from the merely scaled value; using it keeps the advance consistent
  with the grid-fitted ink and removes the extra right-side gap that appeared
  after each glyph. `Light` (the default) and the unhinted path are unchanged -
  they continue to use the scaled `hmtx` advance, since `Light` does not
  grid-fit the horizontal axis

## [2.5.0] - 2026-06-09

### Added
- **TrueType bytecode hinting is now wired into the raster render path
  (Stage 7), and on by default.** The completed interpreter
  (`Chuvadi.Pdf.Fonts.Rendering.Hinting`) is now executed during
  rasterization for embedded TrueType glyphs, replacing the inert flag of
  earlier releases
  - New `RenderOptions.Hinting` mode enum `HintingMode { Off, Light, Full }`.
    **`Light` is the new default** - it grid-fits the vertical (Y) axis only,
    keeping baselines and stem heights crisp without the horizontal stem
    snapping that reads heavy under grayscale anti-aliasing
  - `Full` executes the complete interpreter on both axes (best for
    black-and-white or very low-resolution output); `Off` restores the previous
    unhinted behaviour
  - Glyph hinting runs per device-ppem with a fractional 26.6 outline that the
    painter scales back exactly, so the grid fit lands on real device pixels
  - The example renderer accepts `--hint-light` (default) and `--hint-full`

### Fixed
- **MSIRP (opcodes 0x3A/0x3B) was mislabelled as RTDG and silently dropped its
  two stack operands.** This was the decision deferred in 2.4.4 (A24): the
  working decode table mapped `0x3A` to RTDG, so every MSIRP a glyph program
  issued left two values stranded on the operand stack, corrupting all
  downstream reference points and producing visibly broken glyphs (the capital
  W in the test CV lost half its strokes). MSIRP is now implemented (move a
  point to a stack-supplied distance from rp0, setting rp1/rp2 and, for the
  [1] form, rp0), and **RTDG is moved to its correct opcode 0x3D**
- **`fpgm` now runs before `prep`** when preparing a size, so functions the
  control-value program calls are defined when it executes
- **IP (interpolate points) handles out-of-range points by shifting**, not
  proportional scaling: points whose original position lies outside the
  [rp1, rp2] reference span are shifted by the nearer reference's movement, per
  the TrueType specification, fixing points that previously collapsed inward
- **Glyph outlines are re-cubicized in fractional 26.6**, not rounded to whole
  pixels first, so small glyphs keep their shape instead of degenerating
- Implemented the FLIP opcodes FLIPPT/FLIPRGON/FLIPRGOFF (0x80-0x82) and the
  vector opcodes SPVFS/SFVFS/GPV/GFV/SFVTPV (0x0A-0x0E), which were previously
  silent no-ops that drifted the stack

### Changed
- Default render output now differs from 2.4.x for documents with embedded
  TrueType fonts: glyphs are hinted with `HintingMode.Light`. Pass
  `RenderOptions { Hinting = HintingMode.Off }` to restore the previous output

### Tests
- Corrected the RTDG rounding test to use the proper opcode (0x3D); added an
  MSIRP movement test; the full suite passes (no glyph renders worse than the
  unhinted baseline at the gate)

### Notes
- **Known limitation - `Full` mode over-tightens parallel-stem letters.** On
  letters with two vertical stems (n, u, m, h), `Full` mode pulls the stems too
  close horizontally, narrowing the counter. This is an X-axis
  minimum-distance/stem-width refinement tracked for a follow-up; **`Light`
  (the default) is unaffected** and renders these correctly
- Composite (component) glyphs still fall back to unhinted outlines
- Carries forward the documented MDRP/MIRP simplifications (distance-type
  compensation zero - correct for grayscale, single-width no-op at the default
  cut-in, MPS approximated as ppem) revisited alongside the `Full`-mode stem
  work
- Architecture and rationale: decision log A25

## [2.4.4] - 2026-06-05

### Added
- **TrueType bytecode hinting — Stages 5 and 6 (arithmetic, logic, flow
  control, DELTA, and interpolation)** in
  `Chuvadi.Pdf.Fonts.Rendering.Hinting`. Builds on Stage 4 and fills in the bulk
  of the interpreter's opcode surface; still internal and unwired, so render
  output is unchanged and `RenderOptions.Hinting` remains inert
  - Arithmetic, logical, and stack operators: ADD/SUB/DIV/MUL/ABS/NEG/FLOOR/
    CEILING/MAX/MIN/ROLL, AND/OR/NOT/EQ/NEQ/GT/GTEQ/LT/LTEQ/ODD/EVEN, and
    ROUND[ab]/NROUND[ab]; DIV and MUL operate in 26.6 fixed point
  - Storage-area access (RS/WS)
  - Flow control handled in the execution loop with depth-aware scanning:
    IF/ELSE/EIF and the jumps JMPR/JROT/JROF
  - DELTA exceptions DELTAP1/2/3 (point table bases 0/16/32) and DELTAC1/2/3
    (CVT), decoding each pair's relative-ppem nibble and magnitude selector and
    applying only when the active ppem matches `DeltaBase + tableBase + relppem`
  - Shift and interpolation family, loop-aware via the graphics-state loop
    counter: SHP/SHC/SHZ/SHPIX, IP, ISECT, and full IUP[x]/IUP[y]
    (per-contour interpolation between touched anchors, with single-anchor
    rigid shift), plus ALIGNRP/ALIGNPTS/UTP
  - GETINFO answers conservatively — scaler version and the grayscale-rasterizer
    flag only

### Tests
- 25 tests over synthetic programs (no font file required): each arithmetic,
  logical, and rounding operator; ROLL and storage; the flow-control branches
  and jumps; DELTA at matching and non-matching ppem; and the geometry family
  (SHPIX/IP/IUP interpolate-and-shift/ALIGNRP/ALIGNPTS/UTP/SHC/ISECT/GETINFO),
  with expected values hand-verified in fixed point

### Notes
- Still inert: nothing in the render path calls the interpreter and the
  `Hinting` flag has no effect
- Operand orders that the spec underspecifies are pinned by tests and flagged
  for confirmation against real fonts at Stage 7: DELTA pairs (point/CVT index
  deeper, argument on top), ISECT (b1, b0, a1, a0, point), and JROT/JROF
  (condition then offset)
- Carries forward the Stage 4 simplifications (MDRP/MIRP distance-type
  compensation zero, single-width no-op at default cut-in, MPS as ppem) and
  GETINFO's conservative answer, all revisited at Stage 7
- MSIRP is deferred: its opcode assignment collides with RTDG in the working
  decode table and is resolved alongside the Stage 7 wiring
- Architecture and rationale: decision log A24

## [2.4.3] - 2026-06-04

### Added
- **TrueType bytecode hinting — Stage 4 (points, zones, and movement)** in
  `Chuvadi.Pdf.Fonts.Rendering.Hinting`. Builds on Stage 3; the interpreter is
  internal and unwired, so render output is unchanged and `RenderOptions.Hinting`
  remains inert
  - `PrepareSize(ppem, unitsPerEm, cvt, prep)` computes the 16.16 font-unit to
    26.6 scale, scales the Control Value Table, allocates the twilight zone,
    resets the graphics state, and runs the `prep` program once per size;
    `HintGlyph(RawGlyph)` builds the glyph zone scaled to 26.6, runs the glyph
    program, and returns the fitted zone
  - New internal `Zone` type holding current and original 26.6 coordinates and
    per-axis touch flags for the twilight and glyph zones
  - `F26Dot6.MulFix` — 16.16 scale multiply used to scale coordinates and CVT
    entries
  - Opcodes: measurement (GC/SCFS/MD/MPPEM/MPS), absolute movement (MDAP/MIAP),
    relative movement (MDRP/MIRP with auto-flip, control-value cut-in, rounding,
    and minimum-distance clamp), vector-to-line (SPVTL/SFVTL/SDPVTL), CVT access
    (RCVT/WCVTP/WCVTF), and the supporting state setters
    (SRP/SZP/SLOOP/SMD/SCVTCI/SSWCI/SSW/FLIPON/FLIPOFF/SDB/SDS/SCANCTRL/SCANTYPE/INSTCTRL)

### Tests
- 14 tests over synthetic programs (no font file required): font-unit and CVT
  scaling, CVT read/write, measurement, the four move operators, vector-to-line
  setters, and persistence of `prep` control-value writes into the glyph program

### Notes
- Still inert: nothing in the render path calls the interpreter and the
  `Hinting` flag has no effect
- MDRP/MIRP distance-type compensation is treated as zero (grey rendering),
  single-width handling is a no-op at the default cut-in, and MPS approximates
  point size as ppem; these are revisited at Stage 7
- Interpolation (ISECT/SHP/SHC/SHZ/SHPIX/IP/IUP), DELTA, and the
  arithmetic/logical/storage/flow-control tail remain for Stages 5 and 6
- Architecture and staging rationale: decision log A23

## [2.4.2] - 2026-06-04

### Added
- **TrueType bytecode hinting — Stage 3 (vectors and rounding)** in
  `Chuvadi.Pdf.Fonts.Rendering.Hinting`. Builds on Stage 2; the interpreter is
  internal and unwired, so render output is unchanged and `RenderOptions.Hinting`
  remains inert
  - New internal `F26Dot6` (64 = 1px) with FromPixels/ToPixels/Floor/Ceiling/
    Round/Mul/Div, and `F2Dot14` (16384 = 1.0) with Mul/Dot/ToDouble; multiply
    and divide round half away from zero and guard divide-by-zero
  - Axis-aligned vector setters SVTCA/SPVTCA/SFVTCA, and the round-state
    operators RTG/RTHG/RTDG/RDTG/RUTG/ROFF and SROUND/S45ROUND with spec
    selector decoding
  - `round()` engine via floor-to-multiple, correct for any period including
    S45ROUND's non-power-of-two grid; consumed by MDRP/MIRP in Stage 4
  - `GraphicsState` gains the super-round period/phase/threshold fields,
    defaulted to round-to-grid in `Reset()`

### Tests
- 23 tests over synthetic bytecode: the fixed-point helpers, the round engine
  under every state, SROUND/S45ROUND selector decoding, and the vector setters

### Notes
- Still inert: `round()` and the new opcodes have no render-path consumer and
  the `Hinting` flag has no effect
- The line-based vector setters (SPVTL/SFVTL/SDPVTL) and running `prep` are
  deferred to Stage 4 with the point/CVT infrastructure they consume
- Architecture and rationale: decision log A22

---

## [2.4.1] - 2026-06-04

### Added
- **TrueType bytecode hinting — Stage 2 (VM skeleton)** in
  `Chuvadi.Pdf.Fonts.Rendering.Hinting`. Builds on the Stage 1 foundation; the
  interpreter is internal and unwired, so render output is unchanged and
  `RenderOptions.Hinting` remains inert
  - New internal `HintingInterpreter`: operand stack, storage area, function
    table, and instruction-definition table. Implements the push family
    (NPUSHB/NPUSHW/PUSHB/PUSHW), stack manipulation
    (DUP/POP/CLEAR/SWAP/DEPTH/CINDEX/MINDEX), and function/instruction
    definition and calling (FDEF/ENDF/CALL/LOOPCALL/IDEF), plus
    `RunFontProgram`. The FDEF/IDEF body scanner is instruction-length-aware,
    so inline push data is never misread as `ENDF`; a call-depth guard bounds
    recursion. Opcodes not yet implemented are length-aware no-ops
  - New internal `GraphicsState` carrying the full TrueType graphics state and
    its spec defaults (`Reset()`); most fields are consumed by later stages
  - New internal `HintingLimits` and `RoundState`
  - `TrueTypeLoader.GetHintingLimits()` reads the `maxp` version 1.0 maximums
    that size the interpreter's tables, defaulting on `maxp` version 0.5

### Tests
- 19 tests over synthetic bytecode (no font file required): push family,
  stack manipulation, FDEF/CALL/LOOPCALL/IDEF, length-aware FDEF body
  scanning, the recursion-depth guard, table sizing from limits, and the
  loader's version 0.5 default limits

### Notes
- Still inert: nothing in the render path calls the interpreter and the
  `Hinting` flag has no effect
- Stack values are raw 32-bit integers; F26Dot6/F2Dot14 fixed-point
  interpretation begins in Stage 3 with the vector and rounding operators
- Introduces the repository's first `InternalsVisibleTo` (a standalone
  attribute file) so the internal interpreter can be unit-tested
- Architecture and staging rationale: decision log A21

---

## [2.4.0] - 2026-06-04

### Added
- **TrueType bytecode hinting — Stage 1 (raw-glyph foundation)** in
  `Chuvadi.Pdf.Fonts.Rendering`. Additive groundwork for a spec-complete
  TrueType instruction interpreter; no interpreter yet, and render output is
  unchanged
  - `TrueTypeLoader.ParseOffsetTable` now captures the `cvt `, `fpgm`, and
    `prep` table offsets and lengths, with internal accessors
    `GetControlValueTable`, `GetFontProgram`, and `GetControlValueProgram`
    returning the raw (unparsed) table bytes on demand
  - New internal `BuildRawGlyph(int)` parse path producing an un-cubicized
    point set in font design units — on/off-curve flags, contour ends, the
    glyph's captured instruction bytecode, and four appended phantom points —
    alongside the existing cubic `GetGlyphOutline` path, which stays the
    rendering path. Composite glyphs return null (deferred to a later stage);
    empty glyphs return phantom points only
  - New internal `RawGlyph` model in the new
    `Chuvadi.Pdf.Fonts.Rendering.Hinting` sub-namespace
  - New `RenderOptions.Hinting` flag (default `false`), currently unconsumed;
    it gates the hinted pipeline once later stages wire it in

### Notes
- Purely additive and inert: nothing in the render path calls the new
  members, the 9-page reference CV renders identically, and `Hinting` has no
  effect yet
- No new tests this stage — the foundation is exercised once the VM skeleton
  (Stage 2) calls into it
- Stage-1 simplifications: vertical phantom points (pp3/pp4) are synthesised
  pending `vmtx`/`vhea` parsing; touched-flag arrays for IUP live on the
  interpreter working set rather than the parsed model
- Architecture and staging rationale: decision log A20

---

## [2.1.0] - 2026-05-22

### Added
- **`Chuvadi.Pdf.Reader` module** — a high-level facade over the
  library, designed for interactive PDF reader applications (Blazor
  WebAssembly, WPF, etc.) that want a small, mockable surface area
  instead of wiring the lower-level modules (Documents, Rendering,
  Svg, Text) directly
  - `IPdfReader` interface with six async methods: `OpenAsync`
    (stream + optional password), `RenderPageSvgAsync` (page-sized
    SVG with selectable text layer at native PDF coordinates),
    `RenderThumbnailAsync` (lower-precision SVG for thumbnail strips),
    `GetOutlinesAsync` (bookmark tree), `SearchAsync` (streaming
    `IAsyncEnumerable<SearchMatch>` across pages), and
    `GetTextRunsAsync` (per-page text-run geometry for selection
    layers)
  - `ChuvadiPdfReader` concrete implementation backed by the
    underlying library — `PdfDocument.OpenAsync`,
    `SvgRenderer.RenderPage`, `OutlineReader.GetOutlines`,
    `PdfDocument.SearchAsync`, `PdfDocument.GetTextRuns`. All public
    method parameters are validated with `ArgumentNullException` and
    `ArgumentOutOfRangeException`. Stateless and thread-safe; suitable
    for singleton registration in DI
  - Caches two `SvgRenderer` instances internally — one tuned for
    full-page rendering (`Precision = 4`, web-font embedding,
    selectable text), one for thumbnails (`Precision = 2`, CSS
    fallback fonts, selectable text). Visual sizing is the caller's
    responsibility per design: SVG is resolution-independent and
    browsers scale it losslessly via CSS

### Tests
- New `Chuvadi.Pdf.Reader.Tests` project with tests for every
  `IPdfReader` method covering: null-argument validation,
  out-of-range page indices, cancellation propagation, plain and
  encrypted-PDF open flows, render-output sanity (well-formed SVG),
  outline traversal on a document with no outline, search on a
  document with no text, and text-run extraction on an empty page

### Notes
- The module re-uses existing library types directly: `PdfDocument`,
  `OutlineItem` (in `Chuvadi.Pdf.Forms`), `SearchMatch`/`SearchOptions`/
  `TextRun` (in `Chuvadi.Pdf.Rendering.DisplayList`). Reader-app
  consumers add the corresponding `using` directives rather than
  going through a re-export layer — the types live where they
  logically belong, the facade just exposes them through a single
  small interface
- No breaking changes. Existing API surface is unchanged

---

## [2.0.2] - 2026-05-22

### Fixed
- **`EncryptionOptions` default permission mask** — `EncryptionOptions`
  constructed via the `Aes128` or `Aes256` factories was defaulting
  `Permissions` to `-3904` under the mistaken belief that the value
  meant "allow everything (PDF spec all-bits-on)". In fact `-3904`
  (`0xFFFFF0C0`) has **every** PDF 32000-1 §7.6.3.2 Table 22 permission
  bit CLEAR — print, modify, copy, annotate, fill-forms, accessibility
  extract, assemble, and high-quality print are all denied. The
  correct "allow everything" value is `-4` (`0xFFFFFFFC`): all eight
  permission bits set, both reserved-must-be-1 bits set, and all high
  reserved bits set. Encrypted PDFs written with default
  `EncryptionOptions` in v1.4.0 through v2.0.1 are maximally
  restricted regardless of intent
- Introduced `public const int EncryptionOptions.AllPermissionsAllowed = -4;`
  as a self-documenting reference value for the canonical "allow
  everything" mask, replacing the magic-number literal in the default
  constructor and the misleading doc comment. Callers who want to
  restrict specific permissions can continue to assign directly to
  the `Permissions` init property

### Tests
- New `EncryptionDefaultsRoundTripTests` under
  `tests/Chuvadi.Pdf.Documents.Tests` covering the AES-128 and AES-256
  write → read round-trip with default options, verifying that the
  read-back `PdfDocument.Encryption.Permissions` equals
  `EncryptionOptions.AllPermissionsAllowed` and that every `Allow*`
  decoder reports true. A guard test pins the constant to `-4` so any
  future regression to `-3904` is the loudest possible alarm
- Existing `EncryptionInfoTests.Constructor_PropertiesPropagate`
  updated to use `-4` as its sample permission value for consistency
  with the new constant; behaviour and assertions otherwise unchanged

### Notes
- The reader-side default for an absent `/P` entry in
  `EncryptionDictionary.Parse` (also `-3904`) is **intentionally not
  changed**. PDF spec requires `/P` to be present on encrypted
  documents; if a malformed document omits it, defaulting to all-bits-
  clear (deny by default) is the safer behaviour than auto-granting
  every permission
- The bug only affected the **default** permission value. Callers who
  passed an explicit `Permissions` value via the init property
  (e.g. `EncryptionOptions.Aes256("pw") with { Permissions = ... }`)
  were unaffected

---

## [2.0.1] - 2026-05-22

### Added
- **Document metadata properties** on `PdfDocument` for the date and
  trapping fields declared by PDF 32000-1 §14.3.3 — `CreationDate` and
  `ModDate` returning `DateTimeOffset?` parsed per §7.9.4 (UTC, offset,
  and date-only forms all supported; malformed inputs return null);
  `Trapped` returning `string?` and leniently accepting both `/Name`
  and `/String` forms (some producers write either); `XmpMetadata`
  returning the raw bytes of the Catalog's `/Metadata` stream
- **Encryption introspection** on `PdfDocument` via the new
  `Encryption` property returning `EncryptionInfo?` — null when the
  document is unencrypted, otherwise exposing `Algorithm`, `KeyLength`,
  `Revision`, `Version`, `Permissions`, `EncryptMetadata`, and eight
  permission decoders (`AllowPrint`, `AllowModify`, `AllowCopy`,
  `AllowAnnotate`, `AllowFillForms`, `AllowAccessibilityExtract`,
  `AllowAssemble`, `AllowPrintHighQuality`) per Table 22
- **Shape annotations — read and write** (PDF 32000-1 §12.5.6.7–9)
  - Five new public sealed classes derived from `PdfAnnotation`:
    `SquareAnnotation`, `CircleAnnotation`, `LineAnnotation`,
    `PolygonAnnotation`, `PolyLineAnnotation`
  - New shared `BorderStyle` class plus `BorderStyleType` enum
    (`Solid`/`Dashed`/`Beveled`/`Inset`/`Underline`) for the PDF
    `/BS` border-style dictionary
  - New `LineEnding` enum with all ten spec values
    (`None`/`Square`/`Circle`/`Diamond`/`OpenArrow`/`ClosedArrow`/
    `Butt`/`ROpenArrow`/`RClosedArrow`/`Slash`) for `/LE` on
    `LineAnnotation` and `PolyLineAnnotation`
  - `AnnotationType` enum extended with `Square`, `Circle`, `Line`,
    `Polygon`, `PolyLine`
  - `AnnotationReader` extended with five new subtype parsers plus
    helpers for `/BS`, `/LE`, and `/Vertices`; `ReadColor` refactored
    to take a key parameter so `/IC` (interior color) reuses the
    same code path as `/C` (border color)
  - `AnnotationWriter` extended with five new dictionary builders
    plus serializers for `BorderStyle`, line endings, line points,
    and vertices

### Tests
- 19 new tests under `tests/Chuvadi.Pdf.Documents.Tests` covering
  metadata properties (date parsing across UTC/offset/date-only/malformed
  inputs, name-vs-string Trapped variants, XMP round-trip,
  Encryption-null-when-unencrypted) and the `EncryptionInfo`
  permission decoders (per-bit theory, default deny, default allow)
- 17 new tests under `tests/Chuvadi.Pdf.Annotations.Tests` covering
  `BorderStyle` validation, shape model constructors, and full
  round-trip via `AnnotationWriter.Add` → `AnnotationReader.GetAnnotations`
  for all five shape subtypes
- Library total: 952 tests passing on the v2.0.1 commit

---

## [1.10.0] - 2026-05-20

### Added
- **Parser fuzz harness** (`tests/Chuvadi.Pdf.Fuzz/`) — hand-rolled mutation
  fuzzer with three targets: `pdf-open` (full document open), `content-stream`
  (content stream parsing), `truetype` (font loading). No NuGet dependencies.
  Mutations include splice, bit-flip, byte replace/insert/delete, boundary-value
  injection, range duplication, and random truncation. Crash inputs are saved
  to `crashes/<target>/<sha256>.bin` with full stack traces in matching `.txt`
  files for triage. See `tests/Chuvadi.Pdf.Fuzz/README.md`
- GitHub Actions workflow `.github/workflows/fuzz.yml` for scheduled fuzz runs
- `tests/Chuvadi.Pdf.Fuzz/FOLLOW-UPS.md` documenting findings deferred to
  PR 2.1 (truetype IndexOutOfRangeException bounds, PdfName.Intern
  ArgumentException tightening)

### Fixed
- `PdfPageCollection.FindPage` no longer recurses without bound on malformed
  page trees. Cyclic `/Kids` references and pathologically deep `/Pages`
  chains now throw `PdfDocumentException` with a clear message instead of
  killing the process with a `StackOverflowException`. Surfaced by the
  `pdf-open` fuzz target. Depth limit: 1024 (real PDFs use depth 1–5)
- `PdfObjectParser` no longer leaks `OverflowException` or `FormatException`
  from `int.Parse` on malformed integer tokens. All six parse sites now go
  through a guarded `ParseInt32` helper that throws `PdfReaderException` with
  the offending token's text snippet and byte offset. Surfaced by the
  `pdf-open` fuzz target after ~5.7M iterations

---

## [1.9.0] - 2026-05-19

### Added
- `benchmarks/Chuvadi.Benchmarks` project (BenchmarkDotNet harness)
  - `BrotliRatioBench`: output-size comparison vs `System.IO.Compression.BrotliStream`
    Optimal and Fastest across 6 representative scenarios (Lorem ipsum, English prose,
    repetitive, moderate, SFNT-like binary, random incompressible)
  - `BrotliThroughputBench`: encode-time comparison on the same scenarios
  - `ParserOpenBench`: `PdfDocument.Open` timing on synthetic single-page and 20-page PDFs
- `tests/Chuvadi.Pdf.Fonts.Woff2.Tests/BrotliLz77Tests.cs` — 11 regression tests covering
  the multi-command emission path

### Changed
- `BrotliCompressedEmitter` rewritten to consume the LZ77 command stream from
  `BrotliCommandStream.Encode()` and emit one Brotli command per LZ77 record. Per-block
  cap raised from 64 KiB to 16 MiB (MNIBBLES=6); inputs larger than that split across
  multiple meta-blocks
- `BrotliCompressedEmitter.TryEmit` (bool fallback) replaced by `Emit` (void, infallible
  for non-empty inputs)
- `BrotliEncoder` simplified — speculative compressed + stored, smaller wins
- Compression ratio on real data improved from 50-100% (single-command stage 3) to 2-6%
  (within ~1% of `BrotliStream` Optimal)

---

## [1.8.0] - 2026-05-19

### Fixed
- RFC 7932 §3.5 "modify rule" violation in `BrotliComplexPrefixCode.RunLengthEncode`:
  consecutive 17 (or 16) codes caused exponential count blowup per the spec's modify
  formula, producing invalid streams. Fixed by inserting a literal-length entry between
  consecutive 17s and 16s to break the run

### Changed
- `BrotliCompressedEmitter` now wires complex prefix codes for inputs with 5+ distinct
  literals (previously fell back to stored meta-blocks)
- `BrotliHuffman.BuildCanonicalCodes` uses explicit `int[] ordered` instead of `var`
  (IDE0008 conformance)
- `BrotliCodeTables.cs` switch-expression arms split one-per-line for `dotnet format`
  conformance

### Added
- `tests/Chuvadi.Pdf.Fonts.Woff2.Tests` — first test project for the WOFF2 module, with
  11 regression tests covering the modify-rule fix across 5..25 distinct literals,
  random data, and repeated text

---

## [1.7.0] - 2026-05-16

### Added
- **Phase 1.1.4** — Digital signature verification (`Chuvadi.Pdf.Signatures`)
  - PKCS#7 / CMS detached signature parsing (PDF 32000-1 §12.8.3.3)
  - Certificate chain extraction and signing-time recovery
  - Byte-range verification against the signed bytes of the document
  - Verification-only in this release; signing remains on the backlog

---

## [1.6.0] - 2026-05-15

### Added
- **Phase 1.1.6** — Linearization / Fast Web View (ISO 32000-1 Annex F)
  - Reader detects linearization and exposes the `/Linearized` parameter
    dictionary through `PdfDocument.IsLinearized` / `PdfDocument.Linearization`
  - Writer produces linearized PDFs with primary hint stream via
    `PdfWriter.WriteLinearized(...)`
  - `BitWriter` / `BitReader` and `PageHintTable` infrastructure for
    sub-byte hint encoding

### Notes
- Spec-conformant output. Real-world viewer compatibility (Acrobat,
  Foxit, browser PDF viewers) is not yet verified end-to-end and is
  tracked in the backlog

---

## [1.5.0] - 2026-05-15

### Added
- **Phase 1.1.3** — Form XObject and image redaction. `Redactor` now
  traces the `Do` operator with full CTM intersection so any Form XObject
  or image overlapping a redaction rect is dropped from the rewritten
  content stream
- **Phase 1.1.7** — Optional content (layers) reader. `OptionalContentReader`
  + `OptionalContentGroup` expose `/OCProperties`, `/OCGs`, default
  configuration name, and resolved visibility (`/ON`, `/OFF`, `BaseState`)
- **Phase 1.1.8** — CMYK render output. `PageRasterizer` and `TiffEncoder`
  support CMYK pixel buffers (TIFF photometric=5)

### Fixed
- `Redactor` was silently corrupting name operands (`Tf`, `cs`, `gs`, `Do`)
  in the rewritten content stream. Regression test added

---

## [1.4.0] - 2026-05-13

### Added
- **Encryption fully wired into the public API:**
  - `PdfDocument.Open(stream, password)` for opening encrypted documents
  - `PdfWriter.Write(..., EncryptionOptions)` for writing encrypted documents
  - `EncryptionOptions` factory methods for AES-128 and AES-256 with owner
    and user passwords
  - `EncryptionVisitor` traverses the object graph and encrypts strings and
    streams in place during write
  - `EncryptionDictionaryBuilder` implements PDF Algorithms 3/5 (R=4,
    standard security handler) and ISO 32000-2 Algorithms 8/9 (R=6,
    AES-256)
- 5 integration tests including byte-level plaintext-absence verification
  on round-tripped encrypted documents

### Changed
- AES-128 and AES-256 are now supported for both read AND write paths
  (v1.3.0 shipped read-only)

---

## [1.3.0] - 2026-05-12

### Added
- **Phase 1.1.2** — Pattern-based redaction (`Chuvadi.Pdf.Redaction`)
  - `PatternRule` and `PatternMatcher` for regex-based content matching
  - `CommonPatterns` library covering SSN, email, phone, ICD-10, NHS
    number, and other common PHI identifiers
- **Phase 1.1.9** — TIFF baseline 6.0 read and write
  (`Chuvadi.Pdf.Images.TiffDecoder` / `TiffEncoder`)
  - Uncompressed, PackBits, and LZW compression
  - Multi-page TIFFs via chained IFDs
- **Phase 1.1.5** — Standard security handler encryption (read path)
  (`Chuvadi.Pdf.Encryption`, `Chuvadi.Cryptography`)
  - RC4-40 and RC4-128 decryption
  - AES-128 decryption
  - Password-based key derivation per Algorithm 2

### Notes
- 604 tests across 19 test projects at tag time
- Write path for encryption arrived in v1.4.0

---

## [1.2.0] - 2026-05-12

### Added
- 8 runnable example projects under `examples/` (TextExtraction,
  Watermark, Redaction, Render, FormFill, Outlines, PageOps,
  Annotations)
- Getting Started guide (`docs/getting-started.md`)
- **Auto-generated Markdown API reference** (`docs/api/`, 117 pages)
  produced by `tools/gen_api_docs.py` parsing XML doc comments from
  every public type across all `src/` modules

### Changed
- CI adds a `docs-up-to-date` job that re-runs `gen_api_docs.py` and
  fails the PR if the resulting Markdown diverges from what's committed
- Style check (`tools/check_style.py`) expanded to scan `examples/`
  alongside `src/` and `tests/`

---

## [1.1.0] - 2026-05-11

### Added
- **Phase 1.1.1** — `Chuvadi.Pdf.Annotations` module (read and write
  per PDF 32000-1 §12.5)
  - `AnnotationReader` and `AnnotationWriter` covering Text, FreeText,
    Link, Stamp, Ink, Markup, and Generic annotation types
- GitHub Actions CI matrix: style check + build/test on Ubuntu,
  Windows, and macOS
- Style checker (`tools/check_style.py`) with line-by-line string
  stripping, `CONFLICT_OVERRIDES`, and `bin/` / `obj/` exclusion

### Fixed
- `Chuvadi.Pdf.Text.csproj` was missing several `ProjectReference`
  entries; added
- 10 `var`-in-`src` violations rewritten with explicit types to satisfy
  the IDE0008 rule that the new style checker now enforces

---

## [1.0.0] - 2026-05-11

### Added
- Initial public release. Closes Phase 2 (rendering, watermarking,
  redaction, forms, CLI). 17 modules, ~564 tests, 0 failures
- **Read pipeline:** PDF 1.4–2.0 ingestion (classic and stream xref,
  including hybrid); all standard non-encryption filters (Flate,
  ASCIIHex, ASCII85, RunLength, LZW); Type1 standard 14 fonts, TrueType,
  and CFF/Type1C inspection; full content stream tokenizer and parser
- **Text extraction:** operator-walking, layout-aware, and glyph-level
  fallback strategies (the glyph extractor handles non-Latin scripts via
  TrueType outline extraction)
- **Rendering:** zero-dependency scanline rasterizer producing PNG and
  BMP output; standard PDF fonts (Helvetica, Times, Courier) plus
  embedded TrueType; adaptive Bezier flattening; both fill rules;
  butt/square stroke caps
- **Document operations:** merge, split, delete pages, rotate, extract
  page ranges; text watermarks with rotation, opacity, and per-page
  targeting; image watermarks via PNG XObject embedding
- **PHI-safe redaction:** rectangle-based content-stream rewriting with
  byte-level removal (the redacted text is absent from the output PDF
  at both operator and indirect-object levels). Conservative TJ array
  drop. Tests grep the output bytes to verify removal
- **AcroForms:** read the field tree (fully-qualified names, types,
  current values, object IDs); fill values, set button `/AS`, mark
  `/NeedAppearances=true`
- **Outlines (bookmarks):** read the full tree with children and
  resolve destinations to page indices
- **CLI** (`chuvadi`): 11 user verbs (info, render, watermark, redact,
  form-fill, extract-text, outlines, merge, split, delete, rotate) plus
  6 debug verbs (tokenize, dump-objects, parse-content, decode-stream,
  inspect-xref, validate-fonts)
- Project scaffolding for all 17 Phase 1 modules, Apache 2.0 license,
  initial CI/CD pipelines

---

<!-- Template for future entries:

## [x.y.z] - YYYY-MM-DD

### Added
- New features

### Changed
- Changes to existing features

### Fixed
- Bug fixes

### Deprecated
- Features that will be removed in a future release

### Removed
- Features removed in this release

### Security
- Vulnerability fixes

-->
