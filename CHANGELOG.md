# Changelog

All notable changes to Chuvadi will be documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This file records release-by-release notes. Architectural decisions and
rationale live in `docs/CHANGE-LOG.md` (an append-only decision log,
numbered A01..ANN).

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
