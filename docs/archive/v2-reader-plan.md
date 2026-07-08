# v2.0.0 — Reader app library work plan

> Living document. Each PR updates the status table as it lands.
> Authoritative source: `chuvadi-library-requirements-v1.md` (from the
> Chuvadi Reader app team).

---

## Status

**Date opened:** 2026-05-20
**Tag target:** v2.0.0
**Current tag on main:** v1.10.0
**Scope:** Full Reader requirement set from the app spec, plus all
prerequisites, refactors, and CI work needed to support it.

Split and Merge work (Parts 2 and 3 of the app spec) is **deferred to
v2.1.0 and v2.2.0** — those depend on the v2.0.0 surface and the app
team is staging its own work in the same order (Reader → Merge → Split).

---

## Context

The app spec was written assuming Chuvadi was at v1.4.0 era. Chuvadi
is actually at v1.10.0. The spec's proposed tag scheme
(PR 1 → v1.5.0 … PR 5 → v1.9.0) is therefore inapplicable.

Rather than mint v1.11.0 / v1.12.0 / v1.13.0 for the three reader PRs,
the work is being grouped as **v2.0.0** because:

- It introduces a new architectural seam (PageDisplayList) the entire
  rendering pipeline reorganizes around
- It is intentionally API-breaking: exception hierarchy, async surface,
  rendering module structure all change
- Calling it v2.0.0 signals "breaking changes — consumers must adapt"
  honestly to anyone tracking releases

---

## Design decisions

These three were settled before any code was written. Each cascades
through every downstream choice; they are load-bearing.

### D1 — Exception hierarchy: clean break

The current four-type hierarchy (PdfReaderException,
PdfDocumentException, PdfTokenizerException, PdfObjectException) is
renamed and rerooted under a new `PdfException` abstract base. Mapping:

| Old | New |
|---|---|
| (none) | `PdfException` (abstract base) |
| `PdfReaderException` | `PdfParseException` (carries `Offset`) |
| `PdfTokenizerException` | `PdfParseException` |
| `PdfObjectException` | `PdfParseException` for structural-shape errors |
| `PdfDocumentException` | `PdfCorruptionException` for semantic-integrity errors (cyclic refs, missing required entries) |
| (new) | `PdfEncryptionException` |
| (new) | `PdfPermissionException` (carries `Required` `PdfPermissions`) |

Module-specific exceptions (`AnnotationException`, `ContentException`,
`FilterException`, `FontException`, `FormException`, `ImageException`,
`OperationsException`, `RedactionException`, `RenderingException`,
`SignatureException`, `WatermarkException`, `FontRenderingException`,
`EncryptionException`) all reroot under `PdfException`. Most keep their
names; `EncryptionException` becomes `PdfEncryptionException`.

No `[Obsolete]` aliases. v2.0.0 is breaking.

### D2 — `OpenAsync` is canonical, sync `Open` is sugar

`PdfDocument.OpenAsync(Stream, EncryptionOptions?, CancellationToken)`
is the real entry point. End-to-end async means every filter decoder
gets an async path.

Synchronous `Open(Stream, ...)` is retained as a thin convenience that
does the `GetAwaiter().GetResult()` dance internally. XML doc comment
on `Open` explicitly warns: "Not supported on WebAssembly — use
`OpenAsync` instead." Keeps existing console-tool and test code working
without a global rewrite.

### D3 — Display-list is the only render pipeline

`PageRasterizer` (current direct content-stream interpreter) is
internally rewritten to consume `PageDisplayList`. There is exactly one
content-stream-to-render-ops pipeline. Output adapters (existing PNG/BMP
rasterizer, new SVG renderer, future WPF renderer) all consume the
display list.

Costs slightly more work in R1 but means every rendering bug has
exactly one place to live.

---

## PR sequence

Three PRs, all merging into `main`. v2.0.0 tag goes on the last commit
after R3 merges.

