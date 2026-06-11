# Chuvadi — Session Handoff (2026-06-11)

## Current state
- **Branch:** `main`, clean, up to date with `origin/main`.
- **Latest commit:** `36875b7` — composite hinting test coverage (#80).
- **Latest tag:** `v2.6.0` (`f68210f`).
- **Tests:** full suite green; `Chuvadi.Pdf.Fonts.Rendering.Tests` = **222**.
- **Repo:** `arunshivab/chuvadi`, local `C:\Users\aruns\Documents\Chuvadi\chuvadi-scaffold\chuvadi`, Windows/PowerShell 5.1/.NET 10.

## Work completed (recent -> older)
1. **Composite glyph hinting test coverage (#80, v2.6.0).** New `tests/Chuvadi.Pdf.Fonts.Rendering.Tests/CompositeHintingTests.cs` — a synthetic in-memory TTF (base box glyph + composite referencing it, grid-rounded Y offset + instruction stream) exercising the hinted composite path end to end: assembles/hints, grid-rounded offset, **org<-cur baseline lock**, simple-no-instructions->null, scaled-component bail. 5 tests.
2. **Composite glyph hinting (#79, v2.6.0).** Accented/component glyphs now hinted instead of unhinted fallback. Each component hinted as its own glyph, translated by its (grid-rounded when `ROUND_XY_TO_GRID`) offset, merged; composite's own instruction stream runs over the assembly. **org<-cur semantics**: before a composite program runs, assembled current coords are copied into original (cut-ins/SHC/IP measure from the assembly, not the unhinted design) — diagnosed on the dotted glyph, validated against FreeType interpreter **v35** (classic spec; v40 "minimal" intentionally not matched). Also fixed **SHC/SHZ** to skip the reference point (was double-shifting). Scope: XY-offset composites fully hinted; scaled/2x2/anchor-point/depth>3 -> unhinted fallback. Decision log **A27**.
3. **Autohinter Component 2 (#78)** — per-font blue zones (`BlueZone.cs`, `BlueZoneTable.cs`, `BlueZoneBuilder.cs` + tests). **Banked, unused.**
4. **Autohinter Component 1 (#77)** — vertical stem detection (`Stem.cs`, `StemDetector.cs` + tests). **Banked, unused.**
5. **v2.5.1 (#76)** — Full-mode hinted advance (pp2-pp1); restored dropped v2.5.0 changelog + decision-log A25.
6. **v2.5.0 (#75)** — Stage 7: wired TrueType hinting into raster; MSIRP/RTDG opcode fix (fixed half-missing "W"); `HintingMode.Light` default.

## Decisions reached (don't relitigate)
- **Sub-pixel/LCD rendering: rejected.** Chuvadi is "a library for all, any device" — baking LCD stripe geometry into portable output fringes on non-matching displays. Disqualified by use case.
- **Full-mode stem narrowing at 150 DPI: inherent, not a bug.** Whole-pixel X grid-fitting can't preserve width AND keep integer-positioned stems (proven with the `n`'s coordinates). **Light is the correct grayscale default.** Full is correct for B&W/low-res.
- **`StemFitter`/Component 3: not built.** Its ceiling is ~=Light at 150 DPI; can't beat the default on grayscale.
- **Autohinter C1/C2:** kept as infrastructure for a *future* Y-fitting fallback for **unhinted** fonts (the one place they genuinely help).

## Backlog (next session — Arun wants "one by one")
- **#2 — Image -> PDF.** Smallest; Arun raised it. **Open question to resolve first:** is the need (a) standalone wrap-an-image->PDF, (b) images-inside-generated-reports (really part of #3), or both? Next step: recon the authoring/content API (`Chuvadi.Pdf.Authoring`, `Chuvadi.Pdf.Content`, `Chuvadi.Pdf.Images`) to see if image-on-page already works.
- **#3 — Report-layout helper (Arun explicitly wants this).** Generic "headers + rows + column widths -> paginated PDF table with repeating headers," **in-memory and optional file output**, on top of Chuvadi.Pdf authoring. Write once, reused by all Lipi/SIGMA reports. May embed images (ties to #2).
- **#4 — Autohinter as Y-fitting fallback for unhinted fonts (Direction C).** The real home for C1/C2: detect stems/zones, apply Y-axis grid-fitting to fonts with no bytecode. Multi-component.
- **#5 — Spec simplifications (Direction B).** Distance-type compensation (currently zero), single-width cut-in, MPS-as-ppem. Interpreter hardening; A26 flagged these.

## Related repos (context for export questions)
- **`arunshivab/Chuvadi.Sheets`** — pure-BCL **xlsx** read/write library (NOT a PDF converter; sibling to Chuvadi.Pdf). v1.0, step 8 (encryption). MIT.
- **Export reality:** neither repo does xlsx->PDF or docx->PDF (that needs a layout engine between them). **For Lipi/SIGMA, generate reports *directly* as PDF** via Chuvadi.Pdf authoring (= backlog #3), rather than converting Office files. Image->PDF is the one genuinely easy conversion (pure Chuvadi.Pdf).

## Working rules (critical — from Project Instructions)
- **Never guess APIs** — read source / confirm via PowerShell, quote line numbers. Past guesses cost build cycles.
- **File delivery != deployment** — always give explicit "Copy -> path (replace/new)". Claude's sandbox can't build .NET 10; **Arun runs everything; Arun's eyes are ground truth on visual defects.**
- **Edits:** PowerShell `.Replace()` with unique anchors, UTF-8 no-BOM + LF via `[System.IO.File]::WriteAllText(...New UTF8Encoding($false))`. Commit messages via temp file + `git commit -F` (no PS heredocs with Unicode). Browser-duplicate trap: `Sort-Object LastWriteTime | Select-Object -Last 1`.
- **Before commit:** clear bin/obj; `dotnet build Chuvadi.slnx -c Release`; full `dotnet test`; `dotnet format Chuvadi.slnx --verify-no-changes`; `python tools\gen_api_docs.py` (internal types -> no doc diff).
- **Style that breaks the build:** no `var` in src; XML docs; `ThrowIfNull`; braces (IDE0011); no unused usings (IDE0005); CA1861 hoist arrays; one property per line in initializers. **Tests:** no `.ConfigureAwait(false)` in `[Fact]` bodies (xUnit1030); `Assert.Contains(item, coll)` not `.Should` on `.Contains`; CA2201 (no bare `System.Exception` — use `InvalidOperationException`); FluentAssertions `.Should()` style.
- **CS8602 nullable-flow:** capture fields into locals after opaque method calls.
- **PRs:** squash-merge, then `git branch -D` (squash makes branches look unmerged — verify with `git log main..branch` first). Branch protection: 4 CI checks (style, docs-up-to-date, build matrix ubuntu/windows/macos).
- **Path traps:** `src\Chuvadi.Pdf.Rendering.DisplayList\` (newer, SvgRenderer) vs `src\Chuvadi.Pdf.Rendering\DisplayList\` (older, do not touch). Hinting interpreter: `src\Chuvadi.Pdf.Fonts.Rendering\Hinting\HintingInterpreter.cs`. Loader: `TrueTypeLoader.cs` (composite hinting region near the `// composite glyph` banner).

## Useful sandbox technique (reusable)
For font/glyph validation: extract embedded fonts from a PDF (zlib-decompress streams, check sfnt tag), inspect with **fontTools**, measure hinted vs unhinted with **freetype-py** (set `interpreter-version` 35 vs 40 via `FT_Property_Set`). Used to validate composite dot levels. A `composite-test.pdf` generator (Type0/Identity-H, draws specific GIDs) is in the prior transcript if needed again.
