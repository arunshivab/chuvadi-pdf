# BACKLOG.md — Roadmap

> Tracks capabilities not yet shipped. Items move to CHANGE-LOG.md when work
> begins (creating a new A-entry for the decision to take it on).
> **Reconciled against the actual code at v3.16.0.** Many items previously
> listed open were verified shipped and moved to the Shipped section below;
> the open roadmap now lists only genuinely-unbuilt work, each status checked
> against source.

---

## Shipped (kept for traceability)

Core Phase 1.1/1.2 roadmap:
- **Annotations (read + create)** — `Chuvadi.Pdf.Annotations`.
- **Digital signatures** (verify, sign, timestamp, LTV) — `Chuvadi.Pdf.Signatures`
  (+ `Chuvadi.Cryptography`).
- **Encryption (read + write)** — `Chuvadi.Pdf.Encryption` (RC4-40/128, AES-128/256).
- **CMYK render output**; **TIFF encoder/decoder** — `Chuvadi.Pdf.Images`.
- **Vector page creation** — `Chuvadi.Pdf.Authoring` (`PdfDocumentBuilder`/`PageBuilder`).
- **Image to PDF conversion** — `ImagePdfConverter`.
- **Report layout** — `ReportBuilder`.
- **TrueType bytecode hinting** (Light/Full) + **geometric autohinter**, incl.
  **composite-glyph hinting** (v2.6.0).

Rendering / codecs verified shipped:
- **Shadings (`sh`)** — `ShadeOp`, `PdfShading`, axial/radial and beyond, in
  both raster and SVG sinks.
- **Graphics-state transparency (`gs`)** — constant alpha (`ca`/`CA`), blend
  modes (`BM`), and ExtGState soft masks (`SMask`) honoured
  (`Raster/BuilderGraphicsState.cs`).
- **Non-device colour spaces** — Separation/DeviceN tint-transform evaluation,
  Indexed lookup, ICCBased alternate, Lab, Cal* in the raster image and
  fill/stroke paths.
- **Type3 fonts** — `CharProcs`/`d0`/`d1` glyph content streams render.
- **JBIG2Decode** — full decoder (`Filters/Jbig2/`): arithmetic integer coder,
  generic and text regions.
- **Complex-script (Indic) shaping** — `Chuvadi.Pdf.Text.Shaping` (`TextShaper`,
  `LipiScript`, GSUB/GPOS features).
- **PDF/A output** — `Chuvadi.Pdf.PdfA` (output intents, font substitution,
  Liberation font provider).
- **XFA form rendering + scripting** — `Chuvadi.Pdf.Xfa`: data merge, layout,
  pagination, duplex/keep, tables, widgets, and the FormCalc + JavaScript
  scripting engines. `XfaRenderOptions.ScriptMode` defaults to `Full`.
- **Annotation appearance rendering (§12.5.5)** — all sinks (raster, SVG,
  text display list) draw each visible annotation's `/AP /N` form placed onto
  its `/Rect`; text extraction includes appearance text; hybrid XFA field
  values render, extract, and flatten. Public API:
  `PageAnnotationAppearances.Collect` / `AnnotationAppearance` (Documents).
- **Xref chain precedence (§7.5.6)** — the entry in the most recent
  incremental-update section supersedes all earlier ones of any kind; free
  entries shadow older definitions and compressed entries are not replaced.
  Signed/filled government certificates resolve their newest generation.
- **Hybrid-reference (`/XRefStm`) resolution** — compressed objects on
  Word/Office PDFs resolve; `HasStructTree`/`IsTagged` correct.
- **Object streams + xref streams (writer)** — `PdfWriter` packs compressible
  objects into chunked `/ObjStm` containers.
- **Dynamic-Huffman DEFLATE** — the deflater emits the smaller of stored, fixed,
  and dynamic Huffman; `DeflateEffort.Maximum` adds a lazy-match parse.
- **Nested-form redaction (recursion)** — the redactor recurses into form
  XObjects' content streams with a cycle guard.
- **Pattern-based redaction**, **non-text (image/form) redaction**,
  **inline-image (`BI/ID/EI`) redaction**, **optional content (layers)**,
  **linearization (Fast Web View)**, **one-call render facade**, **streaming
  page enumeration**, **parallel redaction**, **custom TrueType embedding**,
  **glyph subsetting**, **copy-with-format text style**, **image `/SMask` in
  SVG**, **ImageMask stencil compositing (raster)**, **redaction-grade crop**
  (`PageCropMode.ClipOnly`/`Scrub`), and the **rendering project split**
  (Walking/DisplayList/Raster).

---

## Open roadmap

Status verified against code at v3.17.0. Items are independent and may be
re-ordered.

### Image codecs (rendering)
**1. JPXDecode (JPEG 2000).** Recognised but not decoded; JPX image bytes fall
through and render blank/garbage. *Very large (wavelets, EBCOT).*

