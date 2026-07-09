# SESSION-STATE.md — Current Build State

> Read this first each session, then CHANGELOG.md (root), then BASELINE.md.
> Rules and pitfalls live in CLAUDE.md — not here.
> Architectural decisions and rationale live in docs/CHANGE-LOG.md
> (append-only, numbered A01..ANN).

---

## Last Updated

2026-07-08 — hybrid-XFA "just works" arc merged (targeting v3.17.0):
annotation appearance rendering (§12.5.5) on all sinks, appearance text in
extraction, `XfaDataField.Geometry` via the template bind map, xref-chain
precedence fix (§7.5.6 — newest section supersedes, free entries shadow),
consolidated literal-string escape decoding, and the flattener B16 preload
fix. Last released version: v3.16.0.

---

## Build Summary

**Last known passing total: 2,413 tests across 30 in-solution test projects,
0 failures.** (Two further test projects — the WPF surface and the WASM smoke
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

---

## Packaging / Release

`build\pack.ps1 -Version x.y.z` cleans, builds Release, runs the full test
suite, then writes 33 mono-versioned `.nupkg` to `artifacts\nupkg`. Publish by
flat-copying `artifacts\nupkg\*.nupkg` into the two local feeds
(`C:\Users\aruns\Documents\local-nuget` and the Chuvadi Reader's `localpackages`)
— never `dotnet nuget push` (it creates a mismatched hierarchical layout). Tag
`vX.Y.Z` after packing. Latest released version: **3.16.0**.

Releases publish to local folder feeds only, never to nuget.org.

---

## Open / Deferred

- **Deferred visual check:** confirm the `ScriptMode.Full` default fills the
  COI's scripted fields via the Reader app once it references 3.16.0. CI-green
  is not the same as visually confirmed.
- **Meta-package coverage:** `Chuvadi.Pdf` does not yet reference the newer
  modules (Xfa, PdfA, Text.Shaping, Color, Rendering.Raster, Rendering.Walking).
  Consumers of the umbrella get most of the graph transitively, but the direct
  dependency list is worth reconciling.
- **Future-ticket candidates (low priority):** `gen_api_docs.py` doc-filename
  collision for the two `DisplayListBuilder` / `PageDisplayList` type-name pairs
  (cosmetic); moving shared value types (PdfBlendMode, BlendModes, Type3Font,
  geometry) out of `Rendering.DisplayList` into a shared/common project so
  `Rendering.Raster` need not depend on the text display list.

See `docs/BACKLOG.md` for the full open roadmap.

---

## Deploy Script Status

`build\pack.ps1` / `build\publish.ps1` drive packaging and local-feed
distribution. Single-backslash Windows paths; audit on each release.
