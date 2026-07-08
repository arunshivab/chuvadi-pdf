# Chuvadi PDF Library — 35-Item Roadmap & Status Handoff

> **Purpose of this document.** A self-contained briefing for a fresh chat to pick up
> Chuvadi development. It captures: who/what Chuvadi is, the working agreement, the
> current released state, and the full 35-item / 7-phase plan **with status verified
> against the code on `main` at v3.11.1**. Hand this whole file to a new chat as the
> starting context.
>
> **Status verification date:** 2026-07-07, re-verified against `main` @ **v3.16.0**
> (statuses refreshed again for items advanced since v3.14.0 — notably non-device
> colourspaces, Indic shaping, and XFA, which have all shipped). Each item is tagged
> ✅ DONE / 🟡 PARTIAL / ❌ NOT DONE / ⚠️ VERIFY. "VERIFY" means the previous session
> inferred status but did not fully confirm in code — re-check before trusting.
>
> **Superseded note.** The open items in this roadmap are also tracked in
> `docs/BACKLOG.md`, which is the canonical open-roadmap going forward. This
> document is retained as the detailed 7-phase compression + rendering plan; when
> the two disagree, verify against the code (both have drifted before).

---

## 1. Project identity

**Chuvadi (சுவடி)** is a general-purpose, zero-NuGet-dependency PDF library for .NET 10,
built for worldwide public consumption (published to NuGet). It is **not** tailored to any
single consumer — Chuvadi Reader is just one of many potential consumers. Comprehensive PDF
handling across all font types and edge cases is the goal, regardless of rarity.

- **GitHub:** `arunshivab/chuvadi-pdf` — **PUBLIC**, Apache-2.0, branch-protected.
- **Local repo (Arun's Windows machine):** `C:\Users\aruns\Documents\Chuvadi\chuvadi-scaffold\chuvadi\` (referred to as `$repo`).
- **Solution file:** `Chuvadi.slnx`.
- **Current released version:** **v3.11.1** (tag on `main`).
- **Environment:** Windows, PowerShell 5.1, .NET 10 SDK.

### Architecture invariants (from `docs/BASELINE.md` — these never change)
- **B01 — Zero production dependencies.** Anything in `src/` references only the .NET BCL
  or other `Chuvadi.Pdf.*` projects. No `<PackageReference>` in any `src/` csproj. (Tests may
  use xUnit, FluentAssertions, FsCheck, BenchmarkDotNet.)
- **B02 — Strictly bottom-up dependency direction:**
  `Operations → Text → Fonts/Content → Documents → IO → Objects → Filters/Primitives`.
  Never introduce a dependency that points "up" the stack.
- Complete files only — never snippets or diffs (see working agreement below).
- No implementation file is complete without its tests.

### Phase history (the original 3-phase scheme, now all milestone-complete)
- **Phase 1 (core PDF library):** COMPLETE — shipped as the 0.9.x line.
- **Phase 2 (images & editing):** COMPLETE — **1.0.0 milestone reached 2026-05-11**.
- **Phase 3 (security & compliance):** mostly delivered — Encryption and Signatures shipped;
  PDF/A and JavaScript-preservation remain open.
- Everything since 1.0.0 (the 2.x and 3.x work — hinting interpreter, CFF/Type1 families,
  glyph subsetting, custom font embedding, copy-with-format, SMask fix, the recent Bench
  features and page-tree recovery) is post-milestone fidelity/robustness work.

> **Note on the "7-phase / 35-item" plan in this document:** this is a *separate, newer*
> planning scheme (cleanup + compression + conformance), not the original 1/2/3 phases above.
> Don't conflate them. The 35-item plan is what the next chunk of work should follow.

---

## 2. Working agreement (HARD RULES — do not violate)

These are non-negotiable. They exist because each was learned from a real mistake.

1. **Never guess at APIs, types, properties, or method signatures.** Before writing code
   against an external symbol: read the source and quote the line number that confirms it,
   or ask Arun to upload/paste the file. If you catch yourself thinking "this is probably
   called X" — stop and verify. Past costly guesses: `token.Position` (real:
   `token.ByteOffset`), assuming a type's shape before seeing it.

2. **Full files only.** Never deliver snippets, diffs, partial edits, or "anchored replace"
   instructions. Every code deliverable is a COMPLETE file. Before editing any existing repo
   file, you must have seen it IN FULL as it currently exists on `main` (an upload/paste from
   Arun, or — for the assistant with sandbox access — a fresh clone). A stale sandbox copy
   does not count.

3. **Reproduce before theorizing; verify before claiming done.** For bugs: reproduce in a
   sandbox first, trace the actual operator/object, then fix. Mechanical correctness ≠ visual
   correctness — don't claim "fixed" on a rendering defect until Arun confirms visually.
   **The user's eyes are ground truth.** If measurements say "fine" but Arun reports broken,
   you are measuring the wrong thing.

