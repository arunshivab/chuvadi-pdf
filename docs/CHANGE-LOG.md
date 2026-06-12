# CHANGE-LOG.md — Chuvadi Decision History

> Append-only. Old entries are never rewritten.
> New entries supersede old ones where they conflict.
> Format: A[NN] — entries are numbered sequentially.

---

## A01 — Project Identity

**Date:** 2025-05-09
**Scope:** Project name, purpose, and audience
**Rationale:** Name carries meaning; Tamil origin fits the document-library purpose.

Chuvadi (சுவடி) — Tamil for palm-leaf manuscript / written scroll.
The library is a general-purpose PDF library for the .NET ecosystem,
not a hospital-internal tool. It is intended to be a free, open-source
replacement for PdfSharp and a AGPL-free alternative to iText.

**Files affected:** README.md, all csproj PackageDescription fields.

---

## A02 — License

**Date:** 2025-05-09
**Scope:** Open-source license choice
**Rationale:** Apache 2.0 over MIT because the patent termination clause
matters in the PDF space (historical patent activity around JBIG2, JPEG 2000).
MIT has no patent grant. Apache 2.0 is permissive, royalty-free, commercial-friendly,
and does not require derivative works to be open-sourced (unlike AGPL/GPL).

**Decision:** Apache 2.0. No dual licensing. No commercial tier. Free for all use.
**Files affected:** LICENSE, all csproj PackageLicenseExpression fields.

---

## A03 — Runtime Dependency Policy

**Date:** 2025-05-09
**Scope:** NuGet dependency rules for production vs test code
**Rationale:** Supply chain safety, auditability, air-gap compatibility,
and institutional trust for hospital deployments.

RULE: src/ projects have ZERO NuGet dependencies.
      Every line of production code is owned by this repository.
RULE: tests/ projects may use xUnit, FluentAssertions, FsCheck, BenchmarkDotNet.
      These never ship to production.
RULE: tools/ (CLI) may reference only src/ projects.

Build-time tooling (PyTorch for model training if OCR is later added,
compilers, SDK tools) is explicitly excluded from this rule —
it does not run in production.

**Files affected:** Directory.Packages.props, all csproj files.

---

## A04 — Target Framework and Language Version

**Date:** 2025-05-09
**Scope:** .NET and C# version targeting
**Rationale:** .NET 10 is the current LTS-track release at project start.
Latest C# gives access to primary constructors, collection expressions,
ref struct improvements, and modern span APIs critical for PDF byte processing.

**Decision:** net10.0, LangVersion latest.
             global.json pins SDK to 10.0.203 (installed version).
**Files affected:** Directory.Build.props, global.json.

---

## A05 — Code Quality Enforcement

**Date:** 2025-05-09
**Scope:** Compiler and analyzer settings
**Rationale:** A foundational library that will be used by other developers
must ship with maximum quality. Warnings that are silently ignored in
application code become bugs in library code because callers depend on
correct behavior of every public member.

Locked settings:
- `<Nullable>enable</Nullable>` — all nullability expressed in types
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — no warning is ignorable
- `<ImplicitUsings>disable</ImplicitUsings>` — every dependency explicit
- `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` — .editorconfig enforced at build
- `<GenerateDocumentationFile>true</GenerateDocumentationFile>` — XML docs required

Style rules enforced as errors:
- IDE0021: Block body constructors (no expression-body constructors)
- IDE0011: Braces required on all control flow (if, for, foreach, while)
- IDE0005: No unnecessary using directives
- IDE0052: No unread private members
- CA1062: Validate public method parameters before use

**Files affected:** Directory.Build.props, .editorconfig.

---

## A06 — Naming and File Structure Conventions

**Date:** 2025-05-09
**Scope:** C# naming, file layout, namespace style
**Rationale:** Consistency at library scale — consumers read source and
expect predictable structure.

Locked conventions:
- One public type per file. File named after the type.
- File-scoped namespaces (`namespace Chuvadi.Pdf.Primitives;`)
- PascalCase: types, public members, constants
- _camelCase: private fields
- camelCase: parameters, local variables
- IPascalCase: interfaces
- Private fields prefixed with underscore: `_tokenBytes`, not `tokenBytes`
- No regions
- No `var` for primitive types; `var` acceptable when type is obvious from RHS

**Files affected:** .editorconfig (enforces many of these as errors).

---

## A07 — Solution Structure and Project Layering

**Date:** 2025-05-09
**Scope:** Multi-project solution layout and dependency direction
**Rationale:** Each project has exactly one responsibility. Dependency
direction is strictly bottom-up. No circular dependencies. Each layer
can be tested independently.

Layer order (bottom to top):
```
Chuvadi.Pdf.Primitives     — tokens, primitive types, tokenizer
Chuvadi.Pdf.Filters        — stream filters (DEFLATE, ASCII85, LZW, etc.)
Chuvadi.Pdf.Objects        — object model, object store, xref
Chuvadi.Pdf.IO             — reader, writer, file structure
Chuvadi.Pdf.Documents      — document model, pages, outlines, metadata
Chuvadi.Pdf.Fonts          — font parsing, glyph-to-Unicode mapping
Chuvadi.Pdf.Content        — content stream parser, graphics state
Chuvadi.Pdf.Text           — text extraction (3 strategies)
Chuvadi.Pdf.Operations     — high-level operations: merge, split, etc.
```

Test project per src project. Integration tests in Chuvadi.Pdf.Integration.Tests.
CLI tool in tools/Chuvadi.Pdf.Cli.

**Files affected:** Chuvadi.slnx, all csproj files.

---

## A08 — Phase Scope Boundaries

**Date:** 2025-05-09
**Scope:** What is in scope for Phase 1, 2, and 3
**Rationale:** Time-boxed phases prevent scope creep and keep the first
release shipable. Each phase is independently valuable.

**Phase 1 — Core PDF library (current):**
- PDF object model parser (xref classic + stream, objects, indirect refs)
- DEFLATE and all standard non-encryption filters
- PDF writer (full rewrite + incremental update)
- Page-level operations: merge, split, delete, rotate, reorder
- Born-digital text extraction (3 strategies: operator, layout, glyph)
- Font handling for text extraction (TrueType, CFF, Type 1, CMaps)
- Document model: metadata, outlines, page labels, hyperlinks
- Annotation reading (not creation)
- Form field reading (not filling)
- Resource inventory API
- CLI tool: info, merge, split, delete, rotate, extract-text

**Phase 2 — Images and editing:**
- PNG, JPEG, TIFF, BMP decoders/encoders
- Image extraction from PDF
- Image to PDF embedding
- PDF to image rendering (full rasterizer — largest ticket in Phase 2)
- True content redaction (PHI-safe)
- Watermarking
- Annotation creation and editing
- Form field filling and creation

