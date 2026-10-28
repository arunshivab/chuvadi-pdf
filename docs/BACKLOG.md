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

Shipped in v3.17.x:
- **Hybrid XFA "just works"** — open / extract / geometry / flatten through the
  ordinary `PdfDocument` APIs; exact Standard-14 AFM widths; literal/hex string
  decoding in extraction.
- **Tight redaction ink bounds** — rectangle hit testing uses per-glyph ink
  extents, so a box in a line gap no longer eats the line above.

Shipped in v3.18.0:
- **Precise vector redaction** — `VectorRedactionMode.SubtractPrecise`
  (default): untouched subpaths survive, straight stroked segments split at
  region boundaries, axis-aligned rectangle fills decompose; whole-path drop
  remains as `DropIntersecting` and as the doubt fallback.
- **SVG density handling** — consecutive same-style path batching (always on),
  `PathSimplifyTolerance`, `MaxVectorOps` budget with `Simplify`/`Rasterize`
  fallbacks.
- **CompressToTarget rasterize fallback** — opt-in
  (`AllowRasterizeFallback`), descending-DPI image-only PDF when the vector
  floor exceeds the target.
- **Scrub-crop gaps closed** — off-box annotations removed with their
  appearance streams, inline images consumed as a unit and dropped when fully
  off-box, `sh` dropped under a fully off-box clip; `PageCropper` preloads the
  object graph (B16) so crops no longer drop lazily-unloaded objects.
- **Dynamic XFA auto-compose** — `XfaRenderer.OpenComposed` returns the
  composed static document for `/NeedsRendering` files, the original otherwise.
- **ImageMask stencils** — composited with the current fill colour in BOTH the
  raster and SVG pipelines (the earlier raster claim was aspirational).
- **Pattern fills** — tiling (PatternType 1, coloured + uncoloured) and
  shading (PatternType 2) patterns paint in the raster pipeline; shading
  patterns paint in the SVG pipeline.
- **Compression image recoding** — `DownsampleImagesToDpi` (placement-aware),
  `SmartCodecSelection` (indexed palette at 1/2/4/8 bpc vs JPEG vs original).
- **CMYK completeness** — raw DeviceCMYK embedding under Flate (separated
  TIFF frames), CMYK display in the raster sink (SVG already converted).
- **Composite autohinting in unhinted fonts** — composites flatten and Y-fit
  through the geometric autohinter like simple glyphs.
- **Xref-offset recovery** — a provably wrong classic xref offset (wrong
  object number at the offset, or unparseable bytes) falls back to a
  full-file definition scan; healthy files keep the strict fast path.
- **Perf regression baseline** — `--perf-report` /
  `--update-perf-baseline` micro-suite plus an advisory (non-required) CI job.
- **SVG sink colour follow-ups verified shipped** — `cs`/`CS`/`sc`/`scn`/`SCN`
  conversion, form-XObject recursion, and quote-operator text routing all
  landed with the rendering consolidation (the open items were stale).

Shipped in v3.19.0 (Reader delegation):
- **Scrub/redaction leak fix** — Scrub prunes unused resources and omits the
  original page's unreachable objects (content streams, replaced images and
  forms); redaction omits images whose `Do` it removed.
- **Scrub residuals closed** — inline and XObject images crossing the box are
  cropped at the sample level (any filter/bpc/colour space, masks included,
  rotated placements blanked); forms are scrubbed recursively; inherited
  resources resolve. (Was open item 7.)
- **Tiling patterns in SVG** and **pattern strokes in raster** (were open
  items 2 and 3).
- **Reader asks** — arrows (PDF-04), `PageStamper.PlaceMany` with opacity
  (PDF-05), imposition crop/rotate parity + plans (PDF-06), SVG/raster view
  rotation with opt-in `/Rotate` (PDF-07), `PageContentInventory` (PDF-08),
  header/footer `ResolveSegments` (PDF-11). PDF-10 closed as correct
  behaviour (the null-geometry dataset nodes have no template fields).

