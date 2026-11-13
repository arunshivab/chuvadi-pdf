# CLAUDE.md — Chuvadi Project Working Agreement

> Read this at the start of every session.
> For current build state: docs/SESSION-STATE.md
> For decision history: docs/CHANGE-LOG.md
> For architectural invariants: docs/BASELINE.md

---

## 1. Project Identity

**Chuvadi** (சுவடி) — Tamil for palm-leaf manuscript.
A complete, high-performance PDF library for .NET 10+.
Pure managed code. Zero production dependencies. Apache 2.0.

```
PROJECT:   Chuvadi
STACK:     C# (.NET 10, LangVersion latest)
TYPE:      Multi-project class library (9 src + 10 test + 1 CLI)
DEPS:      Zero in src/. xUnit + FluentAssertions + FsCheck in tests/.
TEST:      xUnit

REPO ROOT: C:\Users\aruns\Documents\Chuvadi\chuvadi-scaffold\chuvadi\
DEPLOY:    %USERPROFILE%\Downloads\chuvadi\
SCRIPT:    .\deploy.ps1 (CRLF, ASCII-safe, supports As field)
BUILD:     dotnet build
TEST CMD:  dotnet test
CHECKER:   python3 tools/check_style.py <files>
```

---

## 2. Workflow — Code Generation

### Rule: Complete files only
Claude generates **complete files only** — never snippets, never diffs,
never "insert this here" instructions. If a file needs modifying, the
entire file is regenerated and delivered.

### Rule: Pre-code checklist (mandatory before every batch)
Before generating any file, Claude states:
```
WHAT:    [what is being built]
SPEC:    [PDF spec section, or "project discipline" if not spec-governed]
DESIGN:  [CHANGE-LOG entries or BASELINE rules that apply]
DEPLOY:  [files being created or modified, and their destinations]
```
Arun confirms or adjusts scope. Then Claude generates.

### Rule: API verification before writing
Before writing any method call or property access on a Chuvadi type,
Claude greps the source file for that type to confirm the exact name.
No assumptions about property names, constructor signatures, or method
existence. This prevents "member not found" CS1061 errors.

### Rule: Verify every using directive
Before adding `using X.Y.Z;` to a file, Claude confirms that at least
one type from that namespace is directly referenced in the file.
Speculative usings cause IDE0005 errors.

### Rule: Every fix must be packaged before confirming
A fix is not done until the corrected file is in a zip in outputs.
Never respond "Fixed!" without having packaged and delivered.

### Rule: File generation method
All C# files are generated using bash `cat > file << 'HEREDOC'`.
Never use Python string replacement to generate C# code that contains
backslashes, escape sequences, or character literals.
Python `\n` → actual newline in the file. Python `\'` → bare quote.
Both destroy C# character literals. Heredocs are completely literal.

---

## 3. Workflow — Deploy