**Phase 3 — Security and compliance:**
- Encryption: RC4-40, RC4-128, AES-128, AES-256 (all revisions)
- Digital signatures (PKCS#7, sign and verify)
- JavaScript preservation across operations
- PDF/A conformance (archival format)
- Full documentation, samples, migration guides

**Files affected:** All src projects scoped accordingly.

---

## A09 — Distribution and Versioning

**Date:** 2025-05-09
**Scope:** How Chuvadi reaches users
**Rationale:** NuGet.org is the standard .NET package channel. GitHub
Releases for binaries and changelog. Semantic versioning for trust.

- Primary channel: NuGet.org (dotnet add package Chuvadi.Pdf.Operations)
- Pre-release builds: GitHub Packages (automated on every push to main)
- Stable releases: NuGet.org (automated on every git tag vX.Y.Z)
- Versioning: Semantic Versioning strictly
  - 0.x.y during active Phase 1 development (API not yet stable)
  - 1.0.0 on first stable Phase 1 release (API stability commitment begins)
  - Breaking changes only in major version bumps

**Files affected:** .github/workflows/ci.yml, .github/workflows/release.yml,
                    Directory.Build.props (version defaults).

---

## A10 — Working Agreement Adoption

**Date:** 2025-05-09
**Scope:** Workflow discipline for this project
**Rationale:** Imported from prior project CLAUDE.md. Adapted for Chuvadi context.

Key rules adopted:
- Complete files only. Never snippets, never diffs, never "insert here."
- Pre-code checklist before every generation batch (WHAT / SPEC / DESIGN / DEPLOY).
- Post-code checklist after every generation batch.
- Every new file registered in deploy.ps1 in the same batch.
- Build must be green before proceeding to next module.
- File header format from next delivery onwards:
  ```csharp
  // SPEC:  PDF 32000-1:2008 §X.Y — Section name
  // PHASE: Phase N — Module name
  // [One-line summary]
  ```
- CHANGE-LOG entries for every locked decision.
- Deploy folder: %USERPROFILE%\Downloads\chuvadi\
- Deploy script: .\deploy.ps1 in repo root (CRLF, ASCII-safe for Windows PS 5.1)

**Files affected:** CLAUDE.md (reference), CHANGE-LOG.md (this file),
                    deploy.ps1 (running), docs/ (this directory).

---

## A11 — Build Progress Checkpoint

**Date:** 2025-05-09
**Scope:** Current state of the codebase
**Rationale:** Session continuity. Future sessions read this to know where we are.

Completed and green:
- Solution scaffold: all 20 projects, build infrastructure, CI workflows
- Chuvadi.Pdf.Primitives: all 12 primitive types (PdfObjectId, PdfPrimitive,
  PdfNull, PdfBoolean, PdfInteger, PdfReal, PdfName, PdfString, PdfArray,
  PdfDictionary, PdfStream, PdfReference)
- PdfTokenType, PdfToken, PdfTokenizer, PdfTokenizerException
- 67 tests passing, 0 failures

In progress (deployed, awaiting build confirmation):
- PdfTokenizer tests (PdfTokenizerTests.cs)
- Expected: additional tokenizer tests to pass on top of 67

Next up:
- Chuvadi.Pdf.Filters: DEFLATE (RFC 1951) — the largest single filter
- Then: ASCII85, ASCIIHex, LZW, RunLength

**Files affected:** All src/Chuvadi.Pdf.Primitives files.

---

---

## A12 — Default Font: LiPi Sans

**Date:** 2025-05-10
**Scope:** Default font for all Chuvadi-adjacent applications and PDF output

LiPi Sans v1.0 is the default font family for all projects in the LiPi
ecosystem, including Chuvadi and any companion applications.

**Font family:** LiPi Sans
**License:** SIL OFL 1.1 (Inter + Noto Sans as base families)
**Coverage:** Latin (English/European), Devanagari, Bengali, Tamil, Telugu,
             Malayalam, Kannada, Gujarati, Gurmukhi, Odia
**Format stored:** woff2 (web fonts) in assets/fonts/lipi-sans/
**Variable font:** Yes — single file covers weight axis 100-900

**Application by phase:**

Phase 1 — Chuvadi.Pdf.Fonts:
  When implementing font embedding for text extraction and document writing,
  LiPi Sans (via its underlying Inter + Noto Sans TTF sources) is the
  reference family. TTF/OTF versions must be obtained separately from the
  Inter and Noto Sans repositories for PDF embedding.
  woff2 files in assets/ serve as the design reference and for Unicode
  range routing logic.

Phase 2 — PDF rendering (image output):
  When rendering PDFs to images, LiPi Sans is the fallback/default font
  for glyph rendering when the PDF's embedded fonts are unavailable.

CLI tool and any HTML output:
  The woff2 files in assets/fonts/lipi-sans/ are ready to use directly.
  Link lipi-sans.css and set font-family: var(--lipi-sans).

**Files stored:** assets/fonts/lipi-sans/ (11 woff2 files + CSS + LICENSE)
**NOT stored:** TTF/OTF versions — obtain from Inter and Noto Sans repos
               when Chuvadi.Pdf.Fonts implementation begins.


---

## A13 — Phase 2 Scope and Architecture

**Date:** 2025-05-10
**Scope:** Phase 2 build plan, dependency policy, rasterizer decision

**Zero-dependency rule extended to Phase 2.**
No SkiaSharp. No external rendering libraries. Every pixel produced by Chuvadi
is produced by code owned by this repository. This is a deliberate product
decision: a hospital-grade, auditable, air-gap-deployable library must own
its full rendering stack.

**Phase 2 module order:**
1. Chuvadi.Pdf.Graphics — 2D geometry, paths, colour spaces (shared foundation)
2. Chuvadi.Pdf.Images — JPEG, PNG, TIFF decoders/encoders, image extraction
3. Chuvadi.Pdf.Fonts.Rendering — TrueType/OTF glyph outline extraction
4. Chuvadi.Pdf.Rendering — Page rasterizer (page → pixel buffer → PNG/BMP)
5. Chuvadi.Pdf.Watermark — Text and image watermarking
6. Chuvadi.Pdf.Redaction — PHI-safe content removal (uses incremental writer)
7. Chuvadi.Pdf.Forms — AcroForm read and fill
8. Chuvadi.Pdf.Annotations — Annotation read and create
9. CLI expansion — info, merge, split, render, redact, extract-text commands

**Deferred Phase 1 items folded into Phase 2:**
- Outlines/bookmarks → with Step 7 (forms/document model)
- Glyph extractor (3rd text strategy) → with Step 3 (font rendering)
- Incremental writer → with Step 6 (redaction requires it)

**Target:** General-purpose PDF library. Hospital/clinical capabilities
(PHI redaction, audit-safe rendering, air-gap deployment) built in as
first-class features, not afterthoughts.

**Version milestone:** 1.0.0 after Phase 2 rendering is stable.
                       Phase 1 = 0.9.x pre-release.


---

## A14 — Phase 2 Completion: Rasterization Stack Delivered

**Date:** 2026-05-11
**Scope:** Modules 1–4 of Phase 2 (rendering)

Four foundational modules shipped, every pixel owned by the repository:

- **Chuvadi.Pdf.Graphics** — Vector geometry, paths (with adaptive de Casteljau flattening), 2D affine transforms, colour spaces (Gray/RGB/CMYK), pixel buffer with Porter-Duff blending.
- **Chuvadi.Pdf.Images** — BMP, PNG (all filter types, all colour types, 1-16 bpp), JPEG (baseline DCT with AAN IDCT). Adler32 promoted to public.
- **Chuvadi.Pdf.Fonts.Rendering** — TrueType/OTF table parser (head, hhea, maxp, loca, glyf, hmtx, cmap-format-4); quadratic-Bezier→cubic conversion; one-level composite glyph resolution; glyph caching.
- **Chuvadi.Pdf.Rendering** — Scanline rasterizer with edge-table fill (both fill rules), stroke expander (butt/square caps), full content-stream interpreter via `PdfTokenizer`. Standard 14 PDF fonts plus embedded TTF support.

**Files affected:** `src/Chuvadi.Pdf.Graphics/**`, `src/Chuvadi.Pdf.Images/**`,
`src/Chuvadi.Pdf.Fonts.Rendering/**`, `src/Chuvadi.Pdf.Rendering/**`.

---

## A15 — PHI-Safe Redaction Pattern

**Date:** 2026-05-11
**Scope:** Definition of "true redaction" in Chuvadi

Visual cover-up is not redaction. Drawing a black rectangle over text leaves
the underlying bytes in the content stream; Ctrl+A + copy recovers it. Chuvadi
defines redaction as **byte-level removal**:

1. Re-tokenise the content stream and track graphics + text state
   (CTM stack, text matrix, font size, text origin).
2. For each `Tj` / `TJ` / `'` / `"` operator, compute its device-space text
   box. If it intersects ANY redaction rectangle, drop the operand AND the
   operator from the rewritten stream.
3. **TJ conservative rule:** if any string in a TJ array falls inside a
   redaction rect, drop the entire array.
4. Track the original content-stream object IDs and exclude them from
   the output object table — otherwise direct-object retrieval (`5 0 R`)
   recovers the text even after the stream is rewritten.
5. Append an overlay content stream drawing opaque rectangles at the
   redaction positions; replace page `/Contents` with
   `[redactedStream, overlayStream]`.

The PHI guarantee: the redacted text is byte-by-byte absent from the
output PDF, both at the operator level AND the indirect-object level.
Tests grep the output bytes for the redacted string and fail if it appears.

**Files affected:** `src/Chuvadi.Pdf.Redaction/Redactor.cs`,
`tests/Chuvadi.Pdf.Redaction.Tests/RedactionTests.cs`.

---

## A16 — PdfObjectStore Is Lazy: PreloadAllObjects Required for Rewrites

**Date:** 2026-05-11
**Scope:** Coding rule for any module that rewrites the object graph

`PdfObjectStore.Objects` returns `_objects.Values` — a snapshot of what has
already been resolved, NOT the full object graph. When `PdfDocument.Open`
runs, only the trailer and catalog are eagerly loaded. Pages, content
streams, resources, and fonts are resolved on first access.

This caused three test failures in Redactor before being identified.
Any module that iterates `document.Objects.Objects` to write a new PDF
MUST first call a `PreloadAllObjects` helper that walks the page graph
recursively, calling `Resolve` on every reference to populate the cache.

Modules that follow this pattern:
- `Chuvadi.Pdf.Redaction.Redactor` — `PreloadAllObjects(document)`
- `Chuvadi.Pdf.Forms.FormFiller` — `PreloadAllObjects(document)` plus AcroForm tree walk
- `Chuvadi.Pdf.Watermark.WatermarkStamper` — implicit (only modifies one page)

**Files affected:** `Redactor.cs`, `FormFiller.cs`. Pattern documented in CLAUDE.md.

---

## A17 — Phase 2 Completion: Forms, CLI, 1.0 Tag

**Date:** 2026-05-11
**Scope:** Final Phase 2 deliverables, version 1.0.0

- **Chuvadi.Pdf.Watermark** — Text and image watermarks via appended content
  streams + ExtGState opacity. Standard PDF fonts only (no embedding).
- **Chuvadi.Pdf.Redaction** — True PHI-safe rectangle redaction (see A15).
- **Chuvadi.Pdf.Forms** — AcroForm read (FullyQualifiedName, type, value,
  object ID) and fill (sets `/V`, button `/AS`, AcroForm `/NeedAppearances`).
  Document outlines (bookmarks) read in same module.
- **Chuvadi.Pdf.Cli** — 17 verbs total. User-facing: info, render, watermark,
  redact, form-fill, extract-text, outlines, merge, split, delete, rotate.
  Debug: tokenize, dump-objects, parse-content, decode-stream, inspect-xref,
  validate-fonts. Mixed verb + flag style (`chuvadi watermark in.pdf --output out.pdf --text DRAFT`).

**Deferred to Phase 1.1 (see BACKLOG.md):**
- Annotations (Phase 2 Step 8 — descoped after Forms covered the outline
  half of the original scope and AcroForm widget annotations).
- Pattern-based redaction (SSN regex, email patterns) — Phase 2 ships
  rectangle-only.
- Form XObjects and inline images inside redaction targets.

**Version milestone reached: 1.0.0** (Phase 1 was 0.9.x pre-release).

**Test totals at tag:** ~564 tests across 19 test projects, 0 failures.

---

## A18 — Brotli LZ77 Multi-Command Emission

**Date:** 2026-05-19
**Scope:** `Chuvadi.Pdf.Fonts.Woff2` — `BrotliCompressedEmitter` and `BrotliEncoder`
**Rationale:** Stage 3's single-command emission produced ~50% compression ratios
even on highly-repetitive data because no LZ77 back-references were used. The LZ77
matcher in `BrotliCommandStream` was complete but unused.

This entry wires `BrotliCommandStream.Encode()` output directly into
`BrotliCompressedEmitter.Emit()`. Per-meta-block input cap raised from 64 KiB to
16 MiB (MNIBBLES=6); larger inputs split across multiple meta-blocks with only the
last carrying `ISLAST=1`.

The emitter is now infallible for non-empty inputs. `TryEmit` (returning `bool`)
is replaced by `Emit` (returning `void`). The encoder still speculatively produces
both compressed and stored variants and picks whichever is smaller, so tiny inputs
where Huffman declaration overhead exceeds savings still fall back to stored framing.

**Compression results on sandbox** (against `System.IO.Compression.BrotliStream`
Optimal):

| Input             | Pre-PR (v1.8.0) | Post-PR (v1.9.0) | .NET Optimal |
|-------------------|-----------------|------------------|--------------|
| Lorem ipsum 1KB   | 52.9%           | 6.2%             | 5.3%         |
| English text 5KB  | 100.1%          | 1.8%             | 1.1%         |
| Repetitive 1KB    | 26.4%           | 2.1%             | 1.5%         |
| Moderate 1KB      | 57.1%           | 3.9%             | 2.7%         |

Within ~1% of `.NET Optimal` on real data. Remaining gap is RFC §7 (literal
context modeling) and §8 (static dictionary), planned for v1.11.0.

**Trade-off:** the speculative dual-emission costs ~2x encode time on inputs
where compression wins. For latency-sensitive callers this could be optimised
later by adding a heuristic to skip the stored path when input is obviously
compressible (e.g. low entropy estimate, or output already > 0.95 × input
mid-emission). Kept simple for now since correctness is the v1.9.0 goal.

**Files affected:** `src/Chuvadi.Pdf.Fonts.Woff2/BrotliCompressedEmitter.cs`,
`src/Chuvadi.Pdf.Fonts.Woff2/BrotliEncoder.cs`,
`tests/Chuvadi.Pdf.Fonts.Woff2.Tests/BrotliLz77Tests.cs`,
`benchmarks/Chuvadi.Benchmarks/**`.

---

## A19 — Benchmark Harness Introduced

**Date:** 2026-05-19
**Scope:** New `benchmarks/Chuvadi.Benchmarks` project
**Rationale:** Phase 2.2 stage 4 needs measurable compression numbers to know
whether the LZ77 wiring achieves the design goal. More broadly, the project is
at ~640 tests across 22 modules; performance regressions are starting to be a
real risk without a benchmark guardrail.

Project uses BenchmarkDotNet 0.14.0 (already in `Directory.Packages.props` from
an earlier scaffold). Three benchmark classes:

- **`BrotliRatioBench`** — output size vs `BrotliStream` Optimal/Fastest across
  6 input scenarios (Lorem ipsum, English prose, repetitive, moderate, SFNT-like
  binary, random incompressible).
- **`BrotliThroughputBench`** — encode time on a subset of the same scenarios.
- **`ParserOpenBench`** — `PdfDocument.Open` time on synthetic single-page and
  20-page PDFs generated via `PdfDocumentBuilder` so the benchmark is
  self-contained.

Solution layout: `benchmarks/` parallel to `src/`, `tests/`, and `examples/`.
`IsPackable=false` and `ServerGarbageCollection=true` set on the csproj.

Not wired into CI on every PR (BenchmarkDotNet's harness is slow). Future
follow-up: a scheduled weekly CI job that diffs against a stored baseline and
opens an issue on regression.

**Files affected:** `benchmarks/Chuvadi.Benchmarks/**`, solution file.

---

## A20 — TrueType Bytecode Hinting: Architecture and Staging

**Date:** 2026-06-04
**Scope:** `Chuvadi.Pdf.Fonts.Rendering` — TrueType instruction interpreter
**Rationale:** Small text from embedded TrueType fonts is blurry without
grid-fitting. A spec-complete bytecode hinting interpreter is the fix, but the
existing glyph pipeline cubicizes immediately and discards the raw point data
and instruction bytecode that hinting requires.

Key findings and decisions:

- `TrueTypeLoader.BuildSimpleGlyph` consumes raw points inline and emits a
  cubic `Path`, reads `instructionLength` only to skip the bytecode, and never
  parses `cvt `/`fpgm`/`prep`. Hinting therefore cannot be a post-pass on
  `GlyphOutline`; it needs a parallel raw-glyph pipeline inside the loader:
  parse → raw point set (not cubicized) → scale to 26.6 → run prep per size →
  run glyph instructions → then cubicize the hinted points.

- **Decision A — Fixed-point math (26.6 / F2Dot14), not double.** Spec rounding
  behaviour is defined in fixed-point and hint programs assume it; matching
  FreeType/the spec requires fixed-point throughout.

- **Decision B — `RenderOptions.Hinting` ships default OFF through all stages**,
  flipped on only after Stage 7 visual confirmation, so partial interpreters
  never touch real output.

- **Staging:** seven sequential PRs, each building clean. Stage 1 (v2.4.0) lays
  the foundation — `cvt `/`fpgm`/`prep` parsing, the internal `RawGlyph` model
  in the new `Chuvadi.Pdf.Fonts.Rendering.Hinting` sub-namespace, an internal
  non-cubicizing `BuildRawGlyph`, and the inert `Hinting` flag — with render
  output unchanged.

**Files affected:** `src/Chuvadi.Pdf.Fonts.Rendering/TrueTypeLoader.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/Hinting/RawGlyph.cs`,
`src/Chuvadi.Pdf.Rendering/RenderOptions.cs`.

---

## A21 — TrueType Hinting Stage 2: VM Skeleton and First InternalsVisibleTo

**Date:** 2026-06-04
**Scope:** `Chuvadi.Pdf.Fonts.Rendering` — TrueType instruction interpreter
**Rationale:** Stage 2 of the hinting plan (A20) builds the execution engine
that later stages plug operators into: operand stack, storage area, function
and instruction definitions, and the program loop. It can run the font program
(`fpgm`) to register functions but performs no grid-fitting, so render output
is unchanged and `RenderOptions.Hinting` stays off.

Key decisions:

- **Operand stack is raw `int32`.** The TrueType stack holds 32-bit values
  interpreted either as integers or as F26Dot6 fixed point depending on the
  operator. Stage 2 keeps them raw; the fixed-point operations (round,
  projection, multiply/divide) and the decision on how to represent them are
  introduced in Stage 3, where they are first needed.

- **Length-aware body scanning.** FDEF and IDEF store their body by scanning to
  `ENDF` instruction by instruction, so variable-length push data is skipped
  rather than misread as an `ENDF` opcode. Opcodes not yet implemented are
  length-aware no-ops while the interpreter is inert.

- **First `InternalsVisibleTo` in the repository.** The interpreter is
  `internal` (matching `Type2Interpreter`) and has no public surface until the
  final stage, so it is unit-tested via a standalone `InternalsVisibleTo.cs`
  attribute file granting access to `Chuvadi.Pdf.Fonts.Rendering.Tests`. Prior
  tests exercised only public surfaces; this is the codebase's first use of IVT.

**Files affected:** `src/Chuvadi.Pdf.Fonts.Rendering/Hinting/HintingInterpreter.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/Hinting/GraphicsState.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/Hinting/HintingLimits.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/Hinting/RoundState.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/InternalsVisibleTo.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/TrueTypeLoader.cs`.

---
---

## A22 — TrueType Hinting Stage 3: Fixed-Point and Rounding

**Date:** 2026-06-04
**Scope:** `Chuvadi.Pdf.Fonts.Rendering` — TrueType instruction interpreter
**Rationale:** Stage 3 of the hinting plan (A20) realises Decision A's
fixed-point commitment and adds the rounding machinery that the movement
operators in Stage 4 consume. It remains inert: `round()` and the new opcodes
have no render-path consumer and `RenderOptions.Hinting` stays off.

Key decisions:

- **Fixed-point helpers as separate types.** `F26Dot6` (64 = 1px) and `F2Dot14`
  (16384 = 1.0) are introduced as the two fixed-point families the spec uses —
  26.6 for distances and coordinates, 2.14 for unit vectors. Multiply and divide
  round half away from zero across all sign combinations and guard
  divide-by-zero, so results are deterministic and match the spec rather than
  drifting with IEEE rounding.

- **`round()` via floor-to-multiple, not a bit mask.** The engine floors to a
  multiple of the round period, which is correct for any period including
  S45ROUND's non-power-of-two diagonal grid, and reduces to the bit-mask result
  for the power-of-two standard states. SROUND/S45ROUND selector bytes are
  decoded to period/phase/threshold per the super-round specification.

- **Vector setters limited to the axis-aligned forms.** SVTCA/SPVTCA/SFVTCA set
  the projection, dual, and freedom vectors to a coordinate axis. The
  line-based setters (SPVTL/SFVTL/SDPVTL) and running `prep` are deferred to
  Stage 4, where the point, zone, ppem, and CVT infrastructure they consume is
  built.

- **Versioning: internal-only stages are patch releases.** Stages that add no
  public surface and change no output ship as patches (Stage 3 = v2.4.2); the
  version becomes a minor only when Stage 7 wires the flag on.

**Files affected:** `src/Chuvadi.Pdf.Fonts.Rendering/Hinting/F26Dot6.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/Hinting/F2Dot14.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/Hinting/HintingInterpreter.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/Hinting/GraphicsState.cs`.

---

## A23 — TrueType Hinting Stage 4: Points, Zones, and Movement

**Date:** 2026-06-04
**Scope:** `Chuvadi.Pdf.Fonts.Rendering` — TrueType instruction interpreter
**Rationale:** Stage 4 of the hinting plan (A20) adds the point model and the
operators that move points: scaling to device space, the twilight and glyph
zones, the Control Value Table, measurement, and absolute and relative movement.
It is the largest stage and the crux of grid-fitting, but remains inert —
`RenderOptions.Hinting` stays off and render output is unchanged.

Key decisions:

- **Sizing API: `PrepareSize` per size, `HintGlyph` per glyph.** `PrepareSize`
  computes the 16.16 font-unit-to-26.6 scale, scales the CVT, allocates the
  twilight zone, and runs `prep` once; `HintGlyph` builds the glyph zone scaled
  to 26.6, runs the glyph program, and returns the fitted zone. The graphics
  state resets per glyph (per the spec) while CVT modifications from `prep`
  persist, so the interpreter stays decoupled from the loader and is unit-tested
  with synthetic programs.

- **General projection/freedom math, not axis-only.** Points move along the
  freedom vector so their projection onto the projection vector changes by a
  given distance, scaled by `F_dot_P` (freedom · projection in 2.14). This
  handles SPVTL/SFVTL/SDPVTL diagonal vectors, not just the axis-aligned cases.

- **Deterministic vector normalization.** SPVTL/SFVTL/SDPVTL normalize a 26.6
  line delta to a 2.14 unit vector using an exact integer square root (a double
  seed corrected by integer steps), honouring Decision A so the result cannot
  diverge across platforms in the low bits.

- **Simplifications deferred to the final stage.** MDRP/MIRP distance-type
  compensation (the de flag bits) is treated as zero, correct for grey
  (anti-aliased) rendering; single-width handling is present but a no-op at the
  default cut-in; MPS approximates point size as ppem (exact only at 72 dpi).
  These are revisited at Stage 7 when the interpreter is wired to real output
  and visually confirmed.

**Files affected:** `src/Chuvadi.Pdf.Fonts.Rendering/Hinting/HintingInterpreter.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/Hinting/Zone.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/Hinting/F26Dot6.cs`.

---

## A24 — TrueType Hinting Stages 5 and 6: Arithmetic, Flow Control, DELTA, and Interpolation

**Date:** 2026-06-05
**Scope:** `Chuvadi.Pdf.Fonts.Rendering` — TrueType instruction interpreter
**Rationale:** Stages 5 and 6 of the hinting plan (A20) add the remaining
opcode families on top of the Stage 4 point and movement model: the
arithmetic/logical/stack tail, storage, flow control, the DELTA exceptions, and
the shift/interpolation operators. They were delivered as one PR because the
groups are small and interdependent (the interpolation operators lean on the
arithmetic and flow-control primitives), and the whole set is unit-testable in
isolation. It remains inert — `RenderOptions.Hinting` stays off and render
output is unchanged.

Key decisions:

- **Flow control lives in the execution loop, not the dispatch table.** IF/ELSE/
  EIF and JMPR/JROT/JROF move the instruction pointer, so they are handled where
  the pointer lives. ELSE/EIF matching and forward skips scan instruction by
  instruction using the same length-aware logic as the Stage 2 FDEF scanner, so
  inline push data is never misread as a control opcode, and nested IFs track
  depth.

- **DELTA decode is explicit per pair.** Each (argument, index) pair splits the
  argument byte into a relative-ppem high nibble and a magnitude-selector low
  nibble; the exception applies only when the active ppem equals
  `DeltaBase + tableBase + relppem`, with the three point-table opcodes carrying
  base offsets 0/16/32. The magnitude maps through the DeltaShift step. The
  pop order (index deeper, argument on top) is the spec reading but is **pinned
  by tests and flagged** for confirmation against real fonts at Stage 7.

- **Full IUP, not a placeholder.** IUP[x]/IUP[y] interpolate each contour's
  untouched points between consecutive touched anchors, with a single touched
  anchor producing a rigid shift and zero anchors leaving the contour untouched.
  Doing it fully now keeps Stage 7 focused on wiring and visual confirmation
  rather than algorithm work.

- **GETINFO answers conservatively.** Only the scaler-version and
  grayscale-rasterizer bits are reported; rotation/stretch/ClearType selectors
  return zero. Real hint programs branch on GETINFO, so a deterministic minimal
  answer keeps inert behaviour predictable until Stage 7 sets the true
  environment.

- **MSIRP deferred.** Its opcode assignment collides with RTDG in the working
  decode table; rather than guess, it is left out of this stage and resolved
  with the Stage 7 wiring. The operand orders for ISECT (b1, b0, a1, a0, point)
  and JROT/JROF (condition then offset) are likewise test-pinned pending
  real-font confirmation.

**Files affected:** `src/Chuvadi.Pdf.Fonts.Rendering/Hinting/HintingInterpreter.cs`,
`tests/Chuvadi.Pdf.Fonts.Rendering.Tests/HintingArithmeticAndInterpolationTests.cs`.

---

## A25 - TrueType Hinting Stage 7: Wiring into the Raster Path, MSIRP/RTDG Fix, and HintingMode.Light Default

**Date:** 2026-06-09
**Scope:** `Chuvadi.Pdf.Fonts.Rendering` (interpreter, loader), `Chuvadi.Pdf.Rendering` (options, rasterizer), `Chuvadi.Pdf.Rendering.DisplayList` (raster builder)
**Rationale:** Stage 7 connects the interpreter built across Stages 1-6 (A20, A24) to real raster output and turns hinting on by default. Doing so against a real embedded-TrueType document (a nine-page Word-generated CV, Identity-H, unitsPerEm 2048) surfaced several latent interpreter faults that inert unit tests could not - most importantly the MSIRP/RTDG collision deferred in A24. Delivered as one PR because the wiring and the fixes it exposes are inseparable: the pipeline cannot be validated without correct opcodes, and the opcode faults are only observable once the pipeline renders.

Key decisions:

- **MSIRP implemented; RTDG moved to its correct opcode.** A24 left MSIRP out because `0x3A` was mapped to RTDG in the working decode table. The correct TrueType assignment is `0x3A`/`0x3B` = MSIRP[0]/MSIRP[1] and `0x3D` = RTDG. Because MSIRP was decoded as a round-state op (which pops nothing), every MSIRP a glyph program issued stranded its two operands on the stack, cascading into corrupted reference points (rp0/rp1/rp2) and visibly broken glyphs - the capital W lost the lower halves of its diagonal strokes. MSIRP now moves a point so its projected distance from rp0 equals a stack-supplied distance, setting rp1 = rp0, rp2 = point, and rp0 = point for the [1] form. RTDG is relabelled `0x3D`. The Stage-3 rounding test that pinned RTDG to `0x3A` was corrected to `0x3D`, and an MSIRP movement test was added.

- **`fpgm` runs before `prep`.** Preparing a size must execute the font program before the control-value program, since `prep` calls functions that `fpgm` defines. Earlier the size-prep path ran only `prep`.

- **IP shifts out-of-range points.** Interpolate-points now brackets each point by the two reference points' original positions: points inside are interpolated proportionally; points outside the span are shifted by the nearer reference's delta, per the specification. Previously all points were scaled proportionally, which pulled out-of-range points inward.

- **Outlines re-cubicized in fractional 26.6.** The hinted contour is converted back to a path in fixed point rather than rounded to whole pixels first, so small glyphs retain their shape. The device-ppem grid fit is reconstructed exactly by the painter: the loader hints at the true device ppem (`round(pointSize * dpi / 72)`) and divides the fitted outline by that scale before placement.

- **FLIP and vector opcodes implemented.** FLIPPT/FLIPRGON/FLIPRGOFF (`0x80`-`0x82`) and SPVFS/SFVFS/GPV/GFV/SFVTPV (`0x0A`-`0x0E`) were silent no-ops that drifted the stack; they are now handled.

- **HintingMode.Light is the default.** The three-way `HintingMode { Off, Light, Full }` replaces the inert boolean intent. `Light` grid-fits the Y axis only (`OriginalX` preserved, `CurrentY` fitted); `Full` fits both axes. Light is the default because, on a grayscale (anti-aliased) rasterizer, Y-only fitting gives crisp baselines and stem heights without the horizontal stem snapping that reads heavy - the same reasoning behind the A-series grey-rendering simplifications. The gate for turning hinting on was "no glyph worse than the unhinted baseline," which Light meets.

- **Project-reference direction kept intact.** `RenderOptions` lives in `Chuvadi.Pdf.Rendering`, which references `Chuvadi.Pdf.Rendering.DisplayList`; the raster `DisplayListBuilder` therefore takes plain primitives (a hinting scale and a light/full flag), not `RenderOptions`, and `PageRasterizer` maps the mode to those. This avoids inverting the existing dependency.

- **Full-mode parallel-stem squeeze deferred.** `Full` mode visibly over-tightens the horizontal distance between the two vertical stems of n/u/m/h, collapsing the counter. This is an X-axis minimum-distance/single-width refinement and is left for a follow-up; Light (the default) does not exhibit it because it does not fit X. The MDRP/MIRP distance-type-compensation-zero, single-width-no-op, and MPS-as-ppem simplifications carried since A-series are revisited with that work.

**Files affected:** `src/Chuvadi.Pdf.Fonts.Rendering/Hinting/HintingInterpreter.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/TrueTypeLoader.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/FontRenderer.cs`,
`src/Chuvadi.Pdf.Rendering/RenderOptions.cs`,
`src/Chuvadi.Pdf.Rendering/PageRasterizer.cs`,
`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/DisplayListBuilder.cs`,
`examples/Chuvadi.Examples.Render/Program.cs`,
`tests/Chuvadi.Pdf.Fonts.Rendering.Tests/HintingStateOpsTests.cs`,
`tests/Chuvadi.Pdf.Fonts.Rendering.Tests/HintingMovementTests.cs`.

---

## A26 - Full-Mode Hinted Advance Width

**Date:** 2026-06-09
**Scope:** `Chuvadi.Pdf.Fonts.Rendering` (loader), `Chuvadi.Pdf.Rendering.DisplayList` (raster builder)
**Rationale:** After A25 shipped, `Full` mode left an extra gap to the right of each glyph. The glyph program grid-fits the horizontal advance phantom (pp2), but the renderer ignored that and advanced the pen by the scaled static `hmtx` value, so the cell was wider than the grid-fitted ink. (This entry also restores the A25 record, which was inadvertently omitted from the v2.5.0 commit and is included here alongside the fix.)

Key decisions:

- **Read the hinted advance from the phantom in Full mode.** `GetHintedGlyphOutline` now computes the advance from the hinted horizontal phantom points (`pp2 - pp1`, in 26.6 device units, rounded to whole device pixels) instead of `round(hmtx * scale)`. `ShowText` and `ShowTextComposite` use that hinted advance (converted to user space by `1 / hintingScale`) when a glyph was hinted in Full mode.

- **Light and unhinted paths unchanged.** They keep using `GlyphMetrics.AdvanceWidthAt`, the scaled `hmtx` advance. Light does not grid-fit the horizontal axis, so its ink stays at scaled positions and a grid-fitted advance would mismatch it.

**Files affected:** `src/Chuvadi.Pdf.Fonts.Rendering/TrueTypeLoader.cs`,
`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/DisplayListBuilder.cs`.

---

## A27 - Composite Glyph Hinting

**Date:** 2026-06-10
**Scope:** `Chuvadi.Pdf.Fonts.Rendering` (loader, hinting interpreter)
**Rationale:** Composite glyphs (accented letters and other component-built glyphs) previously returned `null` from `BuildRawGlyph` and fell back to the scaled unhinted outline, so in a hinted render they carried different weight and vertical alignment than their simple-glyph neighbours. This adds a hinted assembly path for the common composite form.

Key decisions:

- **Per-component hinting, then assemble.** `BuildHintedComposite` hints each component as its own glyph (recursing for nested composites, depth-capped at 3), translates its points by the component offset, and merges them into one zone with re-based contour ends. The component offset is scaled to device 26.6 and rounded to the grid when the component's `ROUND_XY_TO_GRID` flag is set - the rounding that aligns an accent to the pixel grid above its base. The composite's four phantom points are appended (scaled), and a carrier `RawGlyph` supplies the contour structure to the existing `BuildHintedPath`; coordinates come from the assembled zone.

- **Composite programs see the assembly as their originals (org <- cur).** Before running a composite's own instruction stream, the assembled (component-hinted) current coordinates are copied into the zone's original coordinates, and the natural originals are restored afterwards. This matches the reference interpreter: control-value cut-ins and the displacement that `SHC` / `IP` measure must be taken from the assembled positions the program was authored against, not from the unhinted design. Without it, a point whose current already differed from its original at program start (always true in a composite) picked up that stale difference as a phantom shift. Diagnosed on the dotted glyph (base + `dotaccent` with a 20-byte MIAP/SHC program): the dot landed ~1.2 px too high until `org <- cur` was applied, after which the output matched the classic-spec reference (FreeType interpreter v35) dot levels at 36/14/10 pt. The v40 "minimal" interpreter intentionally deviates from classic full hinting and is not the conformance target.

- **SHC / SHZ reference-point guard.** The same investigation showed `SHC` / `SHZ` shifted every point of the contour/zone including the reference point, double-displacing it. Fixed to skip the reference point per spec. Latent for simple glyphs (masked by org == cur at program start); corrected here because these ops appear almost exclusively in composite programs.

- **Scope deliberately narrow.** Scaled components, 2x2 transforms, and anchor-point placement fall back to the unhinted outline, as does nesting beyond depth 3. The hinted path therefore matches or exceeds the existing unhinted composite coverage and never renders a glyph worse than the prior baseline. Composites without their own instruction stream are still returned hinted, because their components carry the hinting.

**Files affected:** `src/Chuvadi.Pdf.Fonts.Rendering/TrueTypeLoader.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/Hinting/HintingInterpreter.cs`.

---

## A28 - Image -> PDF: Decoder-Backed Embedding and the Converter

**Date:** 2026-06-12
**Scope:** `Chuvadi.Pdf.Images` (new BmpDecoder), `Chuvadi.Pdf.Authoring` (ImageEmbedder, ImagePdfConverter, PageBuilder/PdfDocumentBuilder)
**Rationale:** The authoring image path supported only JPEG and PNG passthrough, and its RGBA-PNG branch emitted 4-component Flate data declared as DeviceRGB - structurally wrong. Image-to-PDF is also a first-class library use case (scans, photos, multi-page TIFF archives) deserving a one-call API.

Key decisions:

- **Passthrough where provably safe, decode everywhere else.** Baseline JPEG with 1 or 3 components embeds as-is under DCTDecode (DeviceGray/DeviceRGB from the SOF component count); 8-bit truecolour non-interlaced PNG embeds its raw IDAT zlib stream under FlateDecode with PNG Predictor 15. Every other variant - palette, grayscale, alpha, 16-bit PNG, TIFF, BMP, multi-component JPEG - decodes through the Chuvadi.Pdf.Images codecs and re-embeds as Flate-compressed raw samples. Correctness first; the fast paths are kept only where the bytes are exactly the PDF filter's input.

- **Alpha becomes a soft mask.** Decoded frames with any transparent pixel emit an RGB image plus a DeviceGray /SMask image object. Grayscale sources without alpha emit single-channel DeviceGray.

- **BmpDecoder completes the set.** Headers 12 and 40-124, depths 1/4/8/16/24/32, BI_RGB / BI_RLE8 / BI_RLE4 / BI_BITFIELDS (including V4+ alpha masks), top-down and bottom-up rows. It is the inverse companion of the existing BmpEncoder.

- **Converter is a thin layer over the builder.** `ImagePdfConverter` adds page sizing (SizeToImage at a DPI, or FitToPage with margins/centring/upscale control), multi-image to multi-page, optional TIFF frame expansion, and metadata - nothing it does is unavailable through PdfDocumentBuilder directly.

**Files affected:** `src/Chuvadi.Pdf.Images/BmpDecoder.cs` (new),
`src/Chuvadi.Pdf.Authoring/ImageEmbedder.cs` (new),
`src/Chuvadi.Pdf.Authoring/ImagePdfConverter.cs` (new),
`src/Chuvadi.Pdf.Authoring/PdfDocumentBuilder.cs`,
`src/Chuvadi.Pdf.Authoring/PageBuilder.cs`.

---

## A29 - Report Layout Layer

**Date:** 2026-06-12
**Scope:** `Chuvadi.Pdf.Authoring` (ReportBuilder, ReportLayoutEngine, ReportStyles, ReportTable)
**Rationale:** PdfDocumentBuilder draws at explicit coordinates; producing a real report (hospital discharge summaries, audit listings) means hand-managing pagination, repeated table headers, and page numbers. The library should own that layout.

Key decisions:

- **Block model over a flowing engine.** ReportBuilder records content blocks (headings, paragraphs, lists, tables, images, rules, spacers, page breaks); an internal ReportLayoutEngine walks them with a vertical cursor, starting pages as content reaches the bottom margin. The engine builds on the existing PageBuilder primitives - no new content-stream machinery.

- **Tables are a span-aware grid.** Rows list cells HTML-style (spanned-over positions are skipped); placement runs an occupancy grid that validates col/row spans and throws on overlap. Column widths resolve as fixed points, fractions of the content width, and equal-share autos. Row heights are content-driven (wrapped text or image aspect) with row-span deficits expanding the last spanned row. Rows welded by row spans paginate as a unit; a group taller than a fresh page splits at row boundaries with span cells clamped to the page bottom (degraded but bounded). Headers repeat per page by default. Border modes: None, Grid, Outline, HorizontalOnly (span-aware row boundaries), HeaderUnderlineOnly.

- **WinAnsi typographic mapping.** All report text passes through a mapper translating bullets, dashes, smart quotes, the ellipsis, the euro sign, and the trademark sign to their WinAnsi code points, because the content-stream writer emits `ch & 0xFF` octal escapes for non-ASCII and the Standard-14 fonts are declared WinAnsiEncoding.

- **Page numbers as a formatter.** `PageNumberFormatter` (Arabic, upper/lower Roman, Excel-style bijective letters) backs both header/footer `{page}`/`{total}` tokens and ordered-list markers.

- **Justification is word-by-word.** Full lines distribute the leftover width across word gaps; the last line of a paragraph stays left-aligned, per convention.

**Files affected:** `src/Chuvadi.Pdf.Authoring/ReportBuilder.cs` (new),
`src/Chuvadi.Pdf.Authoring/ReportLayoutEngine.cs` (new),
`src/Chuvadi.Pdf.Authoring/ReportStyles.cs` (new),
`src/Chuvadi.Pdf.Authoring/ReportTable.cs` (new).

---

## A30 - Geometric Autohinter: Y-Fitting Fallback for Unhinted Fonts

**Date:** 2026-06-12
**Scope:** `Chuvadi.Pdf.Fonts.Rendering` (Autohint components 3-5, loader hook), `Chuvadi.Pdf.Rendering` / `Chuvadi.Pdf.Rendering.DisplayList` (plumbing)
**Rationale:** Fonts without TrueType bytecode previously rendered from naturally scaled outlines - blurry baselines and x-heights at text sizes while hinted fonts on the same page rendered crisp. Components 1-2 (stem detection, blue zones) existed without a consumer; this wires them into a Y-fitting pass.

Key decisions:

- **Y axis only, matching Light.** The autohinter detects horizontal edges (flat runs of on-curve points, grouped across contours by Y and ink direction), anchors edges in blue zones (rounding the zone reference; classic overshoot suppression when the zone height scales below 3/4 px, whole-pixel overshoots above), fits opposing-edge pairs as horizontal strokes (whole-pixel weights, nearest-gap-first pairing), rounds remaining edges to the grid, and interpolates untouched points per contour with IUP-style rules (between anchors: linear in fitted space; outside: rigid with the nearer anchor). X positions stay naturally scaled in both Light and Full - the library's grayscale philosophy; X stem fitting via the existing vertical-stem detector is a recorded follow-up.

- **Font-level gate, not glyph-level.** The fallback applies only when the font carries no fpgm and no prep. In a hinted font, an instruction-less glyph keeps returning null (unhinted outline) so mixed renders keep consistent weights. Composite glyphs are not autohinted in this iteration and fall back to the unhinted outline; component-wise fitting is a recorded follow-up.

- **Blue zones from reference glyphs.** Per font, lazily, from the classic latin reference set (cap tops/bottoms, x-height, ascenders, descenders) via cmap lookups; missing characters are skipped and an empty table degrades to plain grid rounding.

- **On by default, opt-out exposed.** `RenderOptions.AutohintUnhintedFonts` (default true) flows through PageRasterizer -> DisplayListBuilder -> FontRenderer.GetHintedGlyphOutline -> TrueTypeLoader as a defaulted parameter, so existing call sites keep compiling and SvgRenderer picks the behaviour up automatically. The FontRenderer hinted-outline cache key gained an autohint bit.

- **Advance stays linear.** Autohinted outlines carry `round(hmtx x scale)` as their device advance; the raster path already uses the scaled hmtx advance in Light mode, and Full mode reads the outline's metrics - both consistent with un-grid-fitted X ink.

**Files affected:** `src/Chuvadi.Pdf.Fonts.Rendering/Hinting/Autohint/HorizontalEdges.cs` (new),
`src/Chuvadi.Pdf.Fonts.Rendering/Hinting/Autohint/Autohinter.cs` (new),
`src/Chuvadi.Pdf.Fonts.Rendering/TrueTypeLoader.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/FontRenderer.cs`,
`src/Chuvadi.Pdf.Rendering/RenderOptions.cs`,
`src/Chuvadi.Pdf.Rendering/PageRasterizer.cs`,
`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/DisplayListBuilder.cs`.

---

## A31 - Interpreter Spec Fixes: SSW, Engine Compensation, MPS, MIRP Hardening

**Date:** 2026-06-12
**Scope:** `Chuvadi.Pdf.Fonts.Rendering` (hinting interpreter)
**Rationale:** Three recorded simplifications plus two reference-verified MIRP behaviours, each checked line-by-line against the conformance reference (FreeType interpreter v35, ttinterp.c).

Key decisions:

- **SSW scales FUnits (bug fix).** `SSW` stored its popped argument raw; the value is in font units and must convert to pixels through the current scale, exactly as WCVTF treats its argument (`FT_MulFix(args[0], scale)` in Ins_SSW). This was the one genuine deviation among the three recorded items.

- **Engine compensation: plumbing with zero defaults.** The reference keys compensation off `opcode & 3` at exactly four sites - ROUND, NROUND, MDRP, MIRP - and applies it in the unrounded branch too (Round_None: add/subtract without crossing zero). FreeType sets all four values to zero, so the conformant default is zero; the table now exists (`SetEngineCompensation`) so the semantics are spec-shaped and embedders can model MS-rasterizer black/white compensation. At defaults, behaviour is bit-identical to before.

- **MPS stays ppem by default.** The classic v35 interpreter pushes the ppem from MPS (the GDI interpreter historically returned 12); the spec-true point size needs the rendering DPI, which the interpreter does not know. `MeasuredPointSize` lets an embedder supply it; the pipeline default remains the conformance behaviour.

- **Single-width forms split per instruction.** The shared snap helper approximated both. MDRP snaps when the original distance falls inside the window around +single-width (sign of the result follows the original distance); MIRP snaps when |cvt - single-width| is inside the cut-in (sign follows the CVT distance). Both now mirror Ins_MDRP / Ins_MIRP exactly.

- **MIRP hardening from the reference.** (a) The control-value cut-in test applies only when gep0 == gep1 (undocumented; in FreeType with an explicit comment). (b) When zp1 is the twilight zone, the point's original and current positions are seeded from rp0 plus the CVT distance along the freedom vector before distances are measured (undocumented MS-rasterizer behaviour, confirmed by Greg Hitchcock per the FreeType source). Both matter for fonts that hint through twilight anchors.

**Files affected:** `src/Chuvadi.Pdf.Fonts.Rendering/Hinting/HintingInterpreter.cs`.

---

## A32 - DisplayList Consolidation: One Walker, Two Sinks

**Date:** 2026-06-12
**Scope:** `Chuvadi.Pdf.Rendering.DisplayList` (new internal `Walking` layer; both display-list builders converted)
**Rationale:** Backlog item N.1. The project carried two complete content-stream interpreters - the SVG/Reader display-list builder (`Chuvadi.Pdf.Rendering.DisplayList` namespace, consumed by SvgRenderer, the public IPdfReader surface, WpfRenderer) and the raster builder (`Chuvadi.Pdf.Rendering.Raster`, consumed by PageRasterizer). Each owned its own tokenizer loop, operand parsing, string/name decoding, stream loading, and a ~60-case operator dispatch switch - roughly 500 lines of drifting duplication.

Key decisions:

- **Direction A: one walker, two sinks.** A new internal `Walking` layer owns everything that was mechanically duplicated: `ContentStreamLoader` (resolves /Contents, runs filter chains), `ContentStrings` (literal/hex string and name decoding), and `ContentStreamWalker` (tokenisation, inline-array capture for TJ and d, tolerant operand parsing, and a typed-event dispatch). `IContentOperatorSink` is the contract: ~45 operator events with no-op defaults.

- **All interpretation state stays sink-side.** The walker holds no graphics state. CTM handling, paths, text matrices, font resolution, glyph advances, gap tracking, clipping, and emission remain in each builder exactly as before - the split was chosen so both sides' numerics stay bit-identical through the refactor. Both public Build surfaces are unchanged; no public API was touched (api-docs regeneration produced no diff).

- **Form recursion stays sink-side.** The raster sink resolves form XObjects and calls the walker again on the form bytes with a sub-sink; the SVG sink keeps its image-only XObject handling (it never recursed into forms, and still does not - recorded follow-up).

- **Two genuine fixes fell out of unification:**
  (1) *Raster octal escapes.* The raster string decoder did not handle `\nnn` octal escapes (PDF 32000-1 7.3.4.2), so literal strings written with octal-escaped bytes - including the WinAnsi bullets and ellipses Chuvadi's own ReportBuilder emits - rendered the escape characters verbatim through the raster path. The unified decoder is octal-correct on both paths.
  (2) *SVG dash arrays.* The old dash parser kept only the first dash length and treated every later array element as the phase, so multi-element patterns (`[3 2] 0 d`) rendered wrong on the SVG path. The walker parses the full array.

- **Tolerant numerics on the SVG path.** The old builder used throwing `double.Parse`, so a malformed numeric operand aborted the whole page build; the raster builder already read malformed numbers as 0. The walker unifies on the tolerant form.

- **Behavioural asymmetries deliberately preserved:** the SVG sink continues to ignore cs/CS/sc/scn/SCN (the raster sink implements them); the raster quote operators (' and ") still bypass composite-font routing exactly as before. Both are recorded follow-ups, out of scope for a behaviour-neutral consolidation.

**Files affected:**
`src/Chuvadi.Pdf.Rendering.DisplayList/Walking/IContentOperatorSink.cs` (new),
`src/Chuvadi.Pdf.Rendering.DisplayList/Walking/ContentStreamWalker.cs` (new),
`src/Chuvadi.Pdf.Rendering.DisplayList/Walking/ContentStrings.cs` (new),
`src/Chuvadi.Pdf.Rendering.DisplayList/Walking/ContentStreamLoader.cs` (new),
`src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListBuilder.cs`,
`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/DisplayListBuilder.cs`.

---

## A33 - Reader Feature Batch: JPEG Encoder, CCITT Fax, Raw Raster Images, PDF Compression, Real DEFLATE

**Date:** 2026-06-12
**Scope:** `Chuvadi.Pdf.Images` (JPEG encoder), `Chuvadi.Pdf.Filters` (CCITTFaxDecode, DeflateDeflater), `Chuvadi.Pdf.Rendering.DisplayList` (raw-image raster path, DecodeParms), `Chuvadi.Pdf.Operations` (PdfCompressor); removal of `Chuvadi.Pdf.Images.Jpeg`
**Rationale:** Chuvadi Reader requirements: complete PDF-to-image export (all formats), open scanned PDFs, and compress documents. Word<->PDF conversion is handled by the companion docs/sheets library and is out of scope here.

Key decisions:

- **JPEG encoder** (`Chuvadi.Pdf.Images.JpegEncoder`): baseline sequential DCT (SOF0), JFIF 1.02, Annex K quantisation/Huffman tables, IJG quality scaling 1-100 (default 85). YCbCr 4:4:4 - no chroma subsampling, trading a few percent of size for clean edges on rasterised text, the dominant content for page export. Grayscale frames encode single-component; CMYK throws. API mirrors the sibling encoders: `Encode(ImageFrame, Stream, quality)`. Verified by roundtripping through Chuvadi's own JpegDecoder.

- **Duplicate encoder removed.** PR #80's squash had introduced a standalone `Chuvadi.Pdf.Images.Jpeg` project containing a second public `JpegEncoder` (raw byte[] API). It had zero consumers, zero tests, the same 4:4:4 baseline capability, and an API style inconsistent with the ImageFrame-based codecs. The project is deleted; the docs index previously carried two colliding `JpegEncoder` rows, now one.

- **CCITTFaxDecode** (`Chuvadi.Pdf.Filters.CcittFaxFilter`): Group 3 one-dimensional (Modified Huffman), Group 3 two-dimensional, and Group 4 (MMR) decoding with the full T.4 Tables 2/3 code set, honouring K, Columns (default 1728), Rows, BlackIs1, EncodedByteAlign, and EndOfBlock. Registered with the `CCF` alias. Encoding throws (Chuvadi writes bilevel images with Flate). FilterParameters gained the CCITT fields plus `ColumnsSpecified`, because CCITT's Columns default (1728) differs from the shared default (1).
  *Test provenance:* reference strips were generated by Pillow/libtiff encoding known patterns as Group 3/4 TIFFs. Pillow's fax writer emits its min-is-black samples as fax-WHITE runs (compensating via PhotometricInterpretation inside TIFF), so the expected fixtures pack the inverse image; absolute polarity is pinned independently by a hand-built T.4 vector.

- **Raster raw-image support.** The raster display-list builder previously rendered only images whose decoded bytes sniffed as JPEG - raw-sample images (Flate RGB/Gray, CCITT bilevel) were silently dropped. It now converts 1-bpc gray (incl. CCITT output), 8-bpc DeviceGray, and 8-bpc DeviceRGB samples to frames, resolving ICCBased N=1/N=3 colour spaces and honouring /Decode [1 0] inversion. `ContentStreamLoader.Decode` now reads /DecodeParms (and /DP) per filter index - required for CCITT and also correct for Flate predictors. Stencil masks (/ImageMask) remain skipped: the rasterizer's CompositeImage copies pixels rather than alpha-blending (recorded follow-up).

- **Real DEFLATE compression.** `DeflateDeflater` was a Phase-1 placeholder emitting stored (uncompressed) blocks - every PNG export and every authored content stream was full-size plus framing. It now does LZ77 (32 KiB window, hash-chain search, max chain 128) with fixed-Huffman block emission and a stored-block fallback for incompressible data. Measured: repetitive text 6%, PDF operator streams 17%, zeros 1%; roundtrips verified through the in-repo inflater. Dynamic Huffman is a recorded follow-up.

- **PdfCompressor** (`Chuvadi.Pdf.Operations`): rewrites a document smaller via (1) trailer-rooted garbage collection with dense renumbering - preserving the full catalog graph (outlines, forms, metadata), unlike a page-extraction rebuild; (2) Flate compression of unfiltered streams when it shrinks them; (3) opt-in JPEG re-encoding of 8-bit RGB/gray images (raw or single-Flate, no SMask/ImageMask, above a pixel threshold). ObjectsRemoved is computed against trailer /Size (the lazy object store cannot enumerate the source). Encrypted input is written back decrypted. Object streams + xref streams in the writer are a recorded follow-up.

**Files affected:**
`src/Chuvadi.Pdf.Images/JpegEncoder.cs` (new),
`src/Chuvadi.Pdf.Filters/CcittFaxFilter.cs` (new),
`src/Chuvadi.Pdf.Filters/IStreamFilter.cs`,
`src/Chuvadi.Pdf.Filters/FilterRegistry.cs`,
`src/Chuvadi.Pdf.Filters/DeflateFilter.cs`,
`src/Chuvadi.Pdf.Rendering.DisplayList/Walking/ContentStreamLoader.cs`,
`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/DisplayListBuilder.cs`,
`src/Chuvadi.Pdf.Operations/PdfCompressor.cs` (new),
`src/Chuvadi.Pdf.Operations/Chuvadi.Pdf.Operations.csproj`,
`src/Chuvadi.Pdf.Images.Jpeg/` (removed), `Chuvadi.slnx`.