Shipped in v3.20.0 (redaction assurance + finishing items):
- **PDF-03** — tagged-content redaction strips `/ActualText`, `/Alt` and `/E`
  from inline BDC dictionaries, named `/Properties` lists and structure
  elements (a Figure's `/Alt` too when its image is removed);
  `RedactionVerifier.Verify` / `ExtractWithin` report what a region still
  paints.
- **Redaction hardening (adversarial review)** — text placed by the content
  stream's own `cm`, by `Ts` or `Tz` is matched where it is drawn; survivors
  keep their positions under `Tc`/`Tw`; soft masks, masks, tiling patterns,
  shadings and graphics states used only by removed content leave the file.
- **Downsample placements inside forms** (was open item 8).
- **Autohinter X-axis stem fitting** under full hinting (was open item 4).
- **Raster 4-point perspective deskew** — `PerspectiveCorrection` in
  `Chuvadi.Pdf.Images` (was open item 14).

Shipped in v3.21.0 (Acrobat alignment + last Reader asks):
- **Tiling patterns over 4,096 tiles** are repeated (raster bitmap tile, SVG
  `<pattern>`) instead of painting nothing; **xref recovery** tolerates any
  header whitespace and never returns a different live object (null plus a
  warning). (Were open items 9a and 9b.)
- **PDF-14** — layers hidden by the default configuration are not drawn or
  extracted; per-render overrides; `OptionalContentVisibility`.
- **PDF-15** — `/UserUnit`, viewports and `/Measure` with formatted
  real-world distances, areas and coordinates.

Shipped in v3.23.0:
- **Pattern-cell redaction edges** — patterns drawn by resource-inheriting
  forms, and decoy "shown elsewhere" tiles painted over later (was open
  item 18).
- **Scrub colour-function shadings and soft-mask groups** — sampled data
  of types 1–3 trimmed to the inputs the box shows; single-placement
  `/SMask` groups scrubbed (was open item 17, residuals listed there).
- **Verifier on palette images** — raw index samples checked (was open
  item 19).

Shipped in v3.22.1:
- **Tiling-pattern cells under redaction** — cell content shown inside a
  box is redacted per tile copy; the verifier inspects pattern cells (was
  open item 18, remaining edges listed there).

Shipped in v3.22.0 (backlog scope A):
- **Partial image redaction** — only the covered pixels are blanked (was
  open item 16).
- **Scrub residuals** — tiling-pattern cells scrubbed to what can show,
  mesh shadings trimmed to in-box elements, crossing annotation appearances
  scrubbed, unused patterns/shadings and `/Thumb` / `/PieceInfo` removed
  (was open item 7, except the parts listed under item 17).
- **Pattern strokes in SVG** — stroke-outline mask (was open item 2).
- **API docs for multi-line signatures** (was open item 15).

---

## Open roadmap

Status verified against code at v3.23.0. Items are independent and may be
re-ordered.

### Image codecs (rendering)
**1. JPXDecode (JPEG 2000).** Recognised but not decoded; JPX image bytes fall
through. A decoder is a multi-week engine (EBCOT, wavelets); long-horizon.

### Rendering follow-ups
**3. Page boxes in the default renderers.** SVG and raster render the
MediaBox in unrotated user space; `/Rotate` is applied only with the opt-in
`ApplyPageRotation` (3.19), and the CropBox is not used to bound the output.
A default switch would change every existing caller's output, so it waits for
a major version.

### Accessibility / archival
**5. Tagged-PDF structure generation.** PDF/A *output* ships
(`Chuvadi.Pdf.PdfA`), and hybrid `/StructTreeRoot` on real files resolves, but
generating a `/StructTreeRoot` during page creation (PDF/UA-1 tagging) is not
yet implemented. *Large — long-horizon.*

### Architecture
**6. Shared value-types move (documented defer).** `PdfBlendMode`,
`BlendModes`, `Type3Font`/`Type3Glyph` live in
`Chuvadi.Pdf.Rendering.DisplayList` and are consumed by Raster via aliases.
The clean move is into `Chuvadi.Pdf.Rendering.Walking` (Type3Font already
depends on Walking) with `[assembly: TypeForwardedTo(...)]` shims left in
DisplayList for binary compatibility, after which Raster can drop its
DisplayList reference. Deferred: touches the public surface of three packaged
assemblies for zero user-visible gain; do it at a major-version boundary.

### Scrub-crop residuals
**17. Remaining Scrub residuals (after 3.23).** Kept whole under Scrub:
an ExtGState soft mask (`/SMask /G`) set under more than one placement;
the glyph procedures of Type 3 fonts (and anything their resources hold);
exponential (Type 2) and PostScript (Type 4) functions (no sampled data to
trim); a radial shading whose painted area ends inside the box. Sampled
functions keep a 2-sample margin. Where the visible parts of a tiling cell
overlap, the merged group is scrubbed to its bounding box in the cell, not
the exact union.
Chuvadi does not render mesh shadings (types 4–7) or function-based
shadings (type 1), so those trims are verified structurally; check them
visually in another viewer.
Rasterizer note (not a Scrub defect): pattern fills are not anti-aliased,
so an edge passing exactly through pixel centres can flip one row of
pixels when Scrub splits the path at the box (seen with a rotated radial
pattern; shifting the geometry by a fraction of a point removes it).

### Rendering (major-version switch)
**9. /UserUnit in the default renderers.** `PdfPage.UserUnit` is exposed
(3.21) but the SVG and raster renderers still size output from the MediaBox
in plain points, so a large-format drawing with `/UserUnit 10` renders at a
tenth of its physical size. Honouring it changes output size for existing
callers, so it waits for the same major-version switch as item 3.

### Compression workstream (long-horizon engines)
The image-recoding base has shipped (downsample-to-DPI, per-image codec
selection, CMYK completeness). The remaining items are multi-week engines,
mostly gated on each other; the detailed plan lives in
`docs/chuvadi-35-item-roadmap-handoff.md`.

**10. JBIG2 encode.** Decode ships (`Filters/Jbig2/`); the encoder (sharing the
segment model) does not. Gates the bitonal path of MRC.

**11. JPX / JPEG2000 encode.** Only worth doing if it beats DCT+MRC; depends
on a JPX decoder (#1, also open).

**12. MRC (Mixed Raster Content).** The colour-scan compression differentiator
— foreground/background/mask separation. Downsampling (shipped) was a
dependency; still needs bitonal detection + G4 and JBIG2 encode (#10).

**13. SSIM perceptual target.** SSIM *measurement* exists in the benchmark
suite; the optimisation *target knob* ("smallest file at visually lossless")
that would drive the image-recoding stack does not.

---

## Pre-1.0 / distribution housekeeping (separate track)
Publish to nuget.org (currently local-feed only), reserve the `Chuvadi` package
prefix, and add a real `icon.png`. (The `Chuvadi.Pdf` meta-package's direct
dependencies were reconciled in v3.18.0 — it now lists Xfa, PdfA,
Text.Shaping, Rendering.Raster and Rendering.Walking directly.)

---

## Triage rules
1. Items move to a CHANGE-LOG A-entry when work begins.
2. Open items are independent and may be re-ordered.
3. **Verify status against the code before starting** — this file has drifted
   before; a quick grep prevents re-building shipped features.