4. **File delivery is NOT auto-deployment.** Files the assistant produces are sandboxed; Arun
   copies them manually. Every delivery includes explicit "Copy/expand → path" instructions.
   Standard delivery: a timestamped zip with repo-relative paths → Arun expands into `$repo`.

5. **Hold git operations until told.** Arun runs all `checkout`/`commit`/`push`/`merge`. The
   assistant proposes exact commands and waits. Commit messages: ASCII only, written via temp
   file + `git commit -F` (never PowerShell heredocs with Unicode).

6. **Large focused PRs are preferred** over many small sequential commits, unless Arun asks
   otherwise. Build errors during iteration are expected and comfortable.

### Assistant sandbox capability (if the assistant has code execution)
The assistant's sandbox **has .NET 10** and can **clone the public repo, build, test, and
render** directly. Clone `https://github.com/arunshivab/chuvadi-pdf`, set
`export DOTNET_ROOT=<dotnet>; export PATH=$DOTNET_ROOT:$PATH`, and reproduce/verify there
before delivering. The sandbox also has python3, PIL, pikepdf, ImageMagick — useful for
inspecting PDF structure (`pikepdf`) and rendering pages to PNG for visual checks. The
sandbox **cannot** push to the repo; Arun does all git operations.

---

## 3. The build / gate / delivery process

### The full gate (all must pass before any commit)
Run from `$repo`:

```powershell
# 1. Clean caches (ALWAYS, before any build)
Get-ChildItem -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force

# 2. Build
dotnet build Chuvadi.slnx -c Release

# 3. Test (full suite)
dotnet test Chuvadi.slnx -c Release

# 4. Format check
dotnet format Chuvadi.slnx --verify-no-changes

# 5. Style check (PYTHONUTF8 required — see encoding trap below)
$env:PYTHONUTF8=1
python tools\check_style.py <changed files...>
Remove-Item Env:\PYTHONUTF8

# 6. Regenerate API docs after ANY public type/member change
python tools\gen_api_docs.py
```

### Build-breaking style rules
- **CA1062:** `ArgumentNullException.ThrowIfNull` on every public method parameter (private methods exempt).
- **IDE0270:** use `?? throw` pattern, not `if (x == null) throw`.
- **IDE0005:** NO unused `using` directives — in `src/` AND `tests/`. Traps: (a) parent
  namespaces are implicit (inside `Foo.Bar.Baz`, `using Foo;` and `using Foo.Bar;` are
  redundant); (b) a `using` for a type that appears only as a method return type (never named
  in the body) is unused; (c) common: `PdfObjectId`/`PdfReference` live in
  `Chuvadi.Pdf.Primitives`, not `Chuvadi.Pdf.Objects`.
- **IDE0008:** no `var` in `src/` (tests are looser).
- **IDE0011:** braces on every control-flow statement.
- **IDE0060:** no unused parameters.
- One property per line in object initializers (whitespace check; compact
  `new Foo { A = 1, B = 2 }` fails).
- XML docs on every public member.
- **Test files (xUnit):** `xUnit1030` (never `.ConfigureAwait(false)` inside `[Fact]`),
  `xUnit2017` (`Assert.Contains(item, coll)` not `Assert.True(coll.Contains(item))`),
  `CA1861` (hoist `new[]{...}` to `private static readonly`, never inline in `[Fact]`).

### `check_style.py` REQUIRED_USINGS
Keyed by Primitives/Objects/Filters types only. The `Operations` (and most other) namespaces
have no entries, so new public types there need no registration. Only add to it if you add a
public type to a namespace that already has entries.

### Encoding traps (all have bitten before)
- PowerShell 5.1 `Get-Content` and Python default to **cp1252**, not UTF-8. Set
  `$env:PYTHONUTF8=1` for `check_style.py`. Several existing files have `0x9D` mojibake bytes
  in header-comment dividers (valid UTF-8, compiles fine, CI passes on Linux=UTF-8) that
  crash the checker under Windows cp1252 — `PYTHONUTF8=1` works around it.
- Deliver all `.cs`/`.md` as **UTF-8 no-BOM, LF** line endings. Verify with a byte check
  (no `EF BB BF` prefix, no `\r`) before zipping.
- Commit messages: `[System.IO.File]::WriteAllText($path, $msg, (New-Object System.Text.UTF8Encoding($false)))` then `git commit -F`.
- `git show > file` redirect in PowerShell produces UTF-16; `Out-File -Encoding utf8` adds a BOM. Avoid both.
- Browser downloads can double-encode UTF-8→cp1252→UTF-8 (mojibake). Use timestamped zips, not raw file downloads, and `Sort-Object LastWriteTime | Select -Last 1` to grab the newest.

### Git workflow
Feature branch → PR → 4 CI checks (style, docs-up-to-date, build matrix ubuntu/windows/macos)
→ squash-merge to `main` → tag `vX.Y.Z`. Branch protection requires all four checks.
After merge: delete branch (local + remote), `git remote prune origin`. Versioning is
**tag-only** (the stale `<Version>0.1.0</Version>` in `Directory.Build.props` is unused;
`build/pack.ps1 -Version X.Y.Z` overrides it).

