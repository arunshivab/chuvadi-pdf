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

Shipped in v3.30.2:
- **SVG text at PDF positions** for embedded fonts, and CFF advance widths
  after `hmoveto`, `vmoveto` and an opening `hintmask` (Reader report:
  Elsevier paper, search box one space left of the text).

Shipped in v3.33.0 (D-302, D-307, D-308, D-325):
- **M1** — one standard-14 embedding engine (`Chuvadi.Pdf.Authoring`),
  used by `PdfAWriter` (output unchanged for WinAnsi fonts), by the opt-in
  `PdfDocumentBuilder.EmbedStandardFonts()` and by the new
  `StandardFontEmbedder.Embed` operation. Liberation faces; Symbol and
  ZapfDingbats stay reported, not embedded (URW licence not confirmed).
- **46** — a PDF/A substitute keeps the characters the original encoding
  named (D-314): `/Differences` over WinAnsi for PDF/A-2b and the embed
  operation, a symbolic font with a (3,0) cmap and `/ToUnicode` for
  PDF/A-1b. WinAnsi fonts unchanged.
- **Bug fixes found on the way (D-318):** `MacRomanEncoding` was missing
  `ã` and `õ` (115 codes shifted); the binary header comment was written as
  `%????`; PDF/A-1b XMP Producer and CreatorTool now follow `/Info`.
- **PDF-17 (Reader ask, D-322)** — `OutlineWriter` keeps a bookmark's
  target, colour, style and open state (`OutlineEntry` from its source
  `OutlineItem`), and writes exact destinations (`OutlineEntry` with a
  `PdfDestination`).
- **48** — repair keeps the newest definition of an object, ranked by file
  position, an object inside an object stream at its stream's position.
- **47** — the style checker's `var` rule runs on relative paths (as in CI)
  and ignores `var` in strings and names; the one real `var` in
  `BrotliSimplePrefixCode` is typed.
- **32** — already fixed in 3.31.0: SVG ids come from a stable hash, so the
  same page renders to the same SVG text (checked on 173 pages in two
  processes). Closed.
- **45** — shaped Indic text in generated form-field, comment and signature
  appearances copies and searches as written: a glyph that alone shows a
  cluster gets the text through ToUnicode when that cannot contradict
  another use, any other cluster is wrapped in `/ActualText`, as
  `DrawShapedRun` does. Text extraction also keeps the measured width of
  annotation text (a false space inside a word before).
- **39** — a page whose content stream is damaged shows and extracts what
  decoded before the damage (cut back to the last whitespace) and reports
  `StreamDamaged`; nothing when nothing decoded (option A, D-325). A path
  continued after `h`/`re` starts at the closed subpath's start instead of
  throwing; a curve with no current point is skipped with
  `ContentOperatorSkipped` (D-332). Damaged set of 50: text 50/50 (was
  20), drawing 50/50 (was 13); originals and corpus unchanged.
- **49, the small part** — PDF/A-1b XMP `dc:title`, `dc:creator` and
  `dc:description` follow `/Info` `/Title`, `/Author` and `/Subject`.

Shipped in v3.32.1 (D-280):
- **24** — damaged cross-reference recovery: opt-in
  `PdfOpenOptions.RepairIfDamaged` repairs in memory with `PdfRepairer` and
  opens the result, saying so in `Warnings` (see 48 for a limit).

Shipped in v3.32.0 (Reader and Print asks, D-208 to D-276):
- **The 3.32.0 plan** — items 1c (signature appearance with text, image and
  LiPi fallback), 2 (permissions as flags), 3 (`OpenedWith`, PDF-16),
  4 (destination details), 5 (layer ids, locks, id overrides), 6 (viewports
  cached), P2 (CropBox shown by default, `DisplayBox` opt-out), P3 (curved
  strokes cut at redaction box edges).
- **PDF-09** — named compression levels (Lossless, Medium, High,
  ReducedColor, BlackAndWhiteWithColor, BlackAndWhite) and target mode by
  level with a 130 dpi readable floor; the rasterize fallback keeps rotation.
- **38** — `WpfRenderer` draws a box that does not start at 0,0 where it is.
- **M2** — `AnnotationFlattenOptions.Mode` (View or Print).
- **Indexed images** drawn by the raster renderer (they were skipped; the
  "Indexed lookup" above held for fills and strokes only) and read at 1, 2
  and 4 bits in SVG.