### Deploy script rules
- CRLF line endings (Windows PowerShell 5.1)
- ASCII characters only
- Flat structure with `As` field for rename-on-copy
- Source: `%USERPROFILE%\Downloads\chuvadi\`
- Every new file registered in the same batch as the file

### Placeholder test files
8 empty test projects each have a `PlaceholderTests.cs` that carries
a unique namespace. They are delivered with unique source names
(e.g. `PlaceholderTests_Objects.cs`) and deployed with `As = "PlaceholderTests.cs"`.

---

## 4. Workflow — Build Gate

Build must be green before proceeding to the next module.
`TreatWarningsAsErrors` is on — a warning-only build is not clean.
Test failures are a stop condition.

---

## 5. C# Code Rules (all enforced as build errors)

### Style rules
| Rule | Requirement |
|---|---|
| IDE0008 | No `var` anywhere in `src/` — explicit types always |
| IDE0008 | `using (MemoryStream ms = new MemoryStream())` not `using var` |
| IDE0011 | Braces on ALL control flow — if, for, foreach, while, else |
| IDE0021 | Block body constructors — no `Ctor() => field = value;` |
| IDE0005 | No unused using directives — only add when a type from that ns is used |
| IDE0051/52 | No unused private members — including speculative helper methods |
| CA1062 | Validate all public method parameters before use |
| CA1032 | Every custom exception needs 3 standard constructors |
| CS0419 | Qualify ambiguous `<see cref="Method"/>` with parameter types |
| IDE0270 | Null check can be simplified — use `x ?? throw new Exception()` not `if (x is null) { throw... }` |
| IDE0060 | Remove unused parameter — never declare a parameter that the method body does not read. Use `_` discard for intentionally ignored parameters in overrides/interfaces. |
| IDE0060 | Remove unused parameters — any parameter declared but never read in the method body must be removed or replaced with `_` discard |
| CS0234 | Missing ProjectReference in csproj — before using a Chuvadi.Pdf.* namespace, verify its csproj is listed in the consuming project's `<ProjectReference>` entries. The checker now validates this automatically. |

### Known API facts (confirm by grepping source before using)
| Type | Correct API | Wrong assumption |
|---|---|---|
| PdfBoolean | `.Value` (bool) | `.IsTrue` |
| PdfToken | `.ByteOffset` | `.Offset` |
| PdfString | `.Bytes` | `.RawBytes` |
| PdfName | `PdfName.Intern("x")` | `new PdfName("x")` |
| PdfStream | `PdfStream` is a `PdfPrimitive`, not a `PdfDictionary` | |
| PdfTokenType | `DictionaryStart`, `ArrayStart` | `DictionaryBegin`, `ArrayBegin` |
| PdfTokenType | `True`, `False` (separate) | `Boolean` |
| PdfTokenType | `Reference` (the R token) | |
| PdfTokenType | `ObjectStart`/`ObjectEnd` | `Obj`/`EndObj` |
| PdfDictionary | Implements IEnumerable — foreach works | |
| Child namespace | `Chuvadi.Pdf.Objects.Tests` sees Objects types without using | |

### Known logic pitfalls
| Area | Pitfall | Fix |
|---|---|---|
| LZW decode | Width grows when `nextCode + EarlyChange >= 1 << bits` (decoder) and `>` in the encoder (3.34.0; the A11-era check grew one code too early and failed real streams) | Fixture vectors in Filters.Tests/Fixtures/Lzw |
| LZW EarlyChange | Decoder is 1 entry behind encoder in table building | `>=` vs `>` compensates |
| ASCII85 | Valid range is 33-117. `@`=64 IS valid. `v`=118 is NOT | |
| %%EOF | It is a comment at lexical level — reader finds it by backward scan | |
| Empty input | Decode of empty stream = empty output, not exception | |
| PdfName | No public constructor — always `PdfName.Intern("name")` | |

---

## 6. Pre-delivery Checklist (run before every zip)

```
[ ] python3 tools/check_style.py <all src/ files in batch>
[ ] python3 tools/check_style.py <all test files in batch>
[ ] Every using directive has a type from that namespace referenced in the file
[ ] Every member access confirmed against the source type's actual API
[ ] No Python-generated C# with backslashes (use heredoc)
[ ] For every Chuvadi.Pdf.* namespace used: csproj has matching ProjectReference
[ ] If csproj was modified: csproj is included in the zip AND registered in deploy.ps1

NEW PROJECT SCAFFOLD CLEANUP (whenever dotnet new was used):
[ ] Delete Class1.cs from any new src/ project
[ ] Delete UnitTest1.cs from any new tests/ project
[ ] Confirm no scaffolded placeholder files remain before deploying

DEPLOY.PS1 VERIFICATION (mandatory — run before every zip):
[ ] Every .cs file in the batch is listed in deploy.ps1 with correct Dest and As
[ ] Every .csproj modified in the batch is listed in deploy.ps1
[ ] Every .md doc file updated is listed in deploy.ps1
[ ] Total entry count matches expectation (grep "File = " deploy.ps1 | wc -l)
[ ] Fix is packaged in zip — not just fixed locally
```

---

## 7. Project Structure

```
Chuvadi.slnx                    Solution file
CLAUDE.md                       This file (working agreement)
deploy.ps1                      Deploy script