### PR R1 — Foundations → v2.0.0-preview1 (no tag)

**Branch:** `feat/v2-foundations`

Lands the three load-bearing changes from D1/D2/D3.

- **D1 work:** new exception hierarchy across all 22 src modules.
  Every throw site updated, every test assertion updated.
- **D2 work:** `PdfDocument.OpenAsync` with full async filter decoder
  paths. `Open` retained as sync sugar. DEFLATE, ASCIIHex, ASCII85,
  LZW, RunLength all get async variants.
- **D3 work:** `Chuvadi.Pdf.Rendering` gains a new `DisplayList`
  subdirectory with `PageDisplayList`, `RenderOp` abstract base, and
  concretes `PathOp`, `TextOp`, `ImageOp`, `ClipOp`, `TransformOp`,
  `OpacityOp`, `BlendModeOp`. `PdfColor` neutral color type carrying
  source color space + components.
- `Page.BuildDisplayList()` method on `PdfPage`. Pure, deterministic.
- `PageRasterizer` rewritten as a `PageDisplayList` consumer.
- Standard 14 PDF fonts (Helvetica, Times, Courier × Regular/Bold/Italic
  variants + Symbol + ZapfDingbats) bundled as embedded resource with
  pre-computed glyph outlines extracted at build time from open-source
  metric-compatible substitutes (Liberation / URW).
- `Font.GetGlyphPath(int glyphIndex)` public on the font type.
- Display-list snapshot tests against a representative pages corpus.
- All 752 existing tests still pass (with assertion updates for D1).

**Acceptance:** All existing PNG/BMP rasterizer output is
byte-for-byte unchanged (the rewrite is internal). New display-list
snapshot tests pass. WASM target deferred to R2 — R1 is desktop-only.

### PR R2 — SVG renderer + WASM CI → v2.0.0-preview2 (no tag)

**Branch:** `feat/v2-svg-renderer`

The visible payoff PR.

- New module `Chuvadi.Pdf.Rendering.Svg`:
  - `SvgRenderer.RenderPage(Page, Stream, SvgRenderOptions)`
  - `SvgRenderer.RenderThumbnail(Page, Stream, int maxDimension)`
  - `SvgRenderOptions` with `Scale`, `FontEmbedding`,
    `IncludeTextLayer`, `DeterministicOutput`
  - `FontEmbedding` enum: `GlyphPaths` (default), `Woff2DataUri`
- `RenderOp` → SVG mappings per app spec §1.2.
- Deterministic byte-for-byte output required (snapshot tests).
- `Font.ToTtf()` returns `byte[]`. WOFF2 packer evaluated: if Brotli
  encoder integrates cleanly in R2 scope, ship `Font.ToWoff2()`;
  otherwise R2.1.
- `ColorConversion.CmykToSrgb` public static class with span overload
  and per-pixel overload. Math approximation only; ICC accurate
  conversion is v2.1+.
- PNG encoder publicly exposed (already internal in `Chuvadi.Pdf.Images`
  per v1.0.0; needs API review).
- **WASM CI target:**
  - `dotnet workload install wasm-tools`
  - Build `Chuvadi.Pdf`, `Chuvadi.Pdf.Rendering`,
    `Chuvadi.Pdf.Rendering.Svg`, `Chuvadi.Pdf.Documents` for net10.0
    with WASM/AOT enabled
  - Fail on linker warnings about missing methods, unsupported
    reflection, or `System.Drawing.Common` references
  - Smoke test 1: open sample PDF from `MemoryStream`, build display
    list, render to SVG, assert non-empty output (merge/split smokes
    deferred to v2.1+ PRs that land those features)
- End-to-end test: PDF stream → SVG bytes, snapshot-asserted.

**Acceptance:** SVG snapshot tests pass deterministically across
Ubuntu/Windows/macOS. WASM build green with zero linker warnings.