- **Form XObjects inherit the graphics state** in raster; **Deflate encoder**
  code-length limit; **inflater rejects over-subscribed codes** with a
  `StreamDamaged` warning; **one font-descriptor rule** for `/StemV` and
  `/Flags`.

Shipped in v3.31.2 (bug fixes, D-207 to D-221):
- **Signing real documents** — `PdfSigner.Sign` and `SignAsync` find their
  own signature dictionary through the written xref (they took a page's
  `/Contents` before); already-signed input is refused with a pointer to
  `PdfCounterSigner.AddSignature`. `AddSignature` honours `Appearance`.
- **Links to named destinations** and page indexes from the page tree for
  links and outlines.
- **Bounded name table** — names compare by value; the shared table holds the
  well-known names plus at most 4,096 others of up to 64 characters.
- **Shared `FontRenderer`** is safe between threads.
- **Pages whose MediaBox does not start at 0,0** render in raster and SVG (P1).
- **Platform** — `IsAotCompatible` on every src package except Rendering.Wpf,
  `docs/THREAD-SAFETY.md`, no unused `AllowUnsafeBlocks`, SDK package
  validation against the previous release.

Shipped in v3.31.1:
- **`FontFace.Load` speed** (D-201): only the full-repertoire character map
  subtable that is chosen is read; output unchanged.

Shipped in v3.30.1:
- **FreeType credit** (D-106, part 1): the FTL credit and licence ship with
  `Chuvadi.Pdf.Fonts.Rendering`, the package that holds the FreeType-derived
  hinting code.

Shipped in v3.30.0:
- **Fonts that went missing** (D-105): unknown font names are refused by
  Authoring, font resource names are written validly, the raster renderer
  substitutes fonts a PDF does not embed (Liberation outlines, `/Widths`
  positions, `FontSubstituted` warning; Indic text with the bundled LiPi Sans
  faces, which now live in the shared Chuvadi.Pdf.Fonts.Bundled package,
  D-87, D-100), escaped resource names resolve, and SVG fonts carry the tables
  browsers require.

Shipped in v3.29.0:
- **Reading order and raised text** (were open items 28 and 29, D-104):
  Layout mode follows a Tagged PDF's structure tree, otherwise reads columns
  of running text one after another (`TextExtractor.AnalyzeLayout`,
  `PageLayout`); superscripts and subscripts are written with their Unicode
  forms by default (`SuperscriptMode`), ordinals join their number ("09th"),
  and fragments carry `BaselineShift`. Search matches compatibility forms.

