# BACKLOG.md — Roadmap

> Tracks capabilities not yet shipped. Items move to CHANGE-LOG.md when work
> begins (creating a new A-entry for the decision to take it on).
> Refreshed for v2.7.0 — earlier revisions of this file predated several
> shipped modules and have been reconciled below.

---

## Shipped (previously listed here)

These items were on the original Phase 1.1/1.2 roadmap and have since
shipped; they are kept for traceability:

- **Annotations (read + create)** — shipped as `Chuvadi.Pdf.Annotations`.
- **Digital signatures** — shipped as `Chuvadi.Pdf.Signatures` (+ `Chuvadi.Cryptography`).
- **Encryption (read + write)** — shipped as `Chuvadi.Pdf.Encryption`.
- **CMYK render output** — shipped (CMYK pixel pipeline + CMYK TIFF encoder in `Chuvadi.Pdf.Images`).
- **TIFF encoder / decoder** — shipped (multi-frame decode + single/multi-frame encode in `Chuvadi.Pdf.Images`).
- **Vector page creation** — shipped as `Chuvadi.Pdf.Authoring` (`PdfDocumentBuilder` / `PageBuilder`).
- **Image → PDF conversion** — shipped in v2.7.0 (`ImagePdfConverter`; JPEG/PNG/TIFF/BMP with alpha soft masks; new `BmpDecoder`).
- **Report layout** — shipped in v2.7.0 (`ReportBuilder`: flowing paragraphs, lists, span-aware tables with repeating headers, headers/footers, formatted page numbers, images).
- **TrueType bytecode hinting** — shipped across v2.2–v2.6 (`Chuvadi.Pdf.Fonts.Rendering.Hinting`; Light default, Full classic, composite hinting).
- **Geometric autohinter (Y-fitting fallback for unhinted fonts)** — shipped in v2.7.0, on by default with `RenderOptions.AutohintUnhintedFonts` opt-out.

---

## Active / Next

### N.1 DisplayList consolidation
**Status:** SHIPPED in v2.7.1 (CHANGE-LOG A32). One shared content-stream
walker now feeds both display-list builders as sinks; the duplicated
tokenise/parse/dispatch machinery is single-sourced. Recorded follow-ups:
SVG sink ignores cs/scn colour operators (raster implements them); raster
quote operators bypass composite-font routing; SVG sink does not recurse
into form XObjects.

### N.2 Autohinter follow-ups
**Status:** v2.7.0 ships Y-only fitting for simple glyphs.
- Composite glyphs in unhinted fonts (component-wise Y fitting with
  grid-rounded offsets).
- Optional X-axis stem fitting using the existing vertical-stem detector
  (mono / low-DPI scenarios).

### N.3 Pattern-based redaction
**Status:** Not started.
**Why:** Phase 2 redaction is rectangle-based. Hospitals frequently want
"redact every SSN", "redact every email address", "redact every MRN"
without computing coordinates by hand.
- Regex matcher running on extracted text fragments.
- Resolve each match back to a device-space rectangle using the same
  glyph positions that the text extractor recovers.
- Pre-built patterns: SSN, US phone, email, ICD-10 code prefix, MRN
  (hospital-configurable).
- Extension to `Chuvadi.Pdf.Redaction.RedactionOptions` with a
  `Patterns` collection.

### N.4 Redaction of non-text content
**Status:** Not started. Redaction targets text-showing operators only.
- Inline-image (`BI/ID/EI`) and image XObject (`Do`) operator removal
  when the painted area intersects a redaction rectangle.
- Form XObject (`Do`) recursion into nested content streams.

### N.5 Linearization (Fast Web View)
**Status:** Not started.
- Write linearized PDFs so the first page can render before the full
  document is downloaded.

### N.6 Optional content (layers)
**Status:** Not started.
- Read and toggle `/OC` optional content groups.

### N.7 Font embedding (subsetted)
**Status:** New content uses the 14 standard fonts.
- Embed an arbitrary TrueType as a subsetted CIDFontType2 / Type0.
- Required for non-Latin scripts in generated content
  (Tamil, Devanagari, Han, etc.) — including ReportBuilder content.

### N.8 Tagged PDF / accessibility
**Status:** Not started.
- Generate structure trees (`/StructTreeRoot`) on page creation.
- Compliance: PDF/UA-1 first, then PDF/A-3.

---

## Performance & Scale

### P.1 Streaming page enumeration
- Open a 10 000-page PDF without loading every page into memory.

### P.2 Parallel redaction
- Per-page redaction tasks run in parallel; serialise writes.

### P.3 Benchmarks and regression detection
- BenchmarkDotNet suite for hot paths (tokenizer, deflate, rasterizer).
- Baseline timings tracked per release.

---

## Backlog Triage Rules

1. Items move from this file to a CHANGE-LOG A-entry when work begins.
2. Items N.3 onward may be re-ordered without breaking compatibility —
   they are independent.
3. N.1 (DisplayList consolidation) is sequenced immediately after the
   v2.7.0 merge to avoid the two implementations drifting further.