### Packaging (local NuGet)
`.\build\pack.ps1 -Version 3.11.1` → cleans, builds Release, runs full tests, packs every
`src/` library + the `Chuvadi.Pdf` meta-package into `artifacts\nupkg`. 28 packages at last
run. Per-package metadata (Apache-2.0 license, icon.png, README, repository+commit) is wired.
**Symbols (`.snupkg`) are NOT emitted** — only matters for public nuget.org publish, not local.

---

## 4. THE 35-ITEM ROADMAP — verified status

Tags: **[BL]** = from the existing rendering/feature backlog. **[CMP]** = compression
workstream. Order respects dependencies (foundations first). Each item lists: status, where
the code lives (or where it would go), and notes.

### PHASE 0 — Cleanup + foundations (small, unblocks everything)

**1. [BL] WatermarkDocument lazy-load object-numbering fix** — ✅ **DONE**
`src/Chuvadi.Pdf.Watermark/WatermarkStamper.cs` (`WatermarkDocument` helper). `ForceLoadAll`
preloads the full object graph (BFS from trailer) before assigning object numbers — the same
class of fix applied to PageStamper. No latent lazy-load gap remains.

**2. [BL] DisplayList consolidation (merge the two parallel builders)** — ✅ **DONE**
*v3.16.0 update: further consolidated. Rendering is now three single-namespace projects with
acyclic layering Walking → DisplayList → Raster: `Chuvadi.Pdf.Rendering.Walking` (shared
content-stream walker; exposes internals to the two builders via `InternalsVisibleTo`),
`Chuvadi.Pdf.Rendering.DisplayList` (text/search display list), and
`Chuvadi.Pdf.Rendering.Raster` (raster display list). The old cross-folder duplication
(`src/Chuvadi.Pdf.Rendering/DisplayList/`) is gone; the "two parallel builders" debt is fully
resolved.*

**3. [CMP] Safety guard — detect signed/encrypted before any rewrite** — ✅ **DONE**
`src/Chuvadi.Pdf.Operations/PdfCompressor.cs`. `CompressionOptions.AllowSignedRewrite` /
`AllowEncryptedRewrite` default false; signed/encrypted docs are skipped with
`CompressionSkipReason.Signed` / `.Encrypted` and nothing is written. Rationale: rewriting
invalidates signatures and emits decrypted content.