Shipped in v3.28.0:
- **Text positions in user space** (was open item 26): fragments are placed
  through the text matrix and the CTM (and a form's /Matrix); text-space
  moves are scaled with the text. Superscripts and subscripts stay separate
  tokens, rotated text reads as lines of its own (`TextFragment.Angle`), and
  Authoring no longer references Pdf.Documents (D-99).

Shipped in v3.27.0 (fallback warnings and 3.24–3.26 follow-ups):
- **Fallback warnings** (D-62, D-63): `PdfOpenOptions.OnWarning` receives a
  `PdfWarning` for each substituted font, skipped glyph and skipped shading.
- **Nesting refusal message** (D-82) names the object, the depth found and the
  limit; the 512 limit itself is unchanged.
- **`cpsp` on by default again** (D-68); **`Shape(byte[])` reuses the loaded
  face** (D-70); **dot leaders kept apart from the label** in extracted text
  (D-72); **font licence texts** in the Authoring (LiPi Sans) and PdfA
  (Liberation) packages (D-61).

Shipped in v3.26.0 (HarfBuzz-conformant shaping):
- **Indic shaping conformance** (was open item 21): `TextShaper` is a port
  of HarfBuzz's OpenType shaper; glyphs, UTF-16 clusters, advances and
  offsets match HarfBuzz 15 on 29,493 lines (the 19,641-syllable corpus and
  a 9,852-line word/stress corpus) for all nine LiPi Indic scripts and Latin.
- Fixed on the way: no word gap after `/ActualText` in text extraction (real
  glyph widths in the content parser), sRGB transparency on the aliased-fill
  and image raster paths.

Shipped in v3.25.0 (fonts and drawing for Chuvadi.Layout):
- **`FontFace`** metrics API, **`TextShaper.Shape(FontFace, …)`** with lookup
  caching and a BenchmarkDotNet `ShapingBench`, **CFF (.otf) and collection
  (.ttc) embedding** with CID-keyed CFF subsetting, **searchable shaped runs**
  (ToUnicode / `/ActualText`, honoured by `TextExtractor`), `GlyphRunStyle`,
  clip and opacity scopes, rectangle links (URI and GoTo), `LineStyle`
  dashes, image crops.
- Fixed on the way: CFF shortint operand, CID-keyed CFF font dictionaries,
  CFF glyph bounds, raster CID→GID (`/CIDToGIDMap`, CFF charset), raster
  dashes and dots, round and square caps, raster line width/dash under a
  scaling CTM, triangle and bow-tie clips taken for rectangles, constant
  opacity composited in linear light, SVG dash scale and rotated stroke
  width, inline-image data read as text-extraction content.

Shipped in v3.24.0 (embedding API, Anjal Mail asks A1–A5):
- **Password-required signal**, **viewing guarantees + `PdfSanitizer`**,
  **cancellation**, **resource limits**, **no-disk guarantee** and an
  **enumerated exception set** — see docs/EMBEDDING.md.

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

Status verified against code at v3.26.0. Items are independent and may be
re-ordered.

### Reader asks (open)
None open (39 shipped in 3.33.0).

### Rendering (3.32.0 findings)
**40. WPF clipping (Q10, D-231).** `WpfRenderer` ignores `ClipOp` (not
implemented since v1). The Reader does not draw through WPF; needed if
Windows printing later goes vector through `Chuvadi.Pdf.Rendering.Wpf`.

**41. SVG shading inside a group, soft mask or Type3 glyph.** A `sh` in a
nested display list paints nothing (a 0 by 0 rect); 12 corpus pages, mostly
colour emoji. Filling the page box instead improved 9 pages but made 2 worse,
because SVG does not apply the group's soft mask to the shading. Fix: apply
the group's mask and clip to the shading.

**42. Soft mask in effect at a form's `Do` (D-264).** The raster renderer
does not apply an ExtGState soft mask set before `Do` to the form's content.

**43. Annotation `/OC` and print-state layers.** An annotation's own `/OC`
layer and a layer's `/Usage /Print /PrintState` are honoured by neither the
renderer nor `AnnotationFlattener`.

### Redaction
**44. Curved fills crossing a redaction box (Q9).** 3.32.0 cuts curved
strokes; a single-subpath curved fill (a filled circle or rounded panel)
crossing a box is still removed whole. Cutting it is a region boolean with
curves and needs its own proof set.

### Text shaping
**25. Shapers not ported (3.26 scope note).** `TextShaper` ports HarfBuzz's
Indic and default shapers only. Not ported: Arabic, Hebrew, Thai/Lao,
Hangul, Khmer, Myanmar and USE (a font with only `dev3`-style tags is shaped
with the Indic shaper, not USE), variation sequences (cmap format 14), the
legacy `kern` table, vertical text, and variable-font instances other than
the default. None is needed by a LiPi script; take up when a consumer needs
one.

**27. Indic shaping speed.** With the face reused, `Shape(byte[])` is as
fast as the `FontFace` overload, and faster than 3.25 for Latin and Tamil;
Devanagari is about 8% slower than 3.25's byte overload (74 µs against
68 µs on a 2-core sandbox), because the full Indic pipeline does more work. The cost is in
applying the many Devanagari lookups; profile `OtApplyContext.ApplyForward`
if a consumer needs more.

### Text extraction
**30. Letters recovered from the embedded font.** When a font's ToUnicode
map is missing or wrong (the Word-made maths test sheet in the 3.29
reference pack), WPS reads the real characters from the embedded font's own
cmap; Acrobat and Chuvadi give the wrong letters. Take up when a consumer
needs maths or symbol text from such files.

### Rendering (3.30 findings)
**36. SVG text when ToUnicode contradicts the encoding (D-175, D-201).** In
an embedded TrueType simple font the raster renderer chooses the glyph from
the font's encoding (3.31), but the SVG text is the ToUnicode character, and
the browser draws that character's glyph from the embedded font. The two
differ only when ToUnicode contradicts the encoding; no PDF in the corpus
does. Measure on such files before choosing a fix.

