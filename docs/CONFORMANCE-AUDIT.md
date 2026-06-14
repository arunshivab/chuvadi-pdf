# Conformance Audit — Phase A (code survey)

**Date:** 2026-06-15 · **Against:** ISO 32000-1:2008 · **Method:** source survey
with file:line evidence — no runtime corpus yet (that is Phase B).

This catalogs what the renderer does with each spec feature: **implemented**,
**partial/approximated**, or **missing (renders blank/wrong)**. Verdicts cite the
code that proves them. Roadmap items derived from this live in `BACKLOG.md`.

Scope note: two render sinks share one operator walker
(`Rendering.DisplayList/Walking/ContentStreamWalker.cs`) — the **raster** sink
(`Rendering.DisplayList/Raster/DisplayListBuilder.cs`) and the **SVG** sink
(`Rendering.DisplayList/DisplayListBuilder.cs`). Where they differ, both are noted.

---

## 1. Filters / codecs

**Implemented (decode):** FlateDecode, ASCIIHexDecode, ASCII85Decode,
RunLengthDecode, LZWDecode — registered in `Filters/FilterRegistry.cs:20-27`.
CCITTFaxDecode decode is implemented (`Filters/CcittFaxFilter.cs:43`) but
**encode throws** (`CcittFaxFilter.cs:68`). DCTDecode (JPEG) is handled at the
image layer via `Images/JpegDecoder.cs`, not as a stream filter; PNG via
`Images/PngDecoder.cs`.

**Missing — JBIG2Decode.** No decoder exists anywhere in `src/`. Scanned bilevel
images using JBIG2 render blank. *Large: arithmetic coding, symbol dictionaries,
generic/halftone regions.*

**Missing — JPXDecode (JPEG 2000).** Recognized but not decoded: the raster sink
detects it (`Raster/DisplayListBuilder.cs:1609,1621`) and reads the raw bytes,
but only JPEG (FF D8) and PNG magic bytes are dispatched to a decoder
(`Raster/DisplayListBuilder.cs:1643-1652`); JPX bytes fall through to
`FrameFromRawSamples`, which misinterprets them → garbage or blank. The SVG sink
routes non-DCT streams through the filter pipeline, which has no JPX filter, so
the decode throws and the image is dropped (`DisplayListBuilder.cs:1100-1103`).

---

## 2. Color spaces

### Image sample path
`Raster/DisplayListBuilder.cs:1772 ResolveComponentCount` maps:
DeviceGray/CalGray → 1, DeviceRGB/CalRGB → 3, ICCBased → its stream `/N`
**but only N∈{1,3}** (`DisplayListBuilder.cs:1789-1795`). Everything else returns
0 = unsupported (image not rendered):

- **DeviceCMYK raw-sample images → 0 components** (absent from the switch at
  `:1783-1787`). CMYK *JPEGs* still decode via `JpegDecoder`; raw CMYK samples do not.
- **ICCBased N=4 (CMYK) → 0** (`:1793`).
- **Indexed, Separation, DeviceN, Lab images → 0** (not handled).

### Fill/stroke operator path (`cs`/`CS`/`sc`/`scn`/`SC`/`SCN`)
**Raster** sink: `SetColorSpace` (`Raster/DisplayListBuilder.cs:294`) marks only
device spaces valid; **any non-device space (ICCBased, Indexed, Separation,
DeviceN, Lab, Pattern) marks the colour invalid**. `SetColorN` (`:314`)
interprets operands purely by **count** — 1→gray, 3→rgb, 4→cmyk (`:326-348`):

- ICCBased fills work in practice (numeric `scn` re-validates by component count
  — a reasonable approximation).
- **Separation / DeviceN → wrong colour:** the tint-transform function is ignored;
  raw tint values are used as device components.
- **Indexed → wrong colour:** the index is used as a colour value.
- **Pattern (`scn /P`) → suppressed:** a name operand sets the colour invalid
  (`:318-321`), so pattern-filled regions are not painted.