### PR R3 — Reading API completeness → tag v2.0.0

**Branch:** `feat/v2-reading-api`

Closes the reader spec.

- `Page.GetTextRuns()` returning `IReadOnlyList<TextRun>`.
- `TextRun` with `Unicode`, `BoundingBox`, `Glyphs`, `Direction`,
  `ReadingOrderIndex`.
- `GlyphPosition` struct with `X`, `Y` (baseline), `Advance`,
  `Unicode` (mapped chars, may be 0/1/multi for ligatures).
- Reading-order detection: baseline-grouped lines, sort top-to-bottom,
  sort runs within line by x-position. Multi-column / complex flow is
  v2.1+ work — documented as a limitation in XML doc comments.
- `PdfDocument.SearchAsync(string, SearchOptions, CancellationToken)`
  returning `IAsyncEnumerable<SearchMatch>`.
- `SearchOptions` with `CaseSensitive`, `WholeWord`, `PageRangeStart`,
  `PageRangeEnd`.
- `SearchMatch` with `PageNumber`, `CharacterOffset`, `Length`,
  `BoundingBoxes`.
- `PdfDocument.Info` returning `DocumentInfo` aggregate per spec §1.10
  (Title, Author, Subject, Keywords, Creator, Producer, dates, version,
  page count, file size, encryption info).
- `EncryptionInfo` and `[Flags] PdfPermissions` types.
- **Annotation hierarchy refactor:**
  - Existing `Markup` becomes abstract; concrete types split out:
    `HighlightAnnotation`, `UnderlineAnnotation`, `StrikeoutAnnotation`
    (all carrying `QuadPoints`)
  - `TextNoteAnnotation` (existing `Text` renamed; carries `IconName`)
  - `FreeTextAnnotation` (existing; carries `Text`, `FontName`,
    `FontSize`)
  - `InkAnnotation` (existing; carries `Strokes` as `IReadOnlyList<IReadOnlyList<Point>>`)
  - `ShapeAnnotation` with `ShapeKind` enum (Rect, Ellipse, Line, Polygon)
  - Common base `PdfAnnotation` with `BoundingBox`, `Title`, `Contents`,
    `ModifiedDate`, `Color`
- `Page.GetAnnotations()` returns `IReadOnlyList<PdfAnnotation>`.
- Annotations rendered into the display list automatically by
  `BuildDisplayList()` — they appear in SVG output without additional
  caller work.
- `OutlineNode` / `OutlineDestination` API verified against spec §1.9;
  adjustments as needed.
- Integration tests: multi-column samples, mixed-script (CJK + Latin)
  samples, complex annotation interactions.

**Acceptance:** All reader spec sections 1.1–1.11 + CI requirements
closed. Tag v2.0.0 cut. Reply to app team: "Library v2.0.0 is shipped;
proceed with reader app development."

---

## Answers to app-team open questions

These are the six at the end of the spec, answered with library-team
certainty so the app team can scope their own work:

1. **Glyph-level positions in extraction?** Yes. The glyph extractor
   (third text strategy, shipped v1.0.0) produces glyph-level data
   internally. R3 exposes it via `TextRun` / `GlyphPosition`. §1.4 is
   API design work, not new extraction infrastructure.

2. **Pure-C# TrueType/CFF outline parser?** Yes. Present in
   `Chuvadi.Pdf.Fonts.Rendering` since v1.0.0. R1 exposes it as
   `Font.GetGlyphPath(int glyphIndex)`. §1.3 is API exposure, not new
   parsing work.

3. **`PdfDocument.Open` truly async end-to-end?** No — currently
   synchronous all the way down. R1 introduces `OpenAsync` plus async
   filter decoder paths. The current sync `Open` is retained as a thin
   convenience wrapper but explicitly documented as not WASM-supported.

4. **ICC profile parser?** No. CMYK→sRGB is math-only for v2.0.0. ICC
   accurate conversion is on the v2.1+ roadmap. Spec explicitly accepts
   math approximation for v1.