**37. Glyph choice in non-embedded fonts (D-175, D-201).** A substitute font
still chooses glyphs from the decoded text, not from the encoding's glyph
names. On the O9 Courier fixture Chuvadi draws "BBBB" where MuPDF draws
"AAAA". Measure on the corpus before choosing a fix.

### Hinting provenance (D-106)
**33. Clean-room TrueType interpreter.** Rewrite `HintingInterpreter` from
the Apple and Microsoft TrueType instruction-set specifications only, with a
trace log, and delete the FreeType-derived interpreter once the new one
matches today's output on a hinting test set. Plan and estimate first; no
rewrite code until Arun approves.

**34. Autohinter: rewrite or drop.** Measure what the autohinter changes on
real documents, then report to Arun the pros and cons of rewriting it from a
published design description against dropping it. No rewrite before Arun
decides.

**35. The setup program's graphics state (D-201).** Apple's TrueType manual
says the graphics state left by the control value program (`prep`) is the
starting state of every glyph program. The interpreter starts every glyph
from the defaults instead, and the clean-room rewrite (33) copies that,
because its gate requires today's output. Fix it after the rewrite passes, as
its own measured Rule 5 change: it changes hinted output for fonts whose
`prep` sets graphics state.

### Rendering follow-ups (3.25 findings)
**22. Round joins** are drawn as bevel joins in the raster stroker (round
and square caps and dotted lines are done, 3.25).
**23. Line width under a non-uniform CTM** uses sqrt(|det|) (one width for
the whole stroke); a true elliptical pen needs stroking in user space.

### Image codecs (rendering)
**1. JPXDecode (JPEG 2000).** Recognised but not decoded; JPX image bytes fall
through. A decoder is a multi-week engine (EBCOT, wavelets); long-horizon.

### Rendering follow-ups
**3. Page boxes in the default renderers.** SVG and raster render the
MediaBox in unrotated user space; `/Rotate` is applied only with the opt-in
`ApplyPageRotation` (3.19), and the CropBox is not used to bound the output.
A default switch would change every existing caller's output, so it waits for
a major version. The CropBox part shipped in 3.32.0 (P2); the `/Rotate`
default still waits. Since 3.31.2 a MediaBox that does
not start at 0,0 is drawn correctly (P1).

### Accessibility / archival
**49. `PdfAWriter`: what still fails veraPDF (3.33.0 measurement).** veraPDF
1.28.2 (a test tool, never shipped) over the PDF/A output of 413 corpus files:
3.33.0 passes 286 of 413 at PDF/A-1b and 373 of 413 at PDF/A-2b (main: 0 and
0, because of the header comment and the Producer metadata, both fixed in
3.33.0, D-318). No file fails a rule it passed before. What still fails comes
from the source files, which `PdfAWriter` does not yet convert:
- **PDF/A-1b:** transparency groups in forms (69 files) and `CA`/`ca`/`SMask`
  in ExtGState (12-23); DeviceCMYK without a CMYK output intent (23);
  annotations without the Print flag (10); Type 1 subsets without
  `/CharSet` (9); `/Info` Keywords not matched by XMP `pdf:Keywords`
  (6). `/Info` Title, Author and Subject now
  follow into XMP (the small part, D-325): 302 of 413 pass PDF/A-1b, and
  39 files lose their 6.7.3 failures; none gains a failure.
- **PDF/A-2b:** DeviceCMYK without a DefaultCMYK (23); fonts used outside
  page resources (annotation appearances, Type 3 glyphs) not embedded (9);
  `NeedAppearances` in the form (9); `.notdef` glyphs used (7); annotation
  flags (4-6); `TR` in ExtGState (4); signatures (4).
Take up per item; the rest of Info-to-XMP (Keywords, dates) is small and
follows the D-318 Producer fix.

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

### Embedding follow-ups
**20. Interrupting single JBIG2 / CCITT image decodes.** Cancellation and
the decoded-size limit are checked when such an image finishes decoding, not
part-way (JPEG, Flate, LZW and the other filters are checked as they go).

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
