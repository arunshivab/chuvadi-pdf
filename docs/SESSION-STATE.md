# SESSION-STATE.md — Current Build State

> Read this first each session, then CHANGELOG.md (root), then BASELINE.md.
> Rules and pitfalls live in CLAUDE.md — not here.
> Architectural decisions and rationale live in docs/CHANGE-LOG.md
> (append-only, numbered A01..ANN).

---

## Last Updated

2026-10-22 — v3.22.1: tiling-pattern cells under redaction (backlog 18) —
cell content a box would show is redacted per tile copy (text, paths,
pixel-blanked images, nested patterns); the cell is kept whole only when a
tile elsewhere on the visible page provably shows it entirely;
`RedactionVerifier` inspects pattern cells. Released: v3.22.0 (2026-10-22:
partial image redaction, Scrub residuals, SVG pattern strokes, multi-line
API docs). Last released version: v3.22.0. Open: BACKLOG 17 (Scrub
residuals), 18 (pattern-cell edges), 19 (verifier on palette images).

---

## Build Summary

**Last known passing total: 2,707 tests across 30 in-solution test projects,
0 failures (v3.22.1 sandbox gate).** (Two further test projects — the WPF surface and the WASM smoke
test — build and run outside the default solution test pass.)

The library is **33 packable `src/` projects** (32 modules plus the
`Chuvadi.Pdf` meta-package), all on .NET 10, all with zero NuGet dependencies
in production code (B01).

---

## Module Status

All modules are Complete and shipping. Grouped by role:

### Core read/write pipeline
| Module                          | Status   |
|---------------------------------|----------|
| Chuvadi.Pdf.Primitives          | Complete |
| Chuvadi.Pdf.Filters             | Complete |
| Chuvadi.Pdf.Objects             | Complete |
| Chuvadi.Pdf.IO                  | Complete |
| Chuvadi.Pdf.Documents           | Complete |
| Chuvadi.Pdf.Content             | Complete |
| Chuvadi.Pdf.Encryption          | Complete |
| Chuvadi.Cryptography            | Complete |

### Fonts and text
| Module                          | Status   |
|---------------------------------|----------|
| Chuvadi.Pdf.Fonts               | Complete |
| Chuvadi.Pdf.Fonts.Rendering     | Complete (TrueType bytecode hinting) |
| Chuvadi.Pdf.Fonts.Woff2         | Complete |
| Chuvadi.Pdf.Text                | Complete |
| Chuvadi.Pdf.Text.Shaping        | Complete (OpenType GSUB/GPOS) |

### Rendering
| Module                            | Status   |
|-----------------------------------|----------|
| Chuvadi.Pdf.Graphics              | Complete |
| Chuvadi.Pdf.Color                 | Complete |
| Chuvadi.Pdf.Images                | Complete |
| Chuvadi.Pdf.Rendering.Walking     | Complete (shared content-stream walker) |
| Chuvadi.Pdf.Rendering.DisplayList | Complete (text/search display list) |
| Chuvadi.Pdf.Rendering.Raster      | Complete (raster display list) |
| Chuvadi.Pdf.Rendering             | Complete (scanline rasterizer) |
| Chuvadi.Pdf.Rendering.Wpf         | Complete (Windows-only) |
| Chuvadi.Pdf.Svg                   | Complete |

### Documents, editing, output
| Module                          | Status   |
|---------------------------------|----------|
| Chuvadi.Pdf.Operations          | Complete |
| Chuvadi.Pdf.Authoring           | Complete |
| Chuvadi.Pdf.Watermark           | Complete |
| Chuvadi.Pdf.Redaction           | Complete |
| Chuvadi.Pdf.Annotations         | Complete |
| Chuvadi.Pdf.Forms               | Complete |
| Chuvadi.Pdf.Xfa                 | Complete (rendering + FormCalc/JS scripting) |
| Chuvadi.Pdf.Signatures          | Complete (verify, sign, timestamp, LTV) |
| Chuvadi.Pdf.PdfA                | Complete |
| Chuvadi.Pdf.Reader              | Complete (high-level facade) |
| Chuvadi.Pdf                     | Complete (meta-package) |

---

## Current Architecture Notes

- **XFA arc complete.** `Chuvadi.Pdf.Xfa` renders XFA forms end to end: data
  merge, positioned/flowed layout, pagination, duplex + keep solver, tables,
  widgets, and the FormCalc + JavaScript scripting engines
  (`Xfa/Scripting`: `XfaScriptHost`, `XfaScriptValue`, `XfaJavaScriptEngine`,
  `XfaFormCalcEngine`, `XfaScriptRunner`, `XfaScript`). `XfaRenderOptions.ScriptMode`
  **defaults to `Full`** — initialize/calculate/validate scripts run by default;
  pass `ScriptMode.None` to opt out. Unsupported script constructs fail soft.
- **Rendering is three single-namespace projects**, acyclic layering
  Walking → DisplayList → Raster. `Rendering.Walking` is a leaf (Filters/Objects/
  Primitives) exposing its internals to the two builders via `InternalsVisibleTo`.
  The old cross-folder DisplayList duplication is gone.