**SVG** sink: **no `cs`/`CS`/`sc`/`scn`/`SCN` handling at all** (none found in
`DisplayListBuilder.cs`) — non-RGB/gray fills are mishandled. (Tracked as #11.)

---

## 3. Shadings & patterns

**Missing — shadings (`sh`).** The `sh` operator is in the *recognised-no-ops*
block shared by both sinks (`Walking/ContentStreamWalker.cs:403`, alongside `i`,
`ri`, `gs`, `BMC/BDC/EMC`). No ShadingType 1–7 (function-based, axial, radial,
free/lattice-form Gourand, Coons, tensor) implementation exists. Gradients
painted via `sh` do not render. *This is why a gradient logo/background comes out
blank.*

**Missing — tiling patterns (PatternType 1) and shading patterns
(PatternType 2).** Reached via `scn` with a pattern name, which the raster sink
suppresses (§2 above); no pattern cell replay exists.

---

## 4. Transparency / graphics state

**Missing — `gs` (ExtGState) is a no-op** (`Walking/ContentStreamWalker.cs:402`).
Consequently constant alpha (`ca`/`CA`), blend modes (`BM`), soft masks
(`SMask` in an ExtGState), and transparency groups are all ignored — content
renders fully opaque with Normal blending.

**Implemented (distinct feature) — image XObject `/SMask`** (soft-mask alpha) in
the SVG sink, added v3.6.0. This is per-image alpha, separate from `gs`-level
transparency above.

---

## 5. Fonts

**Implemented outline programs:** TrueType/OpenType (`Fonts.Rendering/FontRenderer.cs`
+ `TrueTypeLoader`), CFF/Type2 charstrings (`Fonts.Rendering/CffLoader.cs`,
`Type2Interpreter.cs`), Type1 (`Fonts.Rendering/Type1FontRenderer.cs`). Type0/CID
is detected (`Fonts/PdfFont.cs:96`) and composite routing exists.

**Missing — Type3 fonts.** No `CharProcs`/`d0`/`d1` handling anywhere in `src/`.
Text set in a Type3 font (glyphs defined as content streams) does not render.

**Missing — complex-script (Indic/Arabic) shaping.** No GSUB/GPOS application or
Indic reordering in the text path. Embedded fonts carry the tables; the shaping
engine that uses them does not exist. (Tracked as #2.)

---

## 6. Annotations

**Missing — appearance-stream rendering.** The render pipeline does not read page
`/Annots` or draw their `/AP /N` appearance streams (no `Annots`/`/AP` handling in
`Rendering.DisplayList/*`). Annotations are readable and creatable
(`Chuvadi.Pdf.Annotations`), but at render time form fields, widgets, stamps,
ink, and other appearance-stream annotations do not appear on the page.

---

## 7. XFA forms

**Missing — XFA rendering.** `PdfDocument.IsXfa` (v3.6.0) detects XFA but the
content lives in `/AcroForm /XFA`, outside page content, so such documents render
essentially blank. Full XFA needs its own template/layout/scripting engine.

---

## 8. Known SVG-sink gaps vs raster (tracked as #11)

`cs`/`scn` colour operators (§2), form-XObject recursion (non-image XObjects
return early), and the raster quote operators (`'`/`"`) bypassing composite-font
routing.

---

## 9. Technical debt

Two parallel `DisplayListBuilder` namespaces remain
(`Rendering.DisplayList/` vs `Rendering/DisplayList/`); consolidation is a
separate cleanup, not a conformance item.

---

## Phase B (next)

Run a real corpus — government XFA forms, JBIG2 scans, gradient-heavy designs,
Separation/DeviceN print PDFs, Type3 documents, annotation-rich forms — and
record each renders-blank/wrong case the way `merged3.pdf` exposed the xref bug.
Phase A is the map; Phase B tests it against ground truth.
