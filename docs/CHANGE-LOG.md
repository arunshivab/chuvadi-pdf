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


---

## A34 - PageBuilder deep copy: stop mutating the source document

**Date:** 2026-06-13
**Scope:** `Chuvadi.Pdf.Operations` (PageBuilder copy/remap; all page-tree operations)
**Rationale:** Bug found while wiring the library into a fresh reader project. Chaining operations on one document - rasterize, split, then compress - produced a compressed file missing its images and signature, and split/merge output was blank for real documents. Root cause was not the compressor: `PageBuilder.CopyDictionary` was a shallow copy, so a copied page shared the very same `/Resources` dictionary instance as the source, and `RemapReferences` then rewrote that shared dictionary's reference numbers in place.

Consequences of the old behaviour:
- The source `PdfDocument` was mutated by any page operation. A later operation on the same document resolved the now-rewritten references to the wrong objects (or to nothing), dropping fonts and images. This is why a compress after a split lost the image and signature.
- Multi-page documents whose pages share a font or image resource object had that shared dictionary remapped more than once, scrambling references in the output itself.
- The array branch of the remapper was a no-op (a `// rebuild if needed` placeholder), so indirect references inside arrays - `/Annots`, array `/Contents` - were never renumbered.

Fix: replaced the shallow copy plus in-place remap with a single recursive deep copy (`DeepCopyPrimitive` / `DeepCopyDictionary`) that always allocates new dictionary, array, and stream instances and rewrites reference numbers through the remap table. Scalars and references are immutable and shared safely. `CopyDictionary` now delegates to the deep copy with an empty remap (detach without renumbering). The source document is never touched.

Verified with an independent external PDF parser (pikepdf): split, merge, and compressed outputs of a real text-plus-image PDF all retain their image streams, and the source document's references are unchanged after splitting. New regression tests build multi-page documents that share one font object and one image object across all pages and assert (1) the source is not mutated by `SplitPages`, (2) every split page keeps the shared font and image, (3) merge preserves resources across all pages, and (4) a second operation on an already-split document still sees intact resources.

**Note:** this is a pre-existing defect, not a v2.8.0 regression; it surfaced now because chaining operations and inspecting real output exercised the shared-resource path that the synthetic page-count tests never did.

**Files affected:**
`src/Chuvadi.Pdf.Operations/PageOperations.cs`,
`tests/Chuvadi.Pdf.Operations.Tests/PageBuilderSharedResourceTests.cs` (new).


---

## A35 - Standard-14 text rendering: real outline bundle + raster wiring

**Date:** 2026-06-13
**Scope:** `Chuvadi.Pdf.Fonts.Rendering` (Standard14Outlines, bundle), `Chuvadi.Pdf.Rendering.DisplayList` (raster text path), `tools/build_standard14_bundle.py`, `Standard14.bin`
**Rationale:** Found wiring the library into a reader. Rasterising a normal PDF produced pages with vector graphics but no text. Most real documents use the Standard 14 fonts (Helvetica/Times/Courier) without embedding a font program, on the assumption the consumer supplies the glyphs. Chuvadi anticipated this with an embedded outline bundle, but two things were unfinished.

Root causes:
1. **The bundle was a placeholder.** `Standard14.bin` shipped as a 576-byte header with zero glyphs (the source comment said so), so outline lookups returned empty paths. It is now generated from the 14 substitute TTFs (Liberation Sans/Serif/Mono, URW StandardSymbolsPS/D050000L) - 361,786 bytes, ~191 glyphs per text font.
2. **The raster path never consulted the bundle.** `DisplayListBuilder.ResolveFontRenderer` built a `FontRenderer` only from an embedded font program; for a non-embedded font it returned null and `ShowTextSimple` skipped glyph emission entirely. It now falls back to `RenderableFont` (which reads the bundle) for the 14 standard font names, emitting `DrawGlyphOp`s and advancing by the AFM standard widths.

Supporting fixes:
- The build tool converted quadratic TrueType outlines to cubic via Qu2CuPen. The loader's QUAD case only handled a single 2-point segment, but TrueType `qCurveTo` bundles many implied-on-curve points (up to 9 in Liberation Sans); without conversion, curved glyphs rendered malformed. all_cubic emits only 3-point cubics, which the loader draws directly.
- The tool resolves symbol-encoded fonts (Symbol, ZapfDingbats) that have no Unicode cmap, via their (3,0) subtable at 0xF000+code, and extends coverage to 0x20-0xFF (Latin-1 accents for text fonts).
- `Standard14Outlines` normalises outlines to a 1000-unit em on load (the substitute fonts use 2048), so callers scale uniformly by pointSize/1000. Without this, glyphs rendered ~2x too large.