- **Hybrid-reference xref works.** `PdfReader.LoadXrefChain` reads `/XRefStm`
  in the classic-xref branch, so compressed objects (e.g. `/StructTreeRoot`,
  `/MarkInfo`) on Word/Office PDFs resolve; `HasStructTree` / `IsTagged` are correct.
- **Annotation appearances render everywhere.** All three sinks draw each
  visible annotation's `/AP /N` form placed per §12.5.5
  (`PageAnnotationAppearances` in Documents is the shared resolver); text
  extraction includes appearance text. Hybrid XFA documents (MCA certificates
  and similar) show, search, and flatten their field values through the
  ordinary open/render/extract/flatten APIs — no XFA-awareness needed in
  consumers.
- **Xref chains follow §7.5.6.** The newest incremental-update section
  supersedes older ones for entries of any kind; free entries shadow older
  definitions (no resurrection) and compressed entries are not replaced by
  older uncompressed ones.
- **Redaction-grade crop is done.** `PageCropMode.ClipOnly` (lossless) and
  `PageCropMode.Scrub` (byte-scrub via `PageScrubber`/`ScrubGeometry`).
- **Redaction matches text in page space.** Glyph boxes are tested under
  text matrix · CTM · form base (and also the legacy text-matrix-only frame;
  a glyph goes if either hits), with `Ts` rise and `Tz` scaling applied.
  `RedactionVerifier` re-checks any output against the rectangles using the
  render display list.
- **Optional content is honoured.** Renderers, inventory, text runs and
  `TextExtractor` skip layers hidden by the default configuration
  (`PdfPage.OptionalContent`); `IgnoreOptionalContent` / `LayerVisibility`
  override per render. Redaction, Scrub and `RedactionVerifier` always see
  every layer.
- **Lazy recovery warnings.** `PdfReader.Warnings` grows when a damaged object
  is first loaded; a stale xref entry with no definition resolves to null.

---

## Packaging / Release

`build\pack.ps1 -Version x.y.z` cleans, builds Release, runs the full test
suite, then writes 33 mono-versioned `.nupkg` to `artifacts\nupkg`. Publish by
flat-copying `artifacts\nupkg\*.nupkg` into `C:\Users\aruns\Documents\local-nuget`
— the library's job ends there; the Chuvadi Reader picks packages up from that
feed on its own side. Never `dotnet nuget push` (it creates a mismatched
hierarchical layout). Tag `vX.Y.Z` after packing. Latest released version:
**3.22.0** (3.22.1 pending merge + pack on Arun's machine).

Releases publish to local folder feeds only, never to nuget.org.

---

## Open / Deferred

- ~~ScriptMode.Full visual check~~ — **closed**: the Reader renders the COI
  fully, but the COI has no script-computed fields (None / CalculationsOnly /
  Full render byte-identically), so it does not exercise the script engines.
  A form with calculated fields is needed to verify Full end to end.
- ~~Scrub/redaction leak~~ — **fixed in 3.19.0**: Scrub kept the original
  content streams (and replaced images/forms) in the file through 3.18.1;
  redaction kept removed images. Documents scrubbed before 3.19 are not
  redacted and should be re-cropped.
- ~~Meta-package coverage~~ — **closed in 3.18.0**: `Chuvadi.Pdf` now lists
  Xfa, PdfA, Text.Shaping, Rendering.Raster and Rendering.Walking directly.
- ~~gen_api_docs filename collision~~ — **closed in 3.18.0**: duplicate type
  names within a module are suffixed with their last namespace segment
  (`DisplayListBuilder.DisplayList.md` / `DisplayListBuilder.Raster.md`).
- **XFA geometry nulls** — closed as correct behaviour: the four COI dataset
  nodes without `Geometry` (`YEAR_CURRENT_NXT`, `NAME_ROC`, `ADDRESS_ROC`,
  `STATE_NAME`) have NO corresponding template field (template refs = 0), so
  every laid-out field carries geometry (15/15).
- **Shared value-types move** — documented defer (see BACKLOG item 6): plan is
  a move into `Rendering.Walking` with `TypeForwardedTo` shims, at a
  major-version boundary.
- ~~Text under `cm` / `Ts` / `Tz` escaping redaction~~ — **fixed in 3.20.0**
  (adversarial review). Documents redacted before 3.20 whose text is placed
  through a content-stream `cm` should be re-redacted.
- ~~Pixel-level partial image redaction~~ — **shipped in 3.22.0**: only the
  covered pixels are blanked.
- ~~Pattern cells under redaction~~ — **fixed in 3.22.1** (BACKLOG 18 lists
  the remaining edges).
- ~~Tagged-image leak~~ — **fixed in 3.22.0**: a structure-tree `OBJR` to an
  image removed by redaction or Scrub kept its pixels in the file. Documents
  processed before 3.22 with tagged images should be processed again.

See `docs/BACKLOG.md` for the full open roadmap.

---

## Deploy Script Status

`build\pack.ps1` / `build\publish.ps1` drive packaging and local-feed
distribution. Single-backslash Windows paths; audit on each release.