src/
  Chuvadi.Pdf.Primitives/       COMPLETE — tokens, primitive types, tokenizer
  Chuvadi.Pdf.Filters/          COMPLETE — DEFLATE, ASCII85, AsciiHex, LZW, RunLength
  Chuvadi.Pdf.Objects/          COMPLETE — xref, object store, indirect objects
  Chuvadi.Pdf.IO/               IN PROGRESS — reader, writer, parser
  Chuvadi.Pdf.Documents/        Scaffolded
  Chuvadi.Pdf.Fonts/            Scaffolded
  Chuvadi.Pdf.Content/          Scaffolded
  Chuvadi.Pdf.Text/             Scaffolded
  Chuvadi.Pdf.Operations/       Scaffolded

tests/
  Chuvadi.Pdf.*.Tests/          One test project per src project
  Chuvadi.Pdf.Integration.Tests/ Cross-project end-to-end tests

tools/
  Chuvadi.Pdf.Cli/              Reference CLI
  check_style.py                Pre-delivery style checker

docs/
  BASELINE.md                   Architectural invariants (never change)
  CHANGE-LOG.md                 Decision history (append-only)
  SESSION-STATE.md              Current build state (updated each session)

assets/
  fonts/lipi-sans/              LiPi Sans v1.0 woff2 files + CSS (web use)
  fonts/lipi-sans/lipi-sans.css  Drop-in web stylesheet
  fonts/lipi-sans/fonts/         11 script woff2 files
```

---

## 8. Default Font: LiPi Sans

LiPi Sans v1.0 is the default font for all Chuvadi and LiPi ecosystem projects.
Stored in `assets/fonts/lipi-sans/`. License: SIL OFL 1.1 (Inter + Noto Sans base).

| Context | Format needed | Status |
|---|---|---|
| Web/HTML/CLI output | woff2 | Ready in assets/ |
| PDF font embedding (Phase 1 Fonts) | TTF/OTF | Obtain from Inter + Noto Sans repos |
| PDF rendering fallback (Phase 2) | TTF/OTF | Deferred |

Scripts: Latin, Devanagari, Bengali, Tamil, Telugu, Malayalam, Kannada,
Gujarati, Gurmukhi, Odia. Variable font, weights 100-900 in one file.

See CHANGE-LOG A12 for full decision record.

---

## 9. Phase Boundaries

**Phase 1 (current):** Primitives, Filters, Objects, IO, Documents, Fonts,
Content, Text, Operations. PDF operations: merge, split, delete, rotate.
Born-digital text extraction. Annotation and form reading.

**Phase 2:** Images, rendering, watermarking, redaction, form filling.

**Phase 3:** Encryption, digital signatures, PDF/A.

---

## 10. Communication Style

Claude:
- States pre-code checklist before every batch; waits for confirmation
- Greps source files before assuming any API
- Surfaces issues before generating — never improvises past a spec ambiguity
- Never confirms a fix without packaging it first
- No filler ("Great question!", "Certainly!")
- No marketing voice ("powerful", "robust")

Arun:
- Confirms pre-code checklist before generation begins
- Pastes full build/test output when errors occur
- Uploads files when Claude needs current content before modifying
- Says "next" or "ready" to proceed after a green build

---

## 11. Resuming Work in a New Session

1. Read `docs/SESSION-STATE.md` — current build state and next step
2. Read `docs/CHANGE-LOG.md` — decisions since last session
3. Read `docs/BASELINE.md` if anything architectural is unclear
4. Ask Arun to confirm context before generating anything

---

*End of CLAUDE.md*
*Chuvadi — சுவடி — Free PDF library for .NET*