**4. [CMP] Benchmark harness + corpus (size/SSIM vs Acrobat/Ghostscript/qpdf/MRC)** — ✅ **DONE** (v3.12.0, #110)
*v3.14.0 update: the external-tool scoreboard IS now wired — `ExternalToolScoreboard.Measure()` runs Ghostscript / qpdf / mutool over the corpus (`benchmarks/Chuvadi.Benchmarks/CompressionReport.cs`).*
`benchmarks/Chuvadi.Benchmarks.Compression/` exists with `Ssim.cs`, `CompressionCorpus.cs`,
`CompressionBaseline.cs`, `CompressionMeasure.cs`, and a `compression-baseline.json`. **The
external-tool comparison (vs Acrobat / Ghostscript / qpdf / an MRC tool) is NOT wired** —
no references to those tools found. *Remaining work: add the external-tool scoreboard so
later compression items can be measured against real-world baselines.*

### PHASE 1 — Lossless compression core (safe, universal, no quality loss)

**5. [CMP] Object streams + compressed xref — WRITING** — ✅ **DONE** (v3.12.0, #111)
*v3.14.0 update: `PdfWriter.Write(..., XrefStyle.Stream)` packs compressible objects into `/ObjStm` containers with a cross-reference stream (`src/Chuvadi.Pdf.IO/XrefStyle.cs`, `PdfWriter.WriteObjectStreamBody`). The highest-leverage lossless win has shipped.*
`PdfWriter` still emits classic cross-reference tables. `PdfCompressor` doc comment explicitly
calls object/xref-stream writing a "recorded follow-up." **Reading already shipped (v2.1.7).**
This is the highest-leverage lossless win and the natural first build after the Phase-0
scoreboard. Goes in `src/Chuvadi.Pdf.IO/` (writer) + wired through `PdfCompressor`.

**6. [CMP] Garbage collection + incremental-update flattening** — ✅ **DONE** (Phase 2 Bucket A)
*Bucket A update: verified — `PdfCompressor`'s reachability rewrite IS the flattening. The GC
walk keys reachable objects by number and stores the resolved (latest) version, renumbering
into one clean generation, so superseded generations and update orphans are dropped and the
`/Prev` chain disappears. Covered by `IncrementalFlatteningTests`.*
`PdfCompressor` removes orphan objects (`CompressionResult.ObjectsRemoved`). Full
incremental-update **flattening** (collapsing multiple update sections into one clean
generation) was **not confirmed** as a distinct, complete feature. *Verify what's actually
implemented vs. just orphan sweeping.*

**7. [CMP] General object deduplication (images, content/form XObjects, fonts)** — ✅ **DONE**
`PdfCompressor` (`CompressionResult.DuplicatesRemoved`). Confirm coverage across all object
kinds if extending.

**8. [CMP] Max-level lossless re-deflate (zopfli / libdeflate)** — ✅ **DONE** (Phase 2 Bucket A)
*Bucket A update: `DeflateEffort.Maximum` widens the encoder to a smallest-wins picker over
(a) the BCL deflater (`CompressionLevel.SmallestSize`), (b) the existing dynamic-Huffman path
(already shipped — the "fixed Huffman only" note below was stale), and (c) a from-scratch
iterated optimal ("zopfli-style") parse. Opt-in via `CompressionOptions.MaxDeflateStreams`.
~5-6% smaller than the default greedy path on realistic streams, with the optimal parse beating
the BCL baseline; every candidate is round-tripped through the inflater and emits valid zlib
(`MaxDeflateTests`).*
No zopfli/libdeflate implementation. Note B01 (zero deps) means this must be a from-scratch
implementation or a managed port — no external package. The existing deflater uses fixed
Huffman (~85-90% of zlib ratios); see also BACKLOG "Dynamic-Huffman DEFLATE."

**9. [CMP] Content-stream minification** — ✅ **DONE**
`src/Chuvadi.Pdf.Operations/ContentStreamMinifier.cs`
(`CompressionResult.ContentStreamsMinified`). Whitespace/comment minification.

**10. [CMP] Granular stripping (metadata/JS/attachments/Thumb/PieceInfo; optional struct-tree/annots)** — ✅ **DONE** (v3.13.1, #113)
*v3.14.0 update: `PdfCompressor` now exposes `RemoveJavaScript`, `RemoveAttachments`, `RemoveThumbnails`, `RemovePieceInfo`, `RemoveStructTree` (in addition to metadata/info).*
`PdfCompressor` has `RemoveMetadata` and `RemoveDocumentInfo`. JavaScript, attachments,
`/Thumb`, `/PieceInfo`, struct-tree, and annotation stripping are **not confirmed** — likely
not yet implemented. *Remaining work: the granular per-category strip flags.*

### PHASE 2 — Rendering & parsing completeness (conformance "Phase B"; after the DisplayList merge)

> *v3.16.0 update: several of these have since shipped.* Items 11 (shadings), 12 (gs
> transparency), and 13 (non-device colourspaces) are now **DONE** on the raster path;
> 14 (SVG colour), 15 (patterns), 16 (SVG ImageMask), and 17 (annotation appearance
> streams) remain **NOT DONE**. Surfaced originally by the code-survey conformance audit
> (`docs/CONFORMANCE-AUDIT.md`, 2026-06-15). Ordered by real-world impact.

**11. [BL] Shadings / gradients (`sh`)** — ✅ **DONE** (axial + radial, both sinks)
*v3.16.0 update: `sh` renders on BOTH sinks. The raster sink implements `PaintShading`
emitting `ShadeOp`, with per-pixel axial (type 2) and radial (type 3) rasterization in
`PageRasterizer` (clip- and Extend-aware); the SVG sink emits gradient defs via
`PdfFunction`/`PdfShading`. The earlier "raster still defers `sh`" note is stale. Remaining:
function-based (type 1) and mesh (types 4–7) shadings. *High impact — the common gradient
cases now render.**

**12. [BL] ExtGState transparency (`gs`)** — ✅ **DONE**
*v3.16.0 update: `gs` is fully honoured. `Raster/BuilderGraphicsState.cs` carries constant
alpha (`/ca`, `/CA`), the active separable blend mode (`/BM`, PDF §11.3.5), and the active
soft mask (`/SMask`); the raster sink composites these per-pixel through `ScanlineRasterizer`
and the SVG sink emits `mix-blend-mode` groups and `<mask>` defs. The only remaining
sub-piece is isolated/knockout transparency-group compositing (deferred). The earlier
"still missing blend modes / soft masks" note is stale.*

**13. [BL] Non-device colorspaces** — ✅ **DONE** (raster)
*v3.16.0 update: the raster sink now resolves non-device fill/stroke and image colours
through their colour space — Separation/DeviceN tint-transform evaluation, Indexed
lookup, ICCBased alternate, Lab, and Cal* — in `Raster/DisplayListBuilder.cs`
(SetColorN and the image sample path). The old "operands interpreted by count" behaviour
is gone for the raster path. The SVG sink's `cs`/`scn` colour handling is still open —
see item 14. Raw DeviceCMYK image samples and full CMYK render remain partial — see
item 38.*

**14. [BL] SVG `cs`/`scn` colorspace operators (pairs with 13)** — ❌ **NOT DONE**
The SVG sink ignores `cs`/`scn` colour operators (raster implements some). Pair with item 13.

**15. [BL] Patterns / tiling** — ❌ **NOT DONE**
Tiling (PatternType 1) and shading (PatternType 2) patterns not painted; `scn` with a pattern
name is suppressed. Needs pattern-cell replay + shading-pattern fill. *Medium impact.*

**16. [BL] SVG ImageMask** — ❌ **NOT DONE**
Raster handles `/ImageMask true` (v2.8.0); the SVG renderer skips it. Apply the stencil with
the current fill colour as an RGBA `<image>` (reuse the RGBA-PNG machinery from the v3.6.0
`/SMask` work). Distinct from `/SMask` (already handled in SVG).

**17. [BL] Annotation appearance streams (`/AP /N`)** — ❌ **NOT DONE**
The render pipeline doesn't draw page `/Annots` `/AP /N` appearance streams, so form fields,
widgets, stamps, ink annotations are invisible in rendered output (though readable).
*Medium/high impact.*

### PHASE 3 — Image recoding (biggest practical compression wins for scan/photo mix)

**18. [CMP] Re-encode existing JPEG/DCT images** — ✅ **DONE**
`PdfCompressor` (`RecompressImages`, `JpegQuality` default 75, `MinImagePixelsToRecompress`
default 4096, `CompressionResult.ImagesRecompressed`).

**19. [CMP] Image downsampling to target DPI** — ❌ **NOT DONE**
Resampling exists inside the rasterizer (`PageRasterizer`) but NOT as a compression-path
"downsample image XObjects to a target DPI" feature. Needed as a dependency for MRC (#29) and
the perceptual target (#32).

**20. [CMP] Smart per-image codec selection (indexed/palette, bit-depth, colorspace/ICC reduction)** — ❌ **NOT DONE**
No per-image codec/bit-depth/palette decision logic.

### PHASE 4 — Codecs: bitonal, JBIG2, JPEG2000, fonts (decode then encode where they pair)

**21. [CMP] Bitonal/grayscale detection + CCITT Group 4** — ✅ **DONE (decode)**
`src/Chuvadi.Pdf.Filters/CcittFaxFilter.cs` implements `CCITTFaxDecode` (Group 3/4). *Confirm
whether bitonal/grayscale **detection** for the compression path (deciding when to re-encode
as G4) is wired, vs. just the decode filter.*

**22. [BL] JBIG2 decode** — ✅ **DONE (decode)**
`src/Chuvadi.Pdf.Filters/Jbig2Filter.cs` implements `JBIG2Decode` (arithmetic): MQ
arithmetic coder, segment headers, generic regions (templates 0-3, TPGDON), symbol
dictionaries, and text regions. Shared `/JBIG2Globals` segments are supplied via
`FilterParameters.Jbig2Globals`. Conformance-verified against a real text-region sample
(symbol dictionary in globals + text region in the image). *Remaining sub-features —
Huffman, refinement/aggregate, transposed text, MMR-coded generic — fail clearly and
are rare follow-ons.*

**23. [CMP] JBIG2 encode (shares the segment model with 22)** — ❌ **NOT DONE**
Depends on 22.

**24. [BL] JPX / JPEG2000 decode** — ❌ **NOT DONE**
No JPX decoder — renders blank. *Very large (wavelets, EBCOT).*

**25. [CMP] JPEG2000 (JPX) encode (optional)** — ❌ **NOT DONE**
Only worth doing if it beats DCT+MRC. Depends on 24.

**26. [BL] Type3 fonts (rendering)** — ✅ **DONE** (Phase 2 Bucket B)
*Bucket B update: Type 3 glyphs (CharProc content streams) now render on BOTH sinks. `d0`/`d1` added to the operator sink + walker; `Type3Font` parses FontMatrix, Encoding/Differences → CharProcs, Resources, and widths. Raster places each cached glyph via `NestedDisplayListOp`; SVG via a `Type3UseOp` transform group. d1 uncoloured glyphs paint in the text colour (colour ops suppressed); d0 coloured glyphs keep their own colour. Glyphs are cached per font/code/fill. Covered by `RasterType3Tests` + `SvgType3Tests`.*
No `CharProcs`/`d0`/`d1` handling; Type3 text (glyphs defined as content streams) doesn't
render. *Medium impact.*

**27. [CMP] Font subsetting + de-duplication** — ✅ **DONE**
`src/Chuvadi.Pdf.Authoring/TrueTypeSubsetter.cs` (shipped v3.4.0) — embeds only used glyphs,
drops non-rendering tables (GSUB/GPOS/cmap/post), preserves numbering for Identity CID→GID.

### PHASE 5 — Research-grade & advanced (each its own multi-round effort)

**28. [BL] TrueType bytecode hinting interpreter** — ✅ **DONE** (plan flagged "verify")
`src/Chuvadi.Pdf.Fonts.Rendering/Hinting/` — a substantial implementation (~3,240 lines total:
`HintingInterpreter.cs` ~2,645, plus `F26Dot6`, `F2Dot14`, `GraphicsState`, `Zone`,
`RoundState`, `HintingLimits`, `RawGlyph`). Shipped over the v2.2–v2.6 arc, including composite
glyph hinting. `RenderOptions.Hinting` defaults to `HintingMode.Light`. **Not mid-flight — it's
done.** (Possible follow-ups remain: composite-glyph Y-fitting in unhinted fonts; optional
X-axis stem fitting — these are BACKLOG "Autohinter follow-ups," minor.)

**29. [CMP] MRC (Mixed Raster Content) — the color-scan differentiator** — ❌ **NOT DONE**
Depends on 19 (downsampling), 21 (bitonal detection/G4), 23 (JBIG2 encode). The big
color-scan compression win.

**30. [BL] Indic / complex-script shaping** — ✅ **DONE**
*v3.16.0 update: shipped as `src/Chuvadi.Pdf.Text.Shaping/` — `TextShaper`, `LipiScript`,
`ShapingFeatures`, `ShapedGlyph`. Applies OpenType GSUB/GPOS features (ligatures/conjuncts,
mark positioning) with Indic reordering, driven from the tables the embedded fonts already
carry. Covered by `Chuvadi.Pdf.Text.Shaping.Tests`.*

**31. [BL] XFA** — ✅ **DONE**
*v3.16.0 update: shipped as `src/Chuvadi.Pdf.Xfa/` — a complete template/layout/scripting
engine (20 files across Parse/Model/Layout/Render/Scripting). Data merge
(`XfaDataMerge`), positioned + flowed layout (`XfaLayoutEngine`), pagination with a
duplex/keep solver (`XfaPaginator`), tables and widgets (`XfaWidgetPainter`,
`XfaContentEmitter`), and the FormCalc + JavaScript scripting engines
(`XfaFormCalcEngine`, `XfaJavaScriptEngine`, `XfaScriptHost`, `XfaScriptRunner`,
`XfaScriptValue`). `XfaRenderOptions.ScriptMode` defaults to `Full`, so
initialize/calculate/validate scripts run by default (pass `ScriptMode.None` to opt out).
Covered by `Chuvadi.Pdf.Xfa.Tests` (82 tests).*

### PHASE 6 — Perceptual, delivery & app-facing

**32. [CMP] SSIM perceptual target ("smallest file at visually lossless")** — ❌ **NOT DONE**
SSIM *measurement* exists (`benchmarks/.../Ssim.cs`), but the optimization *target knob* that
drives image items 18–29 does not. Depends on the whole image-recoding stack.

**33. [CMP] Linearization + compliance modes (PDF/A, PDF/UA)** — 🟡 **SPLIT**
- **Linearization:** ✅ DONE (`src/Chuvadi.Pdf.IO/LinearizationReader.cs`,
  `LinearizationInfo.cs`, and `LinearizedWriter` via `PdfWriter.WriteLinearized`).
- **PDF/A & PDF/UA compliance modes** (auto-disable forbidden optimizations, generate
  `/StructTreeRoot`): ❌ NOT DONE (BACKLOG "Tagged PDF / PDF-A"). *Large.*

**34. [BL] Bench batch #2 — raster 4-point perspective deskew** — ❌ **NOT DONE**
A Chuvadi Reader "Bench" image-processing feature (4-point perspective correction / deskew).

**35. [BL] Image→PDF + report generation (Lipi HIS / SIGMA)** — ✅ **DONE in the library**
`src/Chuvadi.Pdf.Authoring/`: `ImagePdfConverter` (image→PDF) and `ReportBuilder` +
`ReportLayoutEngine` (report generation), shipped v2.7.0. **Wiring these into Lipi HIS / SIGMA
is application-side work, outside the Chuvadi repo.**

## Item 36 — Watermark custom-font embedding (LiPi / Indic)  [Phase: last]

Status: deferred (explicitly parked, not scheduled).
Depends on: Arun supplying a licensable .ttf (Calibri DROPPED — proprietary,
cannot be bundled or embedded).

Goal: let TextWatermarkOptions embed a user-supplied TrueType font instead of
being limited to the Base-14 set. Today TextWatermarkOptions.FontName only
references a Standard-14 face and the watermark path hardcodes a Helvetica
font resource — it embeds nothing (latent bug: FontName is effectively
ignored for embedding).

Scope:
- Route watermark text through the existing TrueTypeFontEmbedder
  (Type0 / CIDFontType2 / Identity-H / FontFile2 / subset / ToUnicode) that
  PdfDocumentBuilder.AddTrueTypeFont + PageBuilder.DrawText already use.
- Wire a user-supplied .ttf into WatermarkStamper (text + ExtGState path).

Hard blocker for correct Indic output: no complex-script shaper exists.
Latin renders correctly; Indic only renders for isolated / pre-ordered
glyphs. Correct conjuncts and reordering need a shaping pass first — treat
the shaper as its own sub-item gating any "LiPi watermark renders correctly"
acceptance.

Acceptance:
- A watermark using an embedded .ttf round-trips and renders (Latin) with the
  font actually embedded + subset, verified via SVG/raster output.
- Indic correctness gated behind the shaper sub-item.

---

## Item 37 — Image alpha round-trip audit  [Phase: completeness]

Status: ✅ DONE (this session). No code change required — alpha is handled on
every image path; the gap was a missing test, now added.

Findings (verified against `main`):
- Embed: `ImageEmbedder` carries a source alpha channel as a DeviceGray /SMask
  image (FlateDecode raw samples or DCTDecode passthrough; no hex encoding).
  Covered by `ImageAuthoringTests.DrawImage_RgbaPng_EmitsSoftMask` (+ siblings).
- SVG sink: image /SMask and /Matte applied. Covered by `ImageSoftMaskTests`
  and `ImageMatteTests`.
- Raster sink: `DisplayListBuilder.ApplySoftMask` reads the image /SMask into
  per-pixel alpha; `PageRasterizer.CompositeImage` blends it (and folds the
  ExtGState /ca in). This path had NO test — closed by the new
  `RasterImageSoftMaskTests.ImageSoftMask_TransparentSamples_ShowBackground`
  (negative-checked: disabling the SMask lookup makes it fail).

## Item 38 — CMYK image support  [Phase 2 — rendering completeness, pairs with #13]

Status: 🟡 PARTIAL.

*v3.16.0 update: CMYK/YCCK JPEG passthrough now SHIPS. `ImageEmbedder` embeds a
4-component JPEG directly under DCTDecode with `/ColorSpace /DeviceCMYK` and the
Adobe-inversion `/Decode` array when the APP14 marker signals it, instead of
decoding to RGB — the exact ink values are preserved for print/prepress. What
remains: raw (non-JPEG) DeviceCMYK sample embedding under FlateDecode, and full
CMYK-to-output conversion in the raster/SVG sinks so CMYK images display in
rendered output. Pairs with item 13 for ICCBased-CMYK.*

Scope:
- Embed: preserve DeviceCMYK images — CMYK JPEG (4-component) passthrough under
  DCTDecode with /ColorSpace /DeviceCMYK and the Adobe-inversion /Decode when
  present; raw DeviceCMYK samples under FlateDecode.
- Decode/render: CMYK-to-output conversion in the raster and SVG sinks (and the
  image decoders) so CMYK images display.
- Pairs with item 13 (non-device colorspaces) for ICCBased-CMYK.

Acceptance:
- A CMYK JPEG embeds as DeviceCMYK (no RGB round-trip), round-trips, and renders
  with colours matching a reference decoder.

---

## 5. Status summary

| Phase | Items | Done | Partial | Not done |
|-------|-------|------|---------|----------|
| 0 — Cleanup + foundations | 1–4 | 1,2,3,4 | — | — |
| 1 — Lossless compression | 5–10 | 5,7,9,10 | 6 | 8 |
| 2 — Rendering completeness | 11–17, **38** | — | 11,12 | 13,14,15,16,17,**38** |
| 3 — Image recoding | 18–20 | 18 | — | 19,20 |
| 4 — Codecs | 21–27 | 21,22,26,27 | — | 23,24,25 |
| 5 — Research-grade | 28–31 | 28 | — | 29,30,31 |
| 6 — Perceptual/delivery | 32–35 | 33(lin.),35 | 33(PDF/A) | 32,34 |
| Completeness (added) | 37–38 | 37 | — | 38 |

**Done (16):** 1, 2, 3, 4, 5, 7, 9, 10, 18, 21, 22, 26, 27, 28, 35, 37 (+ linearization half of 33).
**Partial (4):** 6, 11, 12, 33 (PDF/A side open).
**Not done (17):** 8, 13, 14, 15, 16, 17, 19, 20, 23, 24, 25, 29, 30, 31, 32, 34, 38.
**Deferred (1):** 36 (watermark custom-font — needs a licensable .ttf).

> **Moved since the v3.11.1 snapshot (PRs #110–#119):** #4 and #5 → DONE (external
> scoreboard #110; `/ObjStm` + xref-stream writing #111), #10 → DONE (granular strip
> flags #113), #11 → PARTIAL (SVG `sh` #115; raster still deferred), #12 → PARTIAL
> (constant `/ca` `/CA` #111). **Phase 1 lossless is effectively complete (only #8 +
> the #6 flattening tail remain); Phase 2 rendering completeness is now the frontier.**
>
> **Out-of-roadmap stream — Redaction (shipped, not one of the 38):** physical
> annotation/form/vector redaction (R1, #117), the form-XObject text-leak fix (#114),
> glyph-level over-removal fix + in-place replacement (#118), and checksum-validated
> patterns/sets (#119). Tracked here for completeness; it was consumer-driven, outside
> the compression/rendering plan.

**Where the open work concentrates:** the **compression workstream** (Phase 1 finish + Phase 3
+ Phase 4 codecs + MRC) and the **rendering-conformance backlog** (Phase 2). The library's
foundational, font, and authoring layers are mature.

---

## 6. Recommended next pickup order

> **Refreshed at v3.14.0.** The original plan's first three steps (#4, #5, #10) have all
> shipped, so the sequence below picks up from the new frontier.

**Phase 1 (lossless) — essentially complete.** Only two tails remain, both optional polish:
- **#8** max-level lossless re-deflate (dynamic-Huffman DEFLATE / zopfli-class) — from-scratch
  per B01. The current fixed-Huffman deflater is ~85–90% of zlib ratios.
- **#6** finish/verify incremental-update flattening (orphan sweep exists; full update-section
  collapse not confirmed).

**Phase 2 (rendering & parsing completeness) — now the main frontier, highest user-visible
impact.** Suggested order, grouping the colorspace work together:
1. **#13 non-device colorspaces + #38 CMYK image support** — do these together. They share the
   decode/render colorspace plumbing: #13 brings ICCBased/Separation/Indexed/CalRGB/Lab and
   DeviceCMYK *fills*; **#38 makes images carry DeviceCMYK end-to-end** (CMYK-JPEG passthrough,
   raw CMYK samples, Adobe `/Decode`, and CMYK→output conversion in the sinks) instead of the
   current lossy RGB re-embed. **#38 is filed under Phase 2 (completeness), paired with #13.**
2. **#14 SVG `cs`/`scn` colorspace operators** — pairs with #13 on the SVG side.
3. **#11 raster shadings** — finish the `sh` path the raster sink still defers (SVG shipped in
   #115); then ShadingTypes 4–7.
4. **#12 transparency** — blend modes (`/BM`), soft masks in an ExtGState, transparency groups
   (constant `/ca` `/CA` already done).
5. **#16 SVG ImageMask**, **#17 annotation appearance streams (`/AP /N`)**.

**Then** either the image-recoding compression chain (**#19 → #20**) for file-size wins, or the
research-grade items.

The research-grade items (**#23 JBIG2 encode, #24 JPX, #29 MRC, #30 Indic, #31 XFA**) are each their own
multi-round effort and should be scheduled deliberately, not picked up casually.

**My recommendation:** start the **#13 + #38 colorspace/CMYK pair** — it's the most coherent next
unit (one body of colorspace work, high fidelity payoff for print/prepress consumers), and it
unblocks #14.

---

## 7. Other known open items (NOT in the 35, but real — flag to Arun)

- **Backlog #19 — broader xref-offset recovery on load.** The narrow page-tree case shipped in
  v3.11.1 (`PageTreeRecovery`, surfaced via `PdfDocument.Warnings`/`IsRecovered`). The deferred
  broad work: validate ANY resolved object against its xref offset and fall back to a
  full-file definition scan when provably wrong — opt-in / confined to objects that fail a
  type/role check, so healthy files keep the fast strict path.
- **`OutlineReader.DestinationPageIndex` storage-order limitation.** Derives the page index via
  object-storage order, returning `-1` on some files even though written destinations are
  spec-correct (pikepdf navigates them fine). Fix: walk `/Kids` instead of storage order. Only
  matters if a consumer relies on round-tripping bookmark destinations.
- **NuGet `.snupkg` symbols** not emitted by `build/pack.ps1` — only relevant for public
  nuget.org publish (add `<IncludeSymbols>true</IncludeSymbols>` +
  `<SymbolPackageFormat>snupkg</SymbolPackageFormat>` to `Directory.Build.props`).

---

## 8. Key reference docs in the repo

- `docs/BASELINE.md` — architectural invariants (read before writing any code).
- `docs/CHANGE-LOG.md` — `A`-numbered decision log; phase-completion records.
- `docs/BACKLOG.md` — open roadmap (the `[BL]` items map here; item #19 added v3.11.1).
- `docs/CONFORMANCE-AUDIT.md` — the 2026-06-15 rendering-gap survey (source of Phase-2 items).
- `docs/DISTRIBUTION.md` — packaging/publishing notes.
- `docs/api/` — auto-generated API docs (regenerate via `python tools\gen_api_docs.py`).
- `build/pack.ps1`, `build/publish.ps1` — packaging.
- `tools/check_style.py`, `tools/gen_api_docs.py` — gate tooling.

---

## 9. First-message template for the fresh chat

> *Paste this document, then something like:*
>
> "You're picking up Chuvadi (சுவடி), my zero-NuGet .NET 10 PDF library — full context in the
> attached handoff. Repo `arunshivab/chuvadi-pdf` (public), local at
> `C:\Users\aruns\Documents\Chuvadi\chuvadi-scaffold\chuvadi`, currently v3.11.1. Read the
> working agreement and the build/gate process — they're hard rules. I want to start on
> **[item number + name]**. First, reproduce/verify current state in your sandbox (clone, build)
> before proposing anything — no guessing, full files only, and hold all git ops until I say so."

*End of handoff.*