Verified: rasterising a reportlab PDF that uses non-embedded Helvetica now renders all text legibly (heading, body lines) alongside the image; previously the text band had zero dark pixels. 27/27 projects, 1,754 tests green (248 Fonts.Rendering, 117 Rendering).

**Known cosmetic follow-up:** intra-word tracking is slightly loose because glyph shapes are Liberation substitutes placed on AFM standard advances; legible and correctly laid out, but spacing fidelity could be revisited.

**Licensing:** Liberation = SIL OFL 1.1; URW StandardSymbolsPS/D050000L = AGPL with font exception. Both redistributable under Apache-2.0 (noted in the build tool header).

**Files affected:**
`src/Chuvadi.Pdf.Fonts.Rendering/Standard14Outlines.cs`,
`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/DisplayListBuilder.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/Resources/Standard14.bin` (regenerated),
`tools/build_standard14_bundle.py`,
`tools/fonts/` (14 substitute TTFs).


---

## A36 - Embedded font rendering: code-based glyph selection + Type1 support

**Date:** 2026-06-13
**Scope:** `Chuvadi.Pdf.Fonts.Rendering` (TrueTypeLoader, FontRenderer, new Type1FontRenderer, new StandardEncoding), `Chuvadi.Pdf.Rendering.DisplayList` (raster text path)
**Rationale:** A real AERB/eLORA letter rasterised blank, then (after a partial fix) with garbled glyphs. The document used four embedded subset fonts - three TrueType (FontFile2) and one Type1 (FontFile) - none of them Standard-14. Diagnosis required external tooling (pikepdf + fontTools) to read the actual cmaps and content-stream codes.

Root causes and fixes:
1. **Cmap coverage.** TrueTypeLoader only parsed (3,1)/(0,x) format-4 cmaps. LibreOffice subset fonts carry a (1,0) format-0 cmap mapping content-stream codes directly to subset glyph indices. Added format 0 and 6 parsing, and now retain Unicode, symbol (3,0), and Macintosh (1,0) subtables separately, exposing GetGlyphIndexForCode(code, symbolic) and GetGlyphIndexUnicode(cp).
2. **Glyph selection key.** The raster text path decoded bytes to Unicode first and looked glyphs up by Unicode value - correct for standard fonts, wrong for symbolic/subset fonts whose cmap is keyed by the raw code. ShowTextSimple now iterates the raw bytes; for each code it resolves a GID via the font's encoding (non-symbolic: code -> Unicode -> Unicode cmap, preserving existing behaviour) then by code through the symbol/Mac cmaps and a code-as-index fallback. This fixed F1/F2/F3 (the body text) while keeping the 117 render tests and 248 font tests green.
3. **Type1 programs.** Added Type1FontRenderer: PFB normalisation, eexec decryption (R=55665), charstring decryption (R=4330, lenIV), the Type1 charstring interpreter (hsbw/sbw, moveto/lineto/curveto family, closepath, callsubr/return, hstem/vstem, div, seac accent composition via StandardEncoding, flex and hint-replacement OtherSubrs, endchar), and code->name resolution via the font's built-in /Encoding or the PDF /Differences. Wired as a third fallback in the text path after embedded TrueType and Standard-14.
4. **Advances.** Simple-font advance widths now come from the PDF /Widths array (FirstChar + Widths) when present - the authoritative source - fixing spacing for the Type1 "Note:" run and making all simple-font advances spec-correct. Word spacing now keys on single-byte code 32.

Verified: the AERB letter renders all text correctly (heading, body, signature block, special notes, the bold Type1 "Note:", the blue hyperlink), matching the source. Whole-page dark-pixel count went 377 (blank) -> 28,970. 27/27 projects, 1,758 tests green.

**Known gap (separate work):** the masthead is an embedded baseline JPEG (DCTDecode, 569x113, DeviceRGB) image XObject; Chuvadi does not yet decode DCTDecode for display, so it renders blank. A baseline JPEG decoder (Huffman + IDCT + YCbCr->RGB + chroma upsampling) is a separate component.

**Files affected:**
`src/Chuvadi.Pdf.Fonts.Rendering/TrueTypeLoader.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/FontRenderer.cs`,
`src/Chuvadi.Pdf.Fonts.Rendering/Type1FontRenderer.cs` (new),
`src/Chuvadi.Pdf.Fonts.Rendering/StandardEncoding.cs` (new),
`src/Chuvadi.Pdf.Rendering.DisplayList/Raster/DisplayListBuilder.cs`.


---

## A37 - Progressive + CMYK/YCCK JPEG decoding for embedded images

