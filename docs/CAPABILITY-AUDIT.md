# Chuvadi capability audit

Current for **3.34.0**. Kept current with every release (D-339): each
release plan lists the viewer gaps from this file that Chuvadi still has.
The older `docs/CONFORMANCE-AUDIT.md` and
`docs/chuvadi-35-item-roadmap-handoff.md` are superseded by it.

**Method.**
- Every Chuvadi verdict comes from the source at the release, with probes
  where noted, and from the Phase B runs below.
- Peer columns come from each product's public documentation and long-standing
  behaviour as of mid-2026. **They are not re-tested each release.** A `?`
  marks a peer verdict that is not certain.
- **Windows** means the Microsoft stack: `Windows.Data.Pdf` (render-only: it
  opens a page, with a password if needed, and renders it to an image; no
  text, form, annotation or editing API) plus WPF/XPS printing and
  "Microsoft Print to PDF". Its codec coverage is not documented in detail,
  so those cells carry `?`.
- **Adobe** means Acrobat and the Adobe PDF Library.

**Legend.** ✓ full · ◐ partial · ✗ none · n/a not applicable. **Size** of
Chuvadi's gap: S under 1 week, M 1–2 weeks, L 2–4 weeks, XL over a month.
`#N` is a ticket in `docs/BACKLOG.md`.

**Peers.** Viewing: PDFium (Chrome), MuPDF, pdf.js (Firefox), Poppler, Adobe,
Windows. Editing and creating: iText 8, PDFBox 3, PDFsharp 6, Adobe, Windows.

---

## Part 2. Viewing