### Rendering fidelity
**2. Patterns.** Tiling (PatternType 1) and shading (PatternType 2) patterns are
not painted — a `scn` pattern name marks the colour invalid, so pattern-filled
regions are suppressed. Needs pattern-cell replay and shading-pattern fill.
*Medium impact.*

**3. ImageMask stencil compositing in the SVG path.** Raster handles
`/ImageMask true`; the SVG renderer skips such 1-bit stencils. Reuse the RGBA-PNG
machinery from the `/SMask` path to paint the stencil with the current fill
colour. (`/SMask` soft-mask alpha is already handled in SVG — separate feature.)

**4. SVG sink colour follow-ups.** The SVG sink does not implement `cs`/`CS`/
`sc`/`scn`/`SCN` colour operators (raster does), and does not recurse into form
XObjects; the raster quote operators (`'`/`"`) bypass composite-font routing.
*Small — one tidy PR.*

### Fonts
**5. Autohinter follow-ups.** Composite-glyph Y-fitting in unhinted fonts;
optional X-axis stem fitting for mono/low-DPI. (Base composite hinting for
hinted fonts shipped in v2.6.0.)

### Accessibility / archival
**6. Tagged-PDF structure generation.** PDF/A *output* ships
(`Chuvadi.Pdf.PdfA`), and hybrid `/StructTreeRoot` on real files now resolves,
but generating a `/StructTreeRoot` during page creation (PDF/UA-1 tagging) is
not yet implemented. *Large.*

### Performance
**7. Automated benchmark regression diffing.** The BenchmarkDotNet suite (Brotli,
parser-open, rasterizer) exists; per-release baseline capture + auto-compare in
CI remain (a compression-ratio baseline gate already exists — extend the pattern).

### Input robustness / recovery
**8. General xref-offset recovery on load.** When a classic xref entry points at
the wrong byte offset, the affected object resolves to the wrong primitive. The
narrow case — a page-tree `/Kids` entry that resolves to a non-dictionary — is
recovered in-walk and surfaced via `PdfDocument.Warnings`/`IsRecovered`. The
broader, deferred work: validate any resolved object against its xref offset and
fall back to a full-file definition scan when the offset is provably wrong.
Should be opt-in or confined to objects that fail a type/role check so healthy
files keep the fast strict path.

### Compression workstream (from the 7-phase roadmap)
The lossless compression core has shipped (object/xref-stream writing, GC +
dedup, max-level re-deflate, content minification, granular stripping, JPEG
re-encode). These image-recoding and perceptual items remain, mostly gated on
each other; the detailed plan lives in `docs/chuvadi-35-item-roadmap-handoff.md`.

**9. Image downsampling to target DPI.** Resampling exists inside the rasterizer
but not as a compression-path "downsample image XObjects to a target DPI" step.
Dependency for MRC and the perceptual target.

**10. Smart per-image codec selection.** Per-image codec / bit-depth / palette /
colourspace-and-ICC-reduction decision logic (indexed/palette detection, bit-depth
reduction). Not yet present.

**11. CMYK image completeness.** CMYK/YCCK JPEG passthrough ships (embeds as real
`DeviceCMYK` under DCTDecode with the Adobe-inversion `/Decode`). Remaining: raw
DeviceCMYK sample embedding under FlateDecode, and CMYK-to-output conversion in
the raster and SVG sinks so CMYK images display. Pairs with non-device colour
(ICCBased-CMYK).

**12. JBIG2 encode.** Decode ships (`Filters/Jbig2/`); the encoder (sharing the
segment model) does not. Gates the bitonal path of MRC.

**13. JPX / JPEG2000 encode.** Only worth doing if it beats DCT+MRC; depends on a
JPX decoder (also open).

**14. MRC (Mixed Raster Content).** The colour-scan compression differentiator —
foreground/background/mask separation. Depends on downsampling (#9), bitonal
detection + G4, and JBIG2 encode (#12).

**15. SSIM perceptual target.** SSIM *measurement* exists in the benchmark suite;
the optimisation *target knob* ("smallest file at visually lossless") that would
drive the image-recoding stack does not.

**16. Raster 4-point perspective deskew.** A Chuvadi Reader "Bench" image-processing
feature (4-point perspective correction / deskew). Application-adjacent.

---

## Pre-1.0 / distribution housekeeping (separate track)
Publish to nuget.org (currently local-feed only), reserve the `Chuvadi` package
prefix, add a real `icon.png`, and reconcile the `Chuvadi.Pdf` meta-package's
direct dependencies so it lists the newer modules (Xfa, PdfA, Text.Shaping,
Color, Rendering.Raster, Rendering.Walking) rather than relying solely on
transitive resolution.

---

## Triage rules
1. Items move to a CHANGE-LOG A-entry when work begins.
2. Open items are independent and may be re-ordered.
3. **Verify status against the code before starting** — this file has drifted
   before; a quick grep prevents re-building shipped features.