**Date:** 2026-06-13
**Scope:** `Chuvadi.Pdf.Images` (JpegDecoder)
**Rationale:** A real AERB/eLORA letter rendered all text correctly (after A36) but the masthead - emblem, Devanagari, and red title - stayed blank. Inspection (pikepdf + marker dump) showed the masthead is a progressive JPEG (SOF2, 569x113, 4:2:0). The rasterizer already routed DCTDecode through JpegDecoder, but the decoder supported only baseline (SOF0) and threw ImageException on SOF2, which EmitImageXObject silently dropped. So the wiring was fine; the decoder lacked progressive support.

Implementation:
- Rebuilt JpegContext around a per-component DCT coefficient buffer (sized to the MCU-aligned block grid), shared by baseline and progressive. Baseline decodes a full block per call; progressive runs multiple scans, each updating a spectral band / bit-plane: decodeDCFirst, decodeDCSuccessive, decodeACFirst (with EOB runs), decodeACSuccessive (the successive-approximation refinement state machine). After the final scan, every block is dequantised, inverse-DCT'd, and written to a component sample plane; output assembly upsamples and colour-converts.
- Added 4-component support: Adobe APP14 transform (YCCK when transform=2, else CMYK), the Adobe inverted-channel convention, and CMYK->RGB for display. Existing 1- and 3-component paths preserved.
- Marker-aware bit reader over an in-memory byte[] handles stuffing (FF 00) and stops cleanly at the next marker; restart intervals reset DC predictors, EOB run, and AC state.

Verification: decoded baseline/progressive x 4:4:4/4:2:0, progressive grayscale, and progressive CMYK fixtures and compared against a reference decoder - max per-channel difference within IDCT rounding (<=5 for RGB, <=1 for gray/CMYK). The real masthead decodes correctly (meanDiff 2.21; the maxDiff outliers are high-contrast red-on-white edge ringing). The full letter now rasterises complete - masthead included. 27/27 projects, 1,762 tests green (+4 progressive decoder tests). Baseline image tests unchanged (58 -> still green).

Not supported: 12-bit precision, arithmetic coding (SOF9-11), lossless JPEG.

**Files affected:**
`src/Chuvadi.Pdf.Images/JpegDecoder.cs` (rewritten),
`tests/Chuvadi.Pdf.Images.Tests/JpegProgressiveTests.cs` (new).


---

## A38 - One-call render facade; removal of the obsolete SvgExporter

**Date:** 2026-06-14
**Scope:** `Chuvadi.Pdf.Reader` (new facade), `Chuvadi.Pdf.Svg` (removal), README
**Rationale:** Feedback from the first app built on the library (the Reader) reported a CV rendering with an upside-down photo and doubled/overlapping text. Investigation: the app called `SvgExporter.ExportPage(doc, i)` - a single static call that greps first - which the library had marked [Obsolete] in favour of SvgRenderer. Rendering the same file empirically through both paths confirmed SvgExporter is genuinely broken in 2.8.4 (flipped/mangled image, structurally overlapping glyph positions) while SvgRenderer is correct. So the library's problem was not capability but ergonomics: the path of least resistance led to the deprecated, broken API. For a library whose stated goal is "any amateur can build a PDF editor," the recommended path must also be the easiest.

Decisions (from the owner):
1. Remove the obsolete code outright - no deprecated code in the package. Deleted SvgExporter.cs plus the helpers used only by it: ImageDispatcher.cs, TextDispatcher.cs, SvgGraphicsState.cs (and SvgExporterTests.cs). StreamDecoder stays - it is used by the live FontEmbedder, not just the exporter. Verified by reference analysis that the deleted files were reachable only from SvgExporter.
2. Provide one-call output to every format, not just SVG. Added `PdfRenderExtensions` in Chuvadi.Pdf.Reader: PdfDocument extensions RenderPageToSvg/Png/Jpeg/Bmp/Tiff (byte[] + Stream, DPI parameter), plus RenderToTiff() for a multi-page TIFF of all pages. Internally these wrap SvgRenderer (vector) and PageRasterizer + the existing image encoders (raster). Home is Chuvadi.Pdf.Reader, the existing high-level consumer facade; it gained ProjectReferences to Rendering and Images.
3. Ship as 3.0.0 (breaking: a public type was removed).