| Area | Chuvadi 3.34.0 | Gap, ticket, size | PDFium | MuPDF | pdf.js | Poppler | Adobe | Windows |
|---|---|---|---|---|---|---|---|---|
| Stream filters (Flate, LZW, ASCIIHex, ASCII85, RunLength, predictors, Crypt) | ✓ | – | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| DCTDecode (JPEG) | ◐: baseline and progressive, CMYK/YCCK | 12-bit, arithmetic and lossless JPEG · no ticket · S–M | ✓ | ✓ | ✓ | ✓ | ✓ | ✓? |
| **JPXDecode (JPEG 2000)** | **✗** | **#1 · XL · 3.36 (D-350)** | ✓ | ✓ | ✓ | ✓ | ✓ | ✓? |
| JBIG2Decode | ◐: arithmetic generic regions, symbol dictionaries, text regions | MMR, Huffman, refinement, transposed text, **halftone and pattern regions (skipped silently)** · no decode ticket · M–L | ✓ | ✓ | ✓ | ✓ | ✓ | ✓? |
| CCITTFaxDecode | ✓ decode (encode throws) | encode (#10/#12 family) · M | ✓ | ✓ | ✓ | ✓ | ✓ | ✓? |
| Colour spaces (Device, Cal, Lab, Indexed, Separation, DeviceN, Pattern) | ✓ | – | ✓ | ✓ | ✓ | ✓ | ✓ | ✓? |
| ICC colour management | ◐: profiles parsed, but rendering uses the alternate space; CMYK→RGB is a formula; rendering intents ignored | no ticket · L | ✓ | ✓ | ◐ | ✓ | ✓ | ◐? |
| Overprint and overprint simulation | ✗ | no ticket · M | ◐ | ✓ | ✗ | ✓ | ✓ | ✗? |
| CMYK raster output (not just RGB) | ✓ (CMYK pixel pipeline and CMYK TIFF) | – | ✗ | ✓ | ✗ | ✓ | ✓ | ✗ |
| Transparency: constant alpha, soft masks, isolated groups | ✓ (soft mask at a form's `Do`, 3.34.0) | – | ✓ | ✓ | ✓ | ✓ | ✓ | ✓? |
| Blend modes | ◐: 12 separable modes | Hue, Saturation, Color, Luminosity · no ticket · S | ✓ | ✓ | ✓ | ✓ | ✓ | ✓? |
| Knockout groups | ✗: `/K` read, not honoured | no ticket · M | ◐ | ✓ | ✗ | ◐ | ✓ | ? |
| Shadings | ◐: axial and radial | function-based (1) and mesh (4–7) skipped with a warning; noted only in #17 · M | ✓ | ✓ | ✓ | ✓ | ✓ | ✓? |
| Patterns (tiling, shading) | ✓ for the supported shading types (SVG shading in a group, 3.34.0) | – | ✓ | ✓ | ✓ | ✓ | ✓ | ✓? |
| Image placement (rotated, skewed or flipped images) | ✓ (raster, 3.34.0); SVG and WPF follow the full matrix | – | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Stencil masks (`/ImageMask`) | ✓ raster and SVG | – | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Stroking | ✓: round joins and an elliptical pen under a non-uniform CTM (3.34.0) | bevel joins and `/ML` (#54) · S | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Fonts: Type 1, CFF, CID-keyed CFF, TrueType (hinted), OpenType, Type 3 | ✓ | hinting provenance: clean-room interpreter (#33), autohinter rewrite or drop (#34), setup-program state (#35) · M each | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Non-embedded fonts | ✓: standard-14 outlines plus Liberation; glyphs chosen from the encoding (3.34.0) | – | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| **Predefined CJK CMaps** | **✗** | no ticket · M | ✓ | ✓ | ✓ | ✓ | ✓ (font pack) | ✓? |
| **Vertical writing** (WMode 1) | **✗** | no ticket · M (shaper side in #25) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓? |
| Complex-script shaping (authoring, appearances) | ◐: Indic and default shapers | Arabic, Hebrew, Thai/Lao, Hangul, Khmer, Myanmar, USE, variation sequences, `kern` table, variable instances (#25); Devanagari 21% faster in 3.34.0 · M–L | n/a | n/a | n/a | n/a | ✓ | n/a |
| Annotation rendering (`/AP`) | ✓ (annotation `/OC` and print state, 3.34.0) | – | ✓ | ✓ | ✓ | ✓ | ✓ | ◐? |
| Annotation appearance generation | ◐: 15 subtypes plus widgets and signatures | Redact, Sound, Movie, Screen, 3D, RichMedia · S per type | ✓ | ✓ | ◐ | ◐ | ✓ | ✗ |
| Optional content | ✓: honoured in render, text and flattening; print state (`ForPrinting`, 3.34.0) | – | ✓ | ✓ | ✓ | ✓ | ✓ | ◐? |
| Page boxes and rotation | ◐: CropBox by default; `/Rotate` opt-in; shown-page geometry for callers (`GetDisplayBox`, `GetDisplayTransform`, `GetOutputSize`, 3.34.0) | `/Rotate` default (#3, major version); `/UserUnit` (#9) · S | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Rendering outputs | raster, PNG, SVG, WPF (WPF clips, 3.34.0) | – | raster | raster, SVG | canvas, SVG | raster, Cairo | all | bitmap |
| Text extraction | ✓: layout, columns, tagged order, ToUnicode, ActualText, Indic | CJK via predefined CMaps; RTL/bidi not verified; letters from the embedded font's own cmap when ToUnicode is wrong (#30) · S–M | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ |
| SVG text when ToUnicode contradicts the encoding | ✓ (3.34.0) | – | n/a | ✓ | ✓ | ✓ | n/a | n/a |
| Search | ✓: case, whole word, NFKD, rectangles | no regex · S | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ |
| XFA | ✓: render, FormCalc and JavaScript | saving XFA data back not verified · M? | ◐ | ✗ | ◐ | ✗ | ✓ (Acrobat) / ◐ (PDF Library) | ✗ |
| AcroForm JavaScript | ✗ **by design** | keep · L if ever opt-in | ✓ | ✓ | ✓ | ◐ | ✓ (Acrobat) | ✗ |
| Repair and damaged content | ◐: repair is opt-in (`RepairIfDamaged`); content syntax damage draws what parsed before it; crypt filters, missing MediaBox, looping page trees and damaged object syntax recovered with a warning (3.34.0) | repair by default (#52e, 4.0) | ✓ | ✓ | ✓ | ✓ | ✓ | ◐? |
| Linearization | ✓ read and write | – | read | read and write | read | read | ✓ | read? |
| Streaming page enumeration, large files | ✓ (`EnumerateStreaming`) | – | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Safety for untrusted input | ✓: limits, cancellation, no disk/net/JS, sanitizer; write paths too; JBIG2 and CCITT decodes interruptible (3.34.0) | – | ◐ | ◐ | ◐ | ◐ | ◐ | ◐ |
| 3D, multimedia, RichMedia | ✗ (removed by the sanitizer) | not planned · XL | ◐ | ✗ | ✗ | ◐ | ◐ (3D off by default) | ✗ |
| Portfolios (`/Collection`) | ✗ | no ticket · M | ◐ | ◐ | ◐ | ◐ | ✓ | ✗ |
| Printing | in Chuvadi.Print (monorepo), not chuvadi-pdf | – | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ (XPS) |

## Part 3. Editing, creating and security

| Area | Chuvadi 3.34.0 | Gap, ticket, size | iText 8 | PDFBox 3 | PDFsharp 6 | Adobe | Windows |
|---|---|---|---|---|---|---|---|
| Creating documents (text, images, paths, tables, reports, links, image→PDF) | ✓, with subsetting and Indic shaping | gradients and patterns in the builder · S | ✓ | ◐ | ✓ | ✓ | ◐ (print-to-PDF only) |
| Standard-14 embedding, PDF/A substitution | ✓ | – | ✓ | ◐ | ◐ | ✓ | ✗ |
| **Creating form fields** | **✗** | no ticket · M | ✓ | ✓ | ◐ | ✓ | ✗ |
| Filling forms | ✓: text, choice, check box, radio; widgets, options, flags; UTF-16 values; keeps `/Info` and `/ID`; optional encryption (3.34.0) | – | ✓ | ✓ | ◐ | ✓ | ✗ |
| Flattening and appearance generation | ✓ | – | ✓ | ◐ | ◐ | ✓ | ✗ |
| Creating annotations | ◐: 11 types | Caret, FileAttachment, Redact, Sound · S each | ✓ | ✓ | ◐ | ✓ | ✗ |
| Page operations | ✓: merge keeps forms (rename or share clashing names) and can encrypt (3.34.0) | – | ✓ | ✓ | ✓ | ✓ | ✗ |
| Stamps, watermarks, header/footer, imposition | ✓: placement on the shown page (3.34.0) | – | ✓ | ◐ | ◐ | ✓ | ✗ |
| Redaction (text, images, vectors, annotations, fields, verifier; pattern-based; parallel) | ✓ | per-page report; bookmark titles and XMP scrubbed, `/Info` left out (3.34.0); curved fills removed whole (#44) · S | ✓ (paid add-on) | ✗ | ✗ | ✓ (Acrobat Pro) | ✗ |
| Redaction-grade crop | ✓ ClipOnly / Scrub | Scrub residuals (#17) · M | ✗ | ✗ | ✗ | ◐ | ✗ |
| Bookmarks | ✓ | – | ✓ | ✓ | ✓ | ✓ | ✗ |
| Attachments (adding) | ✗ (read and remove only) | no ticket · S | ✓ | ✓ | ◐ | ✓ | ✗ |
| Creating layers | ✗ | no ticket · S–M | ✓ | ✓ | ✗ | ✓ | ✗ |
| Compression and optimisation | ✓: levels, recompression, downsampling, smart codec choice, dedupe, object streams, minify, target size | MRC (#12) · L; SSIM target (#13) · M | ✓ | ◐ | ◐ | ✓ | ✗ |
| Image encoders (JPEG, PNG, TIFF, BMP, CMYK TIFF) and perspective deskew | ✓ | JBIG2 encode (#10) · L; JPEG 2000 encode (#11) · L | ◐ | ◐ | ◐ | ✓ | n/a |
| Encryption: Standard RC4/AES-128/AES-256, read and write | ✓ | – | ✓ | ✓ | ✓ | ✓ | read only |
| **Public-key encryption** | **✗** | no ticket · M | ✓ | ✓ | ✗ | ✓ | ✗ |
| Signing (CMS via `ISigner`, HSM-ready), countersign, timestamp, LTV, verification with OCSP/CRL/path | ✓ | – | ✓ | ◐ | ◐ | ✓ | ✗ |
| PAdES baseline signing | ◐: signs `adbe.pkcs7.detached` only | ETSI.CAdES.detached plus signing-certificate-v2 · S–M | ✓ | ◐ | ✗ | ✓ | ✗ |
| **Certification signatures** (DocMDP) | **✗** | no ticket · M | ✓ | ◐ | ✗ | ✓ | ✗ |
| **General incremental save** | ◐: signing and LTV only | no ticket · M | ✓ | ✓ | ✗ | ✓ | ✗ |
| PDF/A conversion | ◐: 1b and 2b | 2u, 3b, "a" levels, 4; rest of #49 · L | ✓ | ◐ | ◐ | ✓ (Preflight) | ✗ |
| PDF/A validation | ✗ | no ticket · L | ◐ | ◐ | ✗ | ✓ | ✗ |
| **Tagged PDF and PDF/UA creation** | **✗** | #5 · L–XL | ✓ | ✗ | ✗ | ✓ | ✗ |
| Licence and platform | Apache 2.0; pure managed .NET; AOT, trimming, WASM | – | AGPL/commercial | Apache 2.0 (Java) | MIT | proprietary, paid (native) | Windows only |

---

## Part 4. Short lists

### What the others have that we don't
1. **JPEG 2000 decoding** (#1, 3.36). Every viewer and Adobe have it.
2. **Predefined CJK CMaps and vertical writing.** Every viewer and Adobe.
3. **Full JBIG2** (MMR, Huffman, refinement, halftone).
4. **Hue, Saturation, Color and Luminosity blend modes; knockout groups;
   function-based and mesh shadings.**
5. **ICC colour management and overprint.** MuPDF, Poppler and Adobe.
6. **Complex scripts beyond Indic**, for authoring (#25). Adobe has them.
7. **Creating form fields, layers and attachments.** iText, PDFBox and Adobe.
8. **Public-key encryption, certification signatures, true PAdES baseline
   signing, general incremental save.** iText and Adobe; PDFBox mostly.
9. **Tagged PDF and PDF/UA, PDF/A 2u/3/a/4, PDF/A validation.** iText and
   Adobe.
10. **Portfolios.** Adobe in full; others partly.
11. **AcroForm JavaScript.** All viewers and Acrobat run it. Left out on
    purpose.

### What we have that others don't (or only paid or native)
1. **Indic shaping that copies and searches correctly** in authoring, fields,
   comments and signatures. Among libraries only Adobe (paid) does this.
2. **Full redaction**: vector subtraction, pixel-level image blanking, form
   recursion, pattern search, parallel runs, a per-page report, a verifier.
   Otherwise only Adobe Acrobat Pro and iText's paid add-on.
3. **Redaction-grade crop** (ClipOnly / Scrub). No peer has an equivalent
   byte-scrubbing crop.
4. **XFA rendering with FormCalc and JavaScript** in a free library. Only
   Adobe has full XFA.
5. **Pure managed .NET**: AOT, trimming, WebAssembly, no native code.
6. **Embedding guarantees for untrusted input**, documented and tested (no
   JavaScript, actions, network or disk; limits; cancellation; one exception
   set, write paths included), plus a sanitizer.
7. **Viewing, editing, creating, signing (LTV), PDF/A, compression and
   redaction in one Apache-2.0 family.** Only Adobe covers as much.
8. **A CMYK raster output path**, which pdf.js, PDFium and Windows lack.

### Release plan (D-339, revised by D-350 and D-351)

| Release | Content |
|---|---|
| 3.34.0 (27 Nov 2026) | This release, plus: the Phase B test-bench items N1 to N14 (D-365, D-366: inline images, LZW, CMYK JPEG, hinting size, Type 1 routing, `/Mask`, skipped-image warnings, damaged-file cases); zone-less PDF/A dates reported (63C); rotated, skewed and flipped images (#50); Phase B fixes (#52, except repair-by-default); stamper/composer references (#53); WPF clips (#40); SVG shading in groups (#41); soft mask at a form's `Do` (#42); annotation layers (#43); round joins (#22); elliptical pen (#23); ToUnicode vs encoding in SVG (#36) and glyph choice in substitute fonts (#37), measured first; interruptible JBIG2/CCITT (#20); redaction of bookmarks, `/Info` and XMP (#18); three PDF/A fixes (#49c, #49d, #49g); API docs for tuple members (#51); Devanagari shaping profile (#27) |
| 3.35 (about mid-January 2027) | The small and medium items of the backlog (curved fills in redaction #44, Scrub residuals #17, PDF/A conversion items #49, #30, #13 and others), plus Hue/Saturation/Color/Luminosity blend modes, predefined CJK CMaps (D-352: our own tables generated at build time from one pinned Adobe release, with its licence notice), vertical writing, knockout groups together with off-screen transparency groups (backlog 64, D-370: they share the same render-then-blend machinery), hinting at the drawn size for all text after the autohinter orientation fix and the TrueType interpreter distortion at small sizes, with a dedicated hinting corpus run (backlog 65, D-377) |
| 3.36 | JPEG 2000 decoding (#1, moved from 3.35), ICC colour management, overprint, function-based and mesh shadings, the rest of JBIG2 |
| Later, one engine per release | Shaping (#25 and vertical shaping); hinting (#33, #34, #35, after the D-106 plan and Arun's #34 decision); tagged PDF (#5); compression (#10, #12, #13, then #11 after the JPEG 2000 decoder) |
| To be discussed | The editor-side and other gaps without tickets (Part 3, Part 4 items 6–10); the 4.0 items: `/Rotate` and `/UserUnit` by default (#3, #9), the shared value-types move (#6), repair by default |

---

## Part 5. Phase B (runtime audit)

Phase B runs the library over public test corpora and compares the
outcome with Poppler (`pdfinfo`, `pdftoppm`). Corpora are used for testing
only, never shipped. Arun's own scans and hospital forms are still to come.

**pdf.js test suite** (1,092 PDFs; a corpus of deliberately broken,
unusual and bug-report files). Default options, the first three pages
rendered at 72 dpi and extracted, 20 seconds per file. Run 1 was before the
3.34.0 Phase B fixes, run 2 after them.

| Measure | Run 1 | Run 2 (3.34.0) | Poppler |
|---|---|---|---|
| Opens | 1,036 (8 more need a password) | 1,038 (7 more need a password) | 1,070 |
| Opens with `RepairIfDamaged` | 1,063 | 1,077 (8 need a password) | – |
| Does not open, even with repair | 21 | 7 (Poppler opens 1 of them) | – |
| Opened, but a page fails | 27 files (23 that Poppler draws) | 5 files (2 that Poppler draws) | – |
| Process crash | 1 (fixed: a composite glyph that contains itself overflowed the stack) | 0 | 0 |
| Time per file | median 11 ms, 95th percentile 1.2 s | median 6 ms, 95th percentile 0.7 s | – |

**Run 2, files that still do not open with repair (7):** a Brotli-coded
object stream (an unregistered filter), zlib data with an unknown
compression method, three files whose cross-reference data is missing or
damaged beyond what repair rebuilds, a draft AES-256 handler (`/R 5`, which qpdf also refuses), and one
fuzzed file (`poppler-742-0-fuzzed.pdf`, the only one Poppler opens).

**Run 2, pages that still fail (5 files):** a page tree that refers back to
itself (`Pages-tree-refs.pdf`; it opens and draws with `RepairIfDamaged`,
which walks the tree, 52g); one stress file stopped by the 20-second limit
(`perf_structure_forms.pdf`); and three that Poppler does not draw either
(an image over the pixel limit, two tiling patterns with tiny steps, #56).
Run 2 also found a 3.34.0 regression, fixed before release: a square cap
after a round join threw (`perf_paths_curves.pdf`).

**Run 1 findings, all fixed in 3.34.0 (#52):** content syntax damage (an
unterminated string at the end of a content stream, an empty name, a
dictionary key that is not a name) failed the whole page; "LineTo requires
a current point" in one file; streams without `/Length` and damaged Flate in
page resources; crypt filters; a page-tree cycle and a missing or empty
MediaBox.

**Features in the corpus** (files): JBIG2 104, Type 3 fonts 34, DCT 73,
predefined CJK CMaps 19, JPEG 2000 15, mesh shadings 12, CCITT 11, XFA 8,
vertical writing 5, non-separable blend modes 3, function-based shadings 3,
rotated, skewed or flipped images 9. Both runs measured whether pages
draw, not how faithfully; a fidelity comparison for these feature files is
the next Phase B step.

Repair by default is #52e (4.0).

**Test bench (D-365, D-366), fixed in 3.34.0:** inline images were not
drawn, and their data could end the page (N4); LZW streams decoded wrongly
past 511 codes (N2); CMYK JPEGs were Adobe-inverted instead of read through
`/Decode` (N3); text set as `1 Tf` and scaled by `Tm` was hinted at one
pixel (N1); an embedded Type 1 font with a standard-14 name drew the
substitute (N5); an indirect `/DecodeParms` was ignored (N6); skipped
images were silent and `/Mask` was ignored (N7); `xref` after white space
(N8) or on one line (N11) needed repair; a page-tree kid that is not a
dictionary stopped later pages (N9); fonts were re-read in every form
(N10); short image data was skipped (N13). Each has a synthetic repro
compared with Poppler and a test.

**Run 3 (3.34.0 final, after the test-bench fixes):** the first two pages
of every pdf.js file rendered at 72 dpi by 3.33.0 and by 3.34.0, each
compared with Poppler: of 1,156 pages, 1,033 unchanged, 78 closer to
Poppler, 30 drawn that 3.33.0 did not draw, and 7 further, each checked by
eye (orientation, crop box or 72-dpi anti-aliasing only on four; a JPEG
2000 image now skipped instead of drawn as noise; a pattern-filled stencil
mask, a gap that 3.33.0 also had; one InDesign page with soft masks that is
wrong in both). Pages that fail: 30 files in 3.33.0, 9 in 3.34.0. The run
found seven more problems and a fuzz run of the inline-image path four;
all were fixed before release (Rule 5 items 85 and 86).

**Test bench real files (D-367):** 25 files (Govdocs1, SafeDocs,
Stressful, veraPDF, Poppler; local only). N1 to N5 fixed on every file;
the five N14 files now draw close to Poppler, two of them through new
fixes: fills close open sub-paths, and a transparency group keeps its
outer alpha (Rule 5 items 87 to 89). Found and left for later: no fi/fl
ligatures in the standard-14 outlines (backlog 63).

**Before/after, 3.33.0 against 3.34.0 (D-369):** the pdf.js suite (1,092
files), the test bench (25), a fresh Govdocs1 + SafeDocs sample with the
bench's seed 20261112 (700) and Arun's own files (20); text, raster, SVG
and the save, merge and flatten paths, each output checked with qpdf; and
veraPDF on PDF/A-1b and 2b in IST and UTC. Pages that fail: 53 to 10
(pdf.js), 19 to 0 (sample), 11 to 0 (bench). Writes that fail: 185 to 32,
69 to 4, 11 to 0, 3 to 0. Every page that moved further from Poppler was
inspected: newly drawn content, Poppler's `/Rotate`, or the hinting case
below. veraPDF passes: 8 to 140 (1b) and 222 to 246 (2b), none lost. The
run found merge and split dropping inherited `/Resources` and boxes (an
old bug, fixed: qpdf-flagged outputs 139 to 59 on pdf.js), Word's
near-even text matrices losing hinting (fixed), and two hinting faults at
small sizes once all text is hinted at its drawn size: so 3.34.0 hints at
the drawn size only for a scale above 2 or below 0.5 (D-377), and the rest
moves to 3.35 (backlog 65). One cosmetic writer gap went to the backlog
(66).

---

## Part 6. History

- **2026-11-11, audit v2 (3.33.0).** Peer columns for Adobe and Windows
  added; every older audit and plan reconciled. The JPEG 2000 gap had been
  on record since the first backlog (#1) but was never raised as a basic
  viewer gap: the conformance audit compared Chuvadi with the spec, not with
  peers, and its planned runtime audit (Phase B) was never run. The process
  fixes (D-339) are this file, the gap list in every release plan, Phase B
  with a wider corpus, and the roadmap above.
- **3.34.0.** Moved into the repo; forms, merge, watermark placement and
  write-path safety updated; the image-placement gap (#50) and the Phase B
  results added; the Group A items of D-351 marked shipped, and Phase B
  run 2 (after the fixes) added.
- **3.34.0, round 3.** The Phase B test-bench items (N1 to N13) and the
  PDF/A date warning (63C) added; release date moved to 27 Nov (D-366).
- **3.34.0, before/after check (D-369, D-377).** Results in Part 5;
  hinting at the drawn size for all text and the two hinting faults it
  exposed added to the 3.35 plan (backlog 65).
- **Older documents** (for traceability): the first backlog (2026-05-11),
  the v2.7.0 and v2.8.0 backlogs, the 2026-06 session handoff, the Phase A
  conformance audit (2026-06-15, v3.16.0 updates), the 35-item roadmap
  (2026-06-20) and `docs/archive/`. Their open items are in Parts 2 and 3
  or in `docs/BACKLOG.md`; most of the rest has shipped (annotations,
  encryption, signatures, linearization, optional content, CMYK output,
  TIFF, subsetting, streaming enumeration, pattern-based and non-text
  redaction, image to PDF, JBIG2 decode in part, shadings 2 and 3,
  transparency, Type 3 fonts, Indic shaping, XFA, PDF/A 1b and 2b).