5. **Xref read/write + object reference rewriting primitives?** Yes —
   fully mature since v1.0.0. `PdfWriter`, `WriteIncrementalUpdate`,
   `WriteLinearized` all exist. The foundation for split/merge
   (v2.1.0 / v2.2.0) is already in place.

6. **PdfObject graph with PdfDictionary / PdfArray / PdfStream /
   PdfReference?** Yes — `Chuvadi.Pdf.Primitives` has the complete
   primitive hierarchy. Cloning and rewriting are well-understood
   operations.

---

## Out of scope for v2.0.0

These belong to later v2.x releases or remain on the longer-term
backlog.

**v2.1.0 (merge):** `DocumentMerger` queue API per spec §3, resource
deduplication, outline merge strategies, form field namespacing, merge
performance benchmarks.

**v2.2.0 (split):** `DocumentSplitter` with all four split modes per
spec §2, `PageRangeParser` utility, outline re-anchoring, selective
annotation copying.

**Backlog (no version assigned):**

- RFC 7932 §7 + §8 Brotli gap close (was v1.11.0; now post-v2.0.0)
- TrueTypeLoader bounds checks (PR 2.1 from the fuzz harness backlog)
- PdfName.Intern / FromRawBytes throw tightening (PR 2.1)
- Font WOFF2 packer (if not folded into R2)
- ICC profile parser for accurate CMYK conversion
- Digital signature signing (verification shipped v1.7.0)
- Linearization viewer compatibility verification end-to-end
- Multi-column / complex-flow reading-order detection
- JPEG encoder (PNG is sufficient for v2.0.0)

The PR 2.1 fuzz-harness items (TrueType bounds, PdfName tightening) are
*small* and will be folded into whichever v2.0.0 PR most naturally
contains the surrounding code — likely R1 since it touches the parser
and tokenizer extensively. They are not deferred indefinitely; they
ride along.

---

## Risks and unknowns

- **OpenAsync filter decoder scope.** DEFLATE has `DeflateStream.DecompressAsync`
  out of the box. ASCIIHex / ASCII85 / RunLength / LZW are custom
  implementations and may need careful audit for async correctness
  (especially LZW's bit-packed state machine). Possible R1 schedule
  pressure if any decoder turns out to be deeply sync-coupled.
- **Standard 14 font outline bundling.** Build-time outline extraction
  from Liberation / URW requires a tooling step. Need to decide:
  ship the outline data in the repo or extract during CI? If in repo,
  size impact ~1–2 MB. If during CI, build-time dependency on the
  font files.
- **WOFF2 packer feasibility for R2.** Brotli encoder exists
  (`Chuvadi.Pdf.Fonts.Woff2`, v1.8.0–v1.9.0), but it was wired for
  Brotli output, not for the WOFF2 container format. Container
  encoding may be a meaningful chunk of work. Punt to R2.1 if it
  threatens R2 scope.
- **AOT linker warnings.** The current codebase has not been audited
  against `IlcDisableReflection` or AOT-strict linker passes. Probable
  that some reflection-using corner exists somewhere (XML doc parsing
  for tests? `Activator.CreateInstance` in skill-creator-style code?).
  R2 surfaces this; cleanup may take a follow-up PR.

---

## Process

Same project conventions as v1.x:

- Branch per PR, merge via PR with branch protection enforcing
  build matrix + style + docs-up-to-date
- Large PRs with iteration on build errors are expected and fine
- CLAUDE.md hard rules apply: CA1062, IDE0270, IDE0005, IDE0008,
  one-property-per-line in object initializers
- Delete `Class1.cs` / `UnitTest1.cs` after every `dotnet new` scaffold
- Regenerate API docs (`tools/gen_api_docs.py`) as part of every PR
- This plan doc updates with each PR landing: check items off, note
  surprises, log decisions