README quick-start now leads with the one-call render facade. Verified: every format opens correctly in an external image library, the PNG render matches the correct SvgRenderer output (right-side-up masthead, clean text), and DPI scaling works. 27/27 projects, 1,756 tests green (SvgExporter's 6 tests removed; 9 facade tests added).

**Files affected:**
removed `src/Chuvadi.Pdf.Svg/{SvgExporter,ImageDispatcher,TextDispatcher,SvgGraphicsState}.cs`, `tests/Chuvadi.Pdf.Svg.Tests/SvgExporterTests.cs`;
added `src/Chuvadi.Pdf.Reader/PdfRenderExtensions.cs`, `tests/Chuvadi.Pdf.Reader.Tests/PdfRenderExtensionsTests.cs`;
modified `src/Chuvadi.Pdf.Reader/Chuvadi.Pdf.Reader.csproj` (refs), `README.md`.


---

## A39 - Performance & document-feature batch: streaming pages, OC toggle, parallel redaction, rasterizer benchmark

**Date:** 2026-06-14
**Scope:** Documents, Redaction, benchmarks, developer guide
**Rationale:** Stock-take batch of the tractable Performance & Scale and Document-features items (linearization and Tagged-PDF/PDF-A carved out as conformance-graded efforts of their own). Several items turned out partly built, so the work was incremental.

- **Streaming page enumeration.** `PdfPageCollection` was already lazy+caching; added `EnumerateStreaming()` - a non-caching page-tree walk that yields each leaf page once, so a full pass over a huge document holds only the current page.
- **Optional content toggle.** Reading already shipped (`OptionalContentReader`). Added `OptionalContentWriter.SetVisibility(output, document, name->visible)`: edits the default config's /ON and /OFF arrays unambiguously (independent of /BaseState) and writes out, rewriting the shallowest indirect object that owns the change (the /D object, else /OCProperties, else the catalog when both are inline).
- **Parallel redaction.** The redactor already grouped work per page. Found `PdfObjectStore.TryGet` is not thread-safe (a cache miss writes back), so the store-touching load stays serial; only the pure per-page transforms (`RewriteContent`, `BuildOverlay`) run under `Parallel.For`, gated by the new opt-in `RedactionOptions.MaxDegreeOfParallelism` (default 1). Object-number allocation and assembly stay serial in stable order, so output is byte-identical - verified by a test asserting parallel (-1) equals sequential (1) byte-for-byte across 4 pages, plus all 31 existing redaction tests unchanged.
- **Rasterizer benchmark.** Added `RasterizeBench` (150/300 DPI) to the existing `Chuvadi.Benchmarks` project.
- **Doc correction.** Found `Chuvadi.Pdf.Signatures/Signing/` (PdfCounterSigner, PdfDocumentTimestamper, PdfLtvUpdater) is public and shipped; corrected the developer guide section 14 and module map, which had wrongly said signatures were read-only.

Gate: 27/27 projects, 1,771 tests (6 new), `dotnet format` clean.

**Files:** added `src/Chuvadi.Pdf.Documents/OptionalContentWriter.cs`, `benchmarks/Chuvadi.Benchmarks/Scenarios/RasterizeBench.cs`, `tests/Chuvadi.Pdf.Documents.Tests` (+5 tests), `tests/Chuvadi.Pdf.Redaction.Tests` (+1 test); modified `PdfPageCollection.cs`, `Redactor.cs`, `RedactionOptions.cs`, `Chuvadi.Benchmarks.csproj`, `docs/developer-guide.md`, `docs/BACKLOG.md`.


---

## A40 - Custom TrueType font embedding in authoring (Indic-capable)

**Date:** 2026-06-14
**Scope:** Authoring; new `TrueTypeFontEmbedder`, `EmbeddedFontObjects`, `CustomFontRegistry`; `PdfDocumentBuilder.AddTrueTypeFont`
**Rationale:** Authored output (ReportBuilder/PdfDocumentBuilder) could only use the 14 standard fonts, so non-Latin content (Tamil/Devanagari for Lipi HIS, Kaval) was impossible. Investigation with the SIL OFL LiPi Sans family confirmed Chuvadi's TrueType glyph primitive already renders Indic glyf outlines correctly (Tamil/Deva rendered by eye via TrueTypeLoader), so the missing piece was authoring-side embedding, not rendering.

- New `TrueTypeFontEmbedder.Build(ttf, loader, usedCodepoints, baseFont, allocId)` produces the Type0 object graph: FontFile2 (full program, uncompressed, /Length1), FontDescriptor (Flags/FontBBox/Ascent/Descent/CapHeight/ItalicAngle parsed from head/hhea/OS-2/post, scaled to 1000/em), CIDFontType2 descendant (CIDToGIDMap=/Identity, DW=1000, /W for used glyphs), ToUnicode CMap (gid->Unicode, surrogate-aware), and the top Type0 (Identity-H).
- Reuses `TrueTypeLoader` (Fonts.Rendering) for cmap lookups and per-glyph advance widths; added the Fonts.Rendering project reference to Authoring (no cycle).
- Authoring wiring: `CustomFontRegistry` tracks registered fonts + used code points; `PageBuilder.DrawText` maps text->GIDs and emits `<hex> Tj` via the new `ContentStreamWriter.ShowGlyphsAt`; `PdfDocumentBuilder` embeds each used font once at build time and references the shared Type0 from each page's /Font resources.
- **Proven end to end:** authored Tamil (கமலனவ, கநதம) and Devanagari (कखगघ) via the public API and rendered them back through Chuvadi correctly; verified by eye and by structural tests (Type0 + CIDFontType2 + FontFile2 + Identity-H + W + ToUnicode; shared-across-pages; unused-not-embedded).
- **Scope boundary:** logical-order only, no GSUB/GPOS shaping or reordering; variable fonts must be pre-instantiated to static; whole-font embed (subsetting deferred). All recorded honestly in CHANGELOG and docs/custom-fonts.md.

Gate: 27/27 projects, 1,774 tests (3 new), style clean.

**Files:** added `TrueTypeFontEmbedder.cs`, `EmbeddedFontObjects.cs`, `CustomFontRegistry.cs`, `tests/.../CustomFontEmbeddingTests.cs`, `tests/.../Fixtures/LiberationSerif-Regular.ttf`, `docs/custom-fonts.md`; modified `ContentStreamWriter.cs`, `PageBuilder.cs`, `PdfDocumentBuilder.cs`, both csprojs, CHANGELOG.


---

## A41 - Inline-image redaction + backlog reconciliation

**Date:** 2026-06-14
**Scope:** Redaction (`Redactor`), docs/BACKLOG.md
**Rationale:** Completing the non-text redaction story. Verifying the backlog before starting revealed that pattern-based redaction (the originally-planned next item) and non-text image/form redaction were already shipped and only inline images remained - so the backlog itself was reconciled against the code as part of this PR.

- **Inline images.** The content tokenizer has no `BI/ID/EI` handling, so inline-image binary data was being tokenized as operators - a latent correctness bug for any content stream containing an inline image. `Redactor.RewriteContent` now intercepts the `BI` keyword, reads the dict up to `ID`, scans the raw bytes for the whitespace-delimited `EI`, and treats `BI…EI` as one unit: dropped when the CTM-mapped unit square intersects a redaction rect (reusing `ShouldRedactImageAtCtm`, since inline images paint the unit square like `Do`), otherwise copied verbatim; parsing resumes past `EI` via `tokenizer.Seek`. Two tests: image-in-rect removed with surrounding text intact; image-out-of-rect preserved with operator-like binary data correctly skipped (regression guard for the latent bug).
- **Backlog reconciliation.** Verified every "Not started" item against the code by grep. Found pattern redaction (`PatternMatcher`/`CommonPatterns`, tested), non-text image/form redaction (`ShouldRedactImageAtCtm`), optional content toggle, and linearization (`LinearizedWriter`, tested) all already shipped despite "Not started" labels. Rewrote `docs/BACKLOG.md` to a verified Shipped list + a clean 1-13 open roadmap, replacing the duplicate N.5-N.8 numbering. Remaining non-text redaction work (nested form-XObject recursion) kept as open item 1.

Gate: 27/27 projects, 1,776 tests (2 new), style clean. No public API change.

**Files:** modified `src/Chuvadi.Pdf.Redaction/Redactor.cs`, `tests/Chuvadi.Pdf.Redaction.Tests/RedactionTests.cs`, `docs/BACKLOG.md`, `CHANGELOG.md`.


---

## A42 - Glyph subsetting for embedded TrueType fonts

**Date:** 2026-06-14
**Scope:** Authoring (`TrueTypeSubsetter`, `TrueTypeFontEmbedder`)
**Rationale:** v3.2.0 embedded the whole font program; for complex-script fonts (e.g. the Noto/LiPi Indic faces) that is tens of KB per font, dominated by `glyf` outlines and the GSUB/GPOS/GDEF layout tables.

- **Approach (numbering preserved).** `TrueTypeSubsetter.Subset(font, usedGlyphs)` keeps used glyphs at their original ids and truncates `numGlyphs` to the highest used id + 1; unused glyphs become empty. Because glyph numbering does not change, `CIDToGIDMap` stays `/Identity`, the content stream still emits original gids, and the per-CID `W` array and `ToUnicode` are untouched - the only embedder change is the `FontFile2` bytes. The alternative (renumbering glyphs + a `CIDToGIDMap` stream) would shrink `loca`/`hmtx` slightly more but is far more invasive; deferred.
- **What is kept vs dropped.** Kept: `head`, `hhea`, `maxp`, `hmtx`, `loca`, `glyf`, and `cvt`/`fpgm`/`prep`/`gasp` when present. Dropped: `cmap`, `post`, `name`, `OS/2`, and `GSUB`/`GPOS`/`GDEF` - a CIDFontType2 with an Identity CID-to-GID map does not consult them, and the layout tables are the bulk of an Indic font. Descriptor metrics are still read from the original font (which retains `OS/2`).
- **Composite glyphs.** The used set is closed over composite components (parsing the component flags to skip args/transforms) so referenced glyphs keep real outlines; no remapping is needed since ids are unchanged.
- **Correctness.** Long `loca`; `checkSumAdjustment` recomputed. CFF fonts (no `glyf`/`loca`) pass through unchanged.
- **Verification.** Subsetting a Tamil face to 5 glyphs: 82,644 -> 1,564 bytes (1.9%); fontTools opens the result (tables reduced to the rendering set) and Chuvadi re-parses the க outline identically (50 segments). End-to-end via the public API, a two-font Tamil + Devanagari page went 309,037 -> 7,001 bytes and rendered back pixel-identical by eye.

Gate: 27/27 projects, 1,777 tests (1 new), style clean. No public API change (`TrueTypeSubsetter` is internal).

**Files:** added `src/Chuvadi.Pdf.Authoring/TrueTypeSubsetter.cs`; modified `TrueTypeFontEmbedder.cs`, `tests/Chuvadi.Pdf.Authoring.Tests/CustomFontEmbeddingTests.cs`, `docs/custom-fonts.md`, `docs/BACKLOG.md`, `CHANGELOG.md`.


---

## A43 - Per-run font style (copy-with-format) + shared style classifier

**Date:** 2026-06-14
**Scope:** Rendering.DisplayList (`FontStyle`, `FontStyleClassifier`, `TextOp`, `TextRun`, `DisplayListBuilder`, `TextRunExtractor`), Svg (`SvgRenderer`)
**Rationale:** `TextRun` exposed only geometry + Unicode, so a consumer (e.g. the Reader) could not copy text with formatting. The SVG renderer separately inferred bold/italic from the base-font name only, missing fonts that signal style through their descriptor.

- **Shared classifier.** `FontStyleClassifier.Classify(baseFont, flags?, italicAngle?, stemV?)` returns a `FontStyle { FontFamily, Weight, Slant, ItalicAngle }`. It strips subset tags, extracts the family before the first `-`/`,`, and marks bold (name `Bold`/`Black`/`Heavy`, `/Flags` ForceBold bit 19, or `/StemV` >= 140) and italic/oblique (name `Italic`/`Oblique`, `/Flags` Italic bit 7, or non-zero `/ItalicAngle`). Pure (primitives in, `FontStyle` out) so it has no PDF dependency and is shared by both consumers.
- **Threading.** `DisplayListBuilder.SetFont` resolves the descriptor (handling the Type0 -> descendant-CIDFont hop) once per font key, classifies, and stores the `FontStyle` on `BuilderState`; both `TextOp` construction sites copy it onto `TextOp.Style`. `TextRunExtractor` carries it (and the run's `FontSize`) onto the new `TextRun` members. `SvgRenderer.ResolveStyleHints` now maps `TextOp.Style` to CSS, replacing the name-only check (output shape unchanged: `"bold"`/`"italic"`/null).
- **Verification.** Classifier unit-tested across name, flag, italic-angle, and StemV branches; extractor propagation unit-tested; and on a real authored PDF the builder's descriptor path yields families `Helvetica`/`LiPiSansTamil`/`LiPiSansDeva` with correct sizes (16/48).

Gate: 27/27 projects, 1,790 tests (13 new), style clean. New public API (`FontStyle`, `FontSlant`, `FontStyleClassifier`, `TextOp.Style`, four `TextRun` members) -> api docs regenerated.

**Files:** added `FontStyle.cs`, `FontStyleClassifier.cs` (+ 2 test files); modified `RenderOp.cs`, `BuilderState.cs`, `DisplayListBuilder.cs`, `TextRun.cs`, `TextRunExtractor.cs`, `SvgRenderer.cs`, `CHANGELOG.md`, `docs/BACKLOG.md`, and regenerated `docs/api/**`.


---

## A44 - Fix Merge cross-document object-number collision + write deterministic trailer /ID

**Date:** 2026-06-14
**Scope:** Operations (`PageOperations.PageBuilder`), IO (`PdfWriter`)
**Rationale:** Two defects surfaced wiring Merge/Split into Chuvadi Reader, reproduced with an independent parser on two inputs whose object numbers overlap (a 4-page document with /Contents *arrays* + a 3-page single-stream document, both numbering from 1).

- **Collision.** `PageBuilder.Write` built its renumbering table (`idRemap`) and its "already added" set keyed on the bare original object number. Object numbers are per-document, so document A object 4 and document B object 4 shared one slot: B's pages were rewritten onto A's streams and B's real streams were skipped, producing blank/doubled/corrupt pages. Fix: thread a per-page source-document index (`PageEntry.DocIndex`; `AddPage`/`AddPageWithRotation` gain a `docIndex`, default 0 for the single-document operations) and key the remap, the dedup set, and the deep-copy reference rewrite on `(DocIndex, ObjectNumber)`. Within one document the index is constant, so genuinely shared objects still de-duplicate; across documents identical numbers stay distinct.
- **Missing /ID.** `PdfWriter.Write` set `/ID` only when encrypting, so Merge/ExtractPages/Split output had none and Adobe prompted to save on close. `GetOrCreateFileId` now takes the sorted object list and derives a 16-byte identifier from a SHA-256 over each object's id + stream bytes (truncated to 16), set as `/ID = [id id]` before the encryption block so both paths share one value. Content-derived rather than random keeps output deterministic - parallel and sequential redaction stay byte-identical - and is §14.4-aligned. The linearized path (`LinearizedWriter`) is separate and unchanged.
- **Verification.** On the reported inputs: merged 7 pages, /ID present, all 7 content streams decompress to their own correct text (RealA page 1; RealB pages 1-3) with no bleed, blanks, or duplication - refuting every repro symptom. Four regression tests added: cross-document collision content integrity, Merge /ID, ExtractPages /ID, deterministic-/ID byte-identity.

Gate: 27/27 projects, 1,796 tests (4 new), style clean. No public API change (the new `docIndex` parameters are on internal members; `GetOrCreateFileId` is private) - no api-doc regeneration.

**Files:** modified `src/Chuvadi.Pdf.Operations/PageOperations.cs`, `src/Chuvadi.Pdf.IO/PdfWriter.cs`; added `tests/Chuvadi.Pdf.Operations.Tests/MergeIntegrityTests.cs`; modified `CHANGELOG.md`, `docs/CHANGE-LOG.md`, `docs/BACKLOG.md`.


---

## A45 - Apply image /SMask in SVG rendering + expose PdfDocument.IsXfa

**Date:** 2026-06-14
**Scope:** Rendering.DisplayList (`ImageOp`, SVG `DisplayListBuilder.EmitXObject`), Svg (`ImageEncoder`), Documents (`PdfDocument`)
**Rationale:** Two issues from the Chuvadi Reader integration writeup - #3 (serious, soft-mask) and #4 (minor, XFA detection).

- **/SMask dropped (black box).** The SVG image path embedded only the base colour image; an `/SMask` (a DeviceGray alpha image) was ignored, so transparent regions - whose colour data is conventionally black - rendered as a solid black box. Fix: `EmitXObject` resolves `/SMask` (raw base images only; a JPEG base would need decoding before alpha can be attached), decodes its 8-bit gray samples (honouring `/Decode [1 0]` inversion) and carries them on new `ImageOp.SoftMaskAlpha`/`SoftMaskWidth`/`SoftMaskHeight`. `ImageEncoder.BuildDataUrl` builds an RGBA buffer (base RGB/gray expanded, mask nearest-neighbour resampled when sizes differ) and emits a colour-type-6 PNG; `EncodePng` gained the type-6 case. Verified on the reported `transparent_logo.pdf`: the embedded PNG is now RGBA with a fully transparent background (corner alpha 0) instead of opaque black.
- **XFA detection.** XFA forms render essentially blank because their content lives in the `/AcroForm /XFA` stream, not page content. New `PdfDocument.IsXfa` (expression-bodied over a private helper, so it surfaces in the API docs) lets consumers show a notice without reaching into the catalog. Full XFA rendering remains out of scope.
- **Backlog correction.** The v3.5.1 reconciliation wrongly marked `#6 ImageMask stencil` as shipped in the SVG path; stencil compositing in fact exists only in the raster path (`Raster/DisplayListBuilder`, `RasterRawImageTests`). The reconciled entry is corrected to raster-only and the SVG `/ImageMask` case is re-opened as item 6. `/SMask` (fixed here) is a distinct feature from `/ImageMask` (a 1-bit stencil).

Gate: 27/27 projects, 1,797 tests (3 new), style clean. New public API (`PdfDocument.IsXfa`; `ImageOp.SoftMaskAlpha`/`SoftMaskWidth`/`SoftMaskHeight`) -> api docs regenerated.

**Files:** modified `src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`, `src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListBuilder.cs`, `src/Chuvadi.Pdf.Svg/ImageEncoder.cs`, `src/Chuvadi.Pdf.Documents/PdfDocument.cs`; added `tests/Chuvadi.Pdf.Svg.Tests/ImageSoftMaskTests.cs`, `tests/Chuvadi.Pdf.Documents.Tests/XfaDetectionTests.cs`; modified `CHANGELOG.md`, `docs/CHANGE-LOG.md`, `docs/BACKLOG.md`, and regenerated `docs/api/Documents/PdfDocument.md`, `docs/api/Rendering/ImageOp.md`.

## A46 - Fix 21-byte xref entries (Adobe save-prompt) + write /Info and XMP metadata

**Date:** 2026-06-15
**Scope:** Objects (`XrefTable.FormatEntry`), IO (`PdfWriter`)
**Rationale:** Adobe Acrobat prompted to save merged/extracted documents on close even after only viewing them. Diagnosed from a real `merged3.pdf` supplied via the Chuvadi Reader integration.

- **Root cause: 21-byte xref entries.** `XrefTable.FormatEntry` emitted `"{offset} {gen:D5} {type} \r\n"` - a space before the CRLF - so each cross-reference entry was 21 bytes, not the 20 mandated by ISO 32000-1 §7.5.4. qpdf, pikepdf, and Chuvadi's own reader recover by scanning, so every structural check reported clean; Acrobat instead discards the misaligned table, rebuilds it on open, and marks the document modified. Confirmed by isolation: rewriting only the xref entries of the user's file to 20 bytes (nothing else changed) stopped the prompt in the user's Acrobat. Fix: drop the space -> `"{offset} {gen:D5} {type}\r\n"` = 20 bytes. Covers every writer, since all route through `XrefTable`/`PdfWriter`.
- **Diagnosis discipline.** An earlier width check sliced exactly 20 bytes and inspected the last two, silently truncating the 21st byte; measuring the *stride* between consecutive entries against a pikepdf-written control is what exposed it. An A/B test - a no-metadata pikepdf re-save also opened clean - disproved the initially-suspected `/Info`/XMP metadata theory and pointed at the serializer.
- **Metadata (hygiene, not the cause).** Metadata absence was not the trigger, but a public library should still emit it. `PdfWriter.Write` now adds a deterministic `/Info` (Producer/Creator) and an XMP `/Metadata` stream (pdf:Producer, dc:format, xmpMM:DocumentID/InstanceID derived from the file id) attached to a *copy* of the catalog (no caller-state mutation). Deterministic - no timestamps - so the redaction byte-identical guarantee holds. Caller-supplied entries are preserved.

Gate: 27/27 projects, style clean. New regression tests in `XrefAndMetadataTests` (20-byte entries; /Info; XMP). `DocumentMetadataTests` absent-XMP fixture switched to a hand-assembled raw PDF (the writer now always emits metadata); `WatermarkTests` size assertion replaced with a watermark-content assertion (a cross-serializer size comparison is no longer meaningful). No public API change -> no api-doc regeneration.

**Files:** modified `src/Chuvadi.Pdf.Objects/XrefTable.cs`, `src/Chuvadi.Pdf.IO/PdfWriter.cs`; added `tests/Chuvadi.Pdf.IO.Tests/XrefAndMetadataTests.cs`; modified `tests/Chuvadi.Pdf.Documents.Tests/DocumentMetadataTests.cs`, `tests/Chuvadi.Pdf.Watermark.Tests/WatermarkTests.cs`, `CHANGELOG.md`, `docs/CHANGE-LOG.md`.

## A47 - Conformance audit (Phase A, code survey) + backlog expansion

**Date:** 2026-06-15
**Scope:** docs only (`docs/CONFORMANCE-AUDIT.md` new, `docs/BACKLOG.md`)
**Rationale:** Drive the roadmap from a systematic spec-coverage survey instead of one bug report at a time.

Phase A is a source survey against ISO 32000-1 with file:line evidence for every verdict (no runtime corpus yet - that is Phase B). New findings not previously tracked, now backlog items 12-18: shadings/gradients (`sh` is a no-op, `ContentStreamWalker.cs:403`; no ShadingType 1-7); graphics-state transparency (`gs` no-op, `:402` - `ca`/`CA`, blend modes, ExtGState `SMask`, transparency groups ignored); non-device colour spaces (Separation/DeviceN tint transforms ignored, Indexed mis-keyed, Lab/raw-CMYK/ICCBased-N4 images unrendered - `Raster/DisplayListBuilder.cs:294,314,1772`); patterns (tiling type 1 + shading type 2 suppressed); annotation `/AP` appearance rendering (page `/Annots` not drawn at render time); XFA rendering; Type3 fonts (no `CharProcs` handling). Confirmed already-tracked: Indic shaping (#2), JBIG2 (#4), JPX (#5), SVG ImageMask (#6), SVG cs/scn + form recursion + quote operators (#11).

No code change. Phase B (runtime corpus audit) is the follow-up.

**Files:** added `docs/CONFORMANCE-AUDIT.md`; modified `docs/BACKLOG.md`, `docs/CHANGE-LOG.md`.
