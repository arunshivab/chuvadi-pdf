# Changelog

All notable changes to Chuvadi will be documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This file records release-by-release notes. Architectural decisions and
rationale live in `docs/CHANGE-LOG.md` (an append-only decision log,
numbered A01..ANN).

---
## [3.34.0] - 2026-11-27

Shown-page geometry for callers (D-334), forms that read and fill fully
(fields, widgets, options, flags; buttons and radios fixed; UTF-16 names and
Tamil values; `/Info`, `/ID` and encryption kept), merge that keeps forms and
can encrypt, a redaction report, watermarks and headers placed on the page as
shown, every write path inside the cancellation and limits scope, release
guards, and a trimmed and ReadyToRun publish check (D-335, D-338, D-339,
D-342). Also (D-348, D-350, D-351, D-353): images drawn through their full
matrix, damaged input read past with a warning instead of failing (the pdf.js
test set), crypt filters, print-state layers, round joins and the elliptical
pen, soft masks at a form's `Do`, WPF clips, redaction of bookmarks and XMP,
three PDF/A-1b fixes, and faster Devanagari shaping. Forms (D-358, D-362):
several values for a multi-select list, refused values explained by
properties apps can localise, opt-in value checks, and unknown field names
ignored, reported or refused. Phase B test bench (D-365, D-366): inline
images drawn, LZW streams read past 511 codes, CMYK JPEGs read through
`/Decode`, text scaled by its matrices hinted at the size it is drawn,
embedded Type 1 fonts with a standard-14 name drawn from their own program, image masks applied, skipped
images reported, and several damaged-file cases read; checked on the test
bench's real files, where open paths now fill and transparency groups keep
their alpha (N14, D-367). PDF/A (D-364): a
zone-less date is reported, and can be written as UTC on request.

### Added
- **Shown-page geometry (D-334).**
  - `PdfPage.GetDisplayBox(PdfDisplayBox)`: the box a viewer shows, the
    CropBox intersected with the MediaBox (normalised; the MediaBox when they
    do not overlap). The rule is now shared by the raster, SVG and WPF
    renderers.
  - `PdfPage.GetDisplayTransform(PdfDisplayBox)`: a `Transform` from display
    space (the shown page, upright after `/Rotate`, origin at its lower left)
    to PDF user space. `Chuvadi.Pdf.Documents` now depends on
    `Chuvadi.Pdf.Graphics`.
  - `PageRasterizer.GetOutputSize(PdfPage)`: the size in pixels that
    `Rasterize` will return, without rendering.
- **Form reading (D-335).** `FormField.Widgets` (`FormWidget`: page index,
  rectangle, object id, on-state, export value), `FormField.Options`
  (`FormChoiceOption`: export value and display text), `FormField.Flags`
  (`FormFieldFlags`, the PDF bit values) and `FormField.MaxLength`.
  `/FT`, `/Ff` and `/MaxLen` are inherited from parent fields (item 27).
- **`FormFiller.Fill(..., FormFillOptions)`** with
  `FormFillOptions.Encryption` (D-342): the filled copy can be encrypted.
- **`MergeOptions.FormFields`** (`MergeFormFields`: `Drop`,
  `RenameOnConflict`, `ShareSameName`) and **`MergeOptions.Encryption`**.
- **`Redactor.ApplyWithReport`**, returning `RedactionReport` with one
  `PageRedactionReport` per rewritten page: glyphs removed, images blanked
  and removed, paths cut and removed, annotations removed, field values
  cleared, alternate texts removed. The output is the same as `Apply`.
- **`PlaceInDisplaySpace`** on `TextWatermarkOptions`,
  `ImageWatermarkOptions` and `HeaderFooterOptions` (default false): place
  the watermark, header or footer on the page as a viewer shows it (the
  shown box, upright after `/Rotate`).
- **`OptionalContentVisibility.ForPrinting()`** (#43): the layer visibility
  for printing, with each layer's `/Usage /Print /PrintState` applied.
- **`RedactionReport.DocumentMatchesRemoved`** (#18): matches removed from
  bookmark titles and the XMP metadata.
- **Form filling with several values and a result (D-358, D-362).**
  - `FormFiller.Fill(Stream, PdfDocument, IReadOnlyDictionary<string,
    IReadOnlyList<string>>, FormFillOptions)` returns a `FormFillResult`
    (`FilledFields`, `UnknownFields`). A multi-select list box takes any
    number of export values: `/V` is one string for one value and an array
    for more, and `/I` lists the chosen options' indices. Any other field
    given more than one value throws (`TooManyValues`).
  - `FormException.Reason` (`FormErrorReason`: `UnknownState`,
    `NotInOptions`, `TooLong`, `TooManyValues`, `UnknownField`; null for
    other form errors), `FieldName`, `Value`, `ValidStates` and
    `MaxLength`, beside the English message, so an app can show its own.
  - `FormFillOptions.Validate` (default false in 3.x; may become true in
    4.0): a choice value that is not an option's export value throws
    (`NotInOptions`; an editable combo box accepts any value), and a text
    value longer than `/MaxLen` in Unicode code points throws (`TooLong`).
    An empty value always clears. Values are never truncated or changed,
    with or without it.
  - `FormFillOptions.UnknownFields` (`FormUnknownFields`: `Ignore`, the
    default and the behaviour before; `Report`, listed in
    `FormFillResult.UnknownFields`; `Throw`, before anything is written).
    The default may become `Throw` in 4.0. `Report` needs the overload that
    returns a result; the others throw `ArgumentException`.
- **`PdfAOptions.AssumeUtcForZonelessDates`** (default false) and
  **`PdfAResult.Warnings`** (D-364, 63C). By default an `/Info` date with no
  time zone is left out of the XMP, and a warning says which date and why
  (nothing is invented). With the option it is written as UTC, also with a
  warning. A date that cannot be read is left out with a warning.
- **`PdfWarningKind.ImageSkipped`** (N7): an image that is not drawn is
  reported: JPEG 2000 (not supported yet), a codec error, a colour space,
  bit depth or size that is not supported, or a `/Mask` that cannot be
  read. Before, such images were skipped in silence.
- **Inline images are drawn** (N4) by the raster, SVG and WPF renderers
  (`BI ... ID ... EI`, §8.9.7, with the abbreviated keys and names, a named
  colour space from the resources, and every filter). Before, they were not
  drawn at all.

### Changed
- **Merge keeps the inputs' forms** by default (`RenameOnConflict`), in both
  `Merge` overloads. A top-level field whose name is taken is renamed
  `name_2`, `name_3`, ...; `/DR` is merged (a clashing resource name is
  renamed and the `/DA` strings follow); `NeedAppearances` and `SigFlags`
  are set when any input sets them; `/CO` is concatenated; XFA is not
  carried. Before, the form was dropped and the widgets stayed as plain
  annotations: `FormFields = MergeFormFields.Drop` keeps that.
  `ShareSameName` joins fields with the same full name, also below a parent
  (`patient.name` in two inputs is one field).
- **Text watermarks are centred by their real width** (D-335): the
  Helvetica widths, or the embedded font's glyph advances. Before, the width
  was estimated as half the font size per character, so every text
  watermark sat a little off centre; each one moves.
- **Write paths honour the cancellation and limits scope** (item 7b).
  `PdfDocument.Save`, fill, merge, assemble, split, stamp, watermark,
  header/footer, flatten, compress, sanitize, redact, PDF/A, XFA render and
  the other write paths apply the limits the source documents were opened
  with and any `PdfWorkScope` the caller opened, checking every object
  written. A failure that is not one of their documented exceptions is
  reported as `PdfCorruptionException` with the original as
  `InnerException` (see `docs/EMBEDDING.md` §6).
- **`FormFiller` keeps `/Info` and the file `/ID`** (D-342). Before, the
  filled copy lost them, and an encrypted source came out unencrypted with
  no way to encrypt it; it is still written unencrypted unless
  `FormFillOptions.Encryption` is set.
- **Filled values outside Latin-1 are written as UTF-16** (item 26), so
  Tamil and other scripts fill; Latin-1 values are written as before.
- **A filled text or choice value removes the field's `/RV`** (D-358 c), the
  rich-text value a viewer could otherwise still show; and **a filled choice
  value sets `/I`** to the indices of the options it matches (for a
  multi-select list box, or a field that already had `/I`), or removes `/I`
  when it matches none, so a stale selection no longer contradicts `/V`.
  Both overloads that existed before do this too.
- **Damaged input is read past with a warning** (#52, D-353), where before
  the page or the open failed:
  - content syntax damage (an unterminated string, an empty name) ends that
    content stream: what came before is drawn and extracted, with a
    `StreamDamaged` warning (raster, SVG, WPF and text);
  - a Type 1 glyph that draws a line or curve with no current point starts
    a subpath there;
  - a stream with no `/Length` is read up to `endstream`; a damaged font
    program draws with the substitute font; a damaged form or `ToUnicode`
    stream gives what was decoded before the damage;
  - inside an object, a dictionary key that is not a name, or an empty
    name, is skipped (an empty name as a value reads as null), with a line
    in `PdfDocument.Warnings`;
  - a page whose MediaBox is missing or invalid is US Letter
    (0 0 612 792), with a line in `PdfDocument.Warnings` (before, reading
    `MediaBox`, `Width` or `Height` threw `PdfCorruptionException`);
  - a page-tree `/Kids` entry that leads back to its own ancestor is
    skipped, with a warning; with `RepairIfDamaged` the pages of a looping
    tree are counted by walking it.
- **`RepairIfDamaged` recovers more** (#52): a catalog whose `/Pages` does
  not lead to a page tree, object streams that decode only in part, a
  cross-reference or object stream that cannot be decoded at open (now a
  structure failure that repair handles; without repair the exception is
  unchanged), and an `/Encrypt` entry that leads to no dictionary (opened as
  unencrypted, with a warning).
- **Crypt filters** (#52, valid files, applies always): `/StmF`, `/StrF` and
  `/EFF` select their own crypt filters, and a stream's own `/Crypt` filter
  is honoured (ISO 32000-1 §7.6.5, §7.4.10). A file whose strings and
  streams are `/Identity` (only its attachments are encrypted) opens
  without a password, with a warning.
- **Printing uses the layers' print state** (#43): with
  `AnnotationMode.Print` the raster, SVG and structured renderers and text
  extraction apply `/Usage /Print /PrintState` (before, the view state).
  `AnnotationFlattener` bakes an annotation that is in a layer inside that
  layer's marked content, so the layer still shows and hides it.
- **Redaction carries the bookmarks and the XMP metadata, scrubbed** (#18):
  patterns with no page list remove their matches from bookmark titles and
  from the XMP packet. Before, bookmarks and XMP were written only if
  something had loaded them, and then unscrubbed. The source `/Info` object
  is never written.
- **`PdfAWriter`** (#49c, #49d, #49g, 63C): every annotation gets the Print
  flag and loses NoView (a Hidden or Invisible one is removed); a Type 1
  subset gets a `/CharSet` (PDF/A-1b); `xmp:CreateDate` and
  `xmp:ModifyDate` follow `/Info` (PDF/A-1b) when its dates give a time zone.
  A date without one stays out of the XMP, as before (validators read a
  zone-less XMP date in their own local zone), now with a warning in
  `PdfAResult.Warnings`; `AssumeUtcForZonelessDates` writes it as UTC. The
  XMP dates no longer depend on the machine's time zone.
- **CMYK JPEGs are read through `/Decode`** (N3), as Poppler and MuPDF read
  them: the stored channel values, inverted only when `/Decode` says
  `[1 0 ...]`. Before, the Adobe inverted-channel rule was applied as well,
  so a PDF that does not invert (MuPDF, Ghostscript and PIL write these)
  drew as a negative. Raster, SVG (now embedded as PNG) and WPF. The display
  list's `ImageOp` is unchanged, and `JpegDecoder.Decode` still follows the
  Adobe rule for a standalone JPEG file.
- **Text scaled up or down by its matrices is hinted at the size it is
  drawn** (N1): when the text matrix and CTM scale Tf by more than 2 or less
  than 0.5, the glyph is fitted at Tf times that scale. Before, only Tf was
  used, so text set as `1 Tf` and scaled by `Tm` was fitted to a one-pixel
  grid (wrong shapes and spacing). Other text is hinted at Tf as in 3.33.0
  (D-377). A rotated or skewed text matrix is no longer hinted; a matrix
  stretched by up to 2% (Word's `[0.9981 0 0 1]`, say) is still hinted.
- **An embedded Type 1 font draws its own glyphs even with a standard-14
  name** (N5, an Adobe "Helvetica" subset, say); before, the standard-14
  substitute was drawn. Its program is no longer handed to the TrueType
  reader, which reported a misleading warning; a Type 1 program that cannot
  be read is reported as `FontSubstituted`.
- **`/Mask` is applied** (N7, raster): a stencil mask hides the samples it
  masks, a colour-key range hides the samples in it. Before, the image was
  drawn unmasked. A mask that cannot be read skips the image (reported).
- **Fonts are read once per page** (N10, raster): forms, patterns and
  Type 3 glyphs that use the same font share it (a page of many small forms
  renders about twice as fast).
- **LZW encoding** (N2): `FilterPipeline.Encode` with `LZWDecode` writes the
  standard stream (wider codes start at the standard point). Streams it
  wrote before 3.34.0 were not standard past 511 codes; Chuvadi's own
  writers never use LZW.
- **Glyph choice** (#36, #37): substitute (non-embedded) fonts draw the
  encoding's glyph, not the `ToUnicode` character, as Poppler and MuPDF do;
  in SVG, an embedded TrueType font whose own cmap would lead the browser to
  another glyph gets a cmap that matches the raster renderer.
- **Round joins** are drawn round (#22), and **a pen under an uneven scale is
  elliptical** (#23).
- **A soft mask set before a form's `Do` masks the form** (#42, raster).
- **`WpfRenderer` applies clips** (#40).
- **A shading inside a soft mask's group, a Type 3 glyph or a pattern cell
  paints in SVG** (#41); before, it painted nothing.
- **JBIG2 and CCITT decodes check cancellation as they go**, and a large
  JBIG2 region is charged against `MaxMemoryBytes` before it is allocated
  (#20).
- **The stamper and composer write references to objects they did not copy
  as null** (#53), as the page operations already did, and a form
  XObject's `/Parent` no longer pulls the source's page tree into the
  output.
- **Shaping is faster** (#27): Devanagari about a fifth faster, other scripts
  a little (each subtable's coverage is checked before it is read).

### Fixed
- **Check boxes and radio buttons fill correctly.** A check box gets the
  widget's own on-state (not always `/Yes`), `/V` is a name, and every radio
  kid gets `/AS` (on for the chosen one, `/Off` for the rest). A value that
  is not one of the button's states throws `FormException`. Pushbuttons are
  left untouched.
- **Image watermarks draw.** `WatermarkStamper.ApplyImage` wrote a whole PNG
  file as raw pixels, so viewers showed noise or nothing. The image is now
  embedded as Flate-compressed samples with its alpha as a soft mask, and
  pages share one image object.
- **Page operations no longer write references to objects they did not
  copy.** Such a reference (for example a form widget's `/Parent` field)
  kept its source number, which in the output named an unrelated object,
  such as a stray copy of a page. It is now written as null. This affects
  merge, assemble, split, extract, delete, rotate and reorder.
- **A damaged cross-reference stream** (`/W` or `/Index` that is not a
  small non-negative integer) throws `PdfParseException`, not
  `InvalidCastException`; widths up to 8 bytes are read in full (found by
  the new write-paths fuzz target).
- **PDF/A-1b: `pdf:Keywords` equals `/Info`'s `/Keywords`** (ISO 19005-1
  §6.7.3), as 3.33.0 did for the title, author and subject.
- **Images drawn rotated, skewed or flipped** (#50, D-348):
  `PageRasterizer` drew such images as their upright box, or skipped them;
  they are now drawn through their full matrix.
- **A stroke inside a form XObject drawn under a scale has its scaled
  width** (raster). Before, the width ignored the CTM at the `Do` and the
  form's `/Matrix` (a 1-unit line in a form scaled ten times drew one pixel
  wide).
- **Redaction no longer gives a new object the number of one the catalog
  still names**; new numbers start past the file's `/Size`. The viewer
  preferences, page labels and layer configuration are written with the
  file (before, an unloaded one was named by the catalog and missing, or
  pointed at a new content stream).
- **Shaped text copies without stray spaces in more viewers.** A cluster
  of several glyphs drawn in text order at their own widths (Tamil "சி" is
  ச then the vowel sign ி) now gets its text glyph by glyph through
  `ToUnicode`, as plain text does, instead of an `/ActualText` span; some
  viewers (Edge) put a space between two such spans, so "அருண் சிவா"
  copied as "அருண் சி வா". Reordered clusters (a vowel sign drawn before
  its consonant) and marks moved onto a base keep `/ActualText`. Applies
  to form, comment and signature appearances and to
  `PageBuilder.DrawShapedRun`.
- **WPF draws a closed path's closing edge** (found by the #40 check on
  Windows). `WpfRenderer` ignored closepath, so a stroked rectangle from
  `re` lost its left side and any stroked closed shape its last edge.
- **LZW streams past 511 codes decode** (N2): the code width grew one code
  early, so real LZW streams (qpdf, Poppler, MuPDF and pdf.js agree) came
  out as garbage after their first few hundred bytes.
- **Inline-image data no longer ends the page** (N4): a `<` or `(` in an
  inline image's binary data was read as a string or dictionary, and the
  rest of the content was dropped (with a "content damaged" warning).
- **An indirect `/DecodeParms` is used** (N6): before, it was ignored, so a
  predictor-coded image drew its predictor bytes as samples.
- **Image data that ends early is drawn** (N13), the missing samples as
  zero, with a `StreamDamaged` warning; a PNG-predicted stream whose last
  row is short is padded. Before, the image was skipped.
- **A page-tree `/Kids` entry that is not a dictionary is skipped with a
  warning** (N9), so the later pages can still be reached.
- **JPEG images (raster, found by the pdf.js run):** a JPEG behind another
  filter (`[/FlateDecode /DCTDecode]`) is decoded (before, it was skipped);
  a soft mask that is itself a JPEG is applied (before, the image was drawn
  without it); JPEG data that cannot be read is reported as `ImageSkipped`
  and never read as raw samples.
- **More found by the pdf.js run (raster):** a soft mask's group paints only
  inside its `/BBox`, the backdrop applying outside it (before, the group's
  content spread past the box); a `/Mask` that is a plain 1-bit gray image
  (no `/ImageMask`) shows the image where it is white, as Poppler and MuPDF
  read it.
- **Merge, split and assemble keep inherited page attributes** (§7.7.3.4).
  A page whose `/Resources`, `/MediaBox` or `/CropBox` sat on its page-tree
  node lost them when copied: its fonts and images were gone and it became
  US Letter. They are now written on the copied page. Found by the
  before/after run on the test bench's Govdocs1 files (also in 3.33.0).
- **A fill closes every open sub-path** (raster, §8.5.3.1). A path drawn
  as `m l l ... f` with no `h` filled nothing or the wrong area, because the
  closing edge was left out (the clip region already closed it). Found with
  the test bench's real files (N14; also in 3.33.0 and earlier).
- **A transparency-group form keeps the alpha it was drawn with** (raster):
  the `/ca` in effect at the `Do` now applies to everything the group
  paints, and an `/ca` inside the group no longer undoes it (each object is
  multiplied, which is exact unless objects inside the group overlap).
  Before, a group drawn at `/ca 0.3` whose content set `/ca 1` painted
  opaque (N14).
- **SVG no longer throws on a stray `Q`, on CMYK image data shorter than
  the image, or on a `Tf` with a stray operand before it** (found by
  fuzzing; also in 3.33.0). `Tf` now takes its last two operands in every
  renderer, and a font operand that is not a name is ignored.
- **A stream whose `/Length` does not end at `endstream` is read up to
  `endstream`**, with a warning, as Poppler and pdf.js do. Before, it was cut
  at the wrong length (a content stream lost its text).
- **Cross-reference tables:** `startxref` pointing at white space before
  `xref` (N8), and `xref 0 76` on one line (N11), are read without repair.
- **`FormFiller` writes every object the catalog and `/Info` name.** It
  loaded only the pages and the form, so a filled copy could name bookmarks,
  XMP metadata or name trees it did not contain (103 of 411 corpus files,
  filled straight after opening), and lost them.

### Tooling
- **Release guards.** `build/pack.ps1` packs only from the clean commit of
  tag `vX.Y.Z` (`-AllowUntagged` for trial packs) and sets `CI=true`, so
  the packages name the released commit. `build/release-assets.ps1` checks
  that every package names the tag's commit before uploading. The release
  order is now merge, tag, check out the tag, pack, upload.
- **Publish Smoke CI job.** A small app using rendering, text and authoring
  is published trimmed and ReadyToRun for linux-x64 with warnings as errors,
  then run.
- **`write-paths` fuzz target**, in the weekly fuzz run.

### Docs
- `docs/CAPABILITY-AUDIT.md`: what Chuvadi does and lacks against viewers
  and editors (Adobe, Windows), kept current every release (D-339). The
  35-item roadmap and `CONFORMANCE-AUDIT` are marked superseded.
- `docs/developer-guide.md`: "Behaviour changes within 3.x", and a
  fill-and-flatten example.
- API docs: members that return or are typed as a named tuple, and
  properties whose body starts on the next line, are documented (#51).
- `docs/EMBEDDING.md`: write paths in §4 to §6.

### Known gaps (D-339)
- JPEG 2000 images are not decoded (planned for 3.36).
- `/Mask` (stencil and colour-key masking) is applied by the raster
  renderer only; SVG and WPF still draw such images unmasked.
- Predefined CJK CMaps and vertical text, the four non-separable blend
  modes and knockout groups (planned for 3.35); several shading types, full
  JBIG2, ICC-based colour and overprint (3.36). `docs/CAPABILITY-AUDIT.md`
  has the release plan and the full list.
- `RepairIfDamaged` stays opt-in (a default switch is for a major version).
- Text is hinted at its drawn size only when its matrices scale it by more
  than 2 or less than 0.5; hinting all text at its drawn size waits for two
  hinting fixes at small sizes (planned for 3.35, backlog 65).

## [3.33.0] - 2026-11-11

Embedding the standard fonts (M1, D-302), PDF/A substitutes that keep the
original font's characters (ticket 46, D-314), repair keeps the newest
object (ticket 48, D-307), bookmarks that keep their target when saved
(PDF-17, D-322, Chuvadi Reader), three bug fixes found on the way (D-318),
Indic text in generated appearances that copies as written (ticket 45),
damaged pages that show what decoded (ticket 39), PDF/A-1b Dublin Core
metadata from `/Info` (ticket 49, D-325), and tooling.

### Added
- **`PdfDocumentBuilder.EmbedStandardFonts()`** (opt-in). The standard 14
  text fonts a new document uses (Helvetica, Times, Courier, all four styles)
  are embedded as subsetted Liberation fonts, which have the same metrics.
  Each face is embedded once and shared by every page, as a TrueType font
  with `/WinAnsiEncoding`. Symbol and ZapfDingbats are never embedded: their
  substitute fonts' licence is not confirmed. Without the call, output is
  exactly as before.
- **`StandardFontEmbedder.Embed(PdfDocument, Stream, StandardFontEmbedOptions?)`**
  (`Chuvadi.Pdf.Operations`), returning `StandardFontEmbedResult`. Writes a
  copy of a document in which its non-embedded standard fonts are embedded.
  - **Where:** fonts in page resources, the forms they use, annotation
    appearances and the interactive form's `/DR`.
  - **Aliases:** Arial, Times New Roman and Courier New (as Word writes them,
    for example `ArialMT`) are embedded too, unless `IncludeAliases` is false.
  - **The result** lists the fonts embedded and, with a reason, the fonts
    left as they were (Symbol, ZapfDingbats, Type0 fonts, unknown fonts).
  - **Encryption and signatures:** an encrypted document throws
    `OperationsException` unless `AllowEncryptedInput` is set; the copy is
    written without encryption, and a signature no longer verifies. The
    source document is not changed.

- **Bookmarks that keep their target (PDF-17, D-322).** Two new
  `OutlineEntry` constructors for `OutlineWriter.Apply`:
  - **`OutlineEntry(OutlineItem source, string title, children)`** keeps a
    bookmark read with `OutlineReader.GetOutlines`, renamed or not. Its
    `/Dest` (named destinations included) or `/A` (any action), colour `/C`,
    style `/F`, structure element `/SE` and open or closed state are written
    back unchanged. The source must come from the document being written,
    otherwise `ArgumentException`.
  - **`OutlineEntry(string title, PdfDestination destination, children)`**
    goes to an exact destination (`/XYZ` left, top and zoom, `/FitH` top,
    `/FitR` and so on), written as given; a null value is written as `null`.
  - New properties `OutlineEntry.Destination` and `OutlineEntry.Source`.
    Entries made with a page index are written exactly as before (the whole
    page, `/Fit`, closed). An open bookmark's `/Count` counts its visible
    descendants, as the specification requires.

### Changed
- **One embedding engine.** `PdfAWriter`, `EmbedStandardFonts()` and
  `StandardFontEmbedder` share it (in `Chuvadi.Pdf.Authoring`). For fonts
  with WinAnsi encoding, PDF/A output is unchanged apart from the header
  line (see Fixed).

### Fixed
- **PDF/A substitutes keep the characters the original font's encoding
  named (ticket 46, D-314).** Before, every substitute was WinAnsi-encoded,
  so a font with no `/Encoding` (StandardEncoding), with `/Differences`, or
  with `MacRomanEncoding` showed some wrong characters: for example `'`
  instead of `’`, `á` instead of `Æ`, a TeX ligature lost. WinAnsi fonts are
  written as before. The others get:
  - **PDF/A-2b and `StandardFontEmbedder`:** a TrueType font with a
    `/Differences` encoding over WinAnsiEncoding, using Adobe Glyph List
    names, and a (3,1) Unicode cmap.
  - **PDF/A-1b:** a symbolic TrueType font with no `/Encoding`, one (3,0)
    cmap that maps each code to its glyph, and a `/ToUnicode` map. PDF/A-1b
    allows no `/Differences` in a non-symbolic TrueType font.
  - Two fonts on one face with different encodings get two substitutes.
    A character the substitute face lacks (as the standard font lacked it,
    such as the ff ligature) keeps its text and stays without a glyph.
- **Repair keeps the newest definition of an object (ticket 48, D-307).**
  Every definition is ranked by its position in the file, an object inside
  an object stream at its stream's position, and the latest wins. Before, a
  direct definition always won, so an incrementally updated file could come
  back with older content (checked: of 385 damaged files, only the one such
  file changes, and it gets its full text back).
- **`MacRomanEncoding` was missing two entries (D-318).** `ã` (0x8B) and `õ`
  (0x9B) were left out, so the 115 codes after them were shifted: text from
  MacRoman fonts came out with wrong characters, for example `‘` for `“`
  and `ß` for `•`.
- **The binary comment on line 2 of every written file (D-318).**
  `PdfWriter` and `LinearizedWriter` wrote it as `%????`; it is now
  `%âãÏÓ` (four bytes above 127), as PDF/A requires. The file length and
  every offset are unchanged.
- **PDF/A-1b metadata (D-318).** The XMP `pdf:Producer` and
  `xmp:CreatorTool` are now taken from the final `/Info` `/Producer` and
  `/Creator`, and are `Chuvadi PDF/A` when `/Info` has none. Before they were
  always `Chuvadi PDF/A`, which failed PDF/A-1b validation when `/Info` said
  otherwise.
- **Indic text in generated appearances copies and searches as written
  (ticket 45, D-325).** Form-field, comment and signature appearances
  (since 3.31) gave ToUnicode text only to the first glyph of each shaped
  cluster, so copying or searching Tamil, Devanagari or Bengali gave stray
  characters (for example `தமி*ழ்நா)டு`). Each cluster now carries its source
  text, as `PageBuilder.DrawShapedRun` does: a glyph that alone shows a
  cluster gets it through ToUnicode when that cannot contradict another use
  of the glyph; any other cluster is wrapped in `/ActualText`. A glyph the
  face lacks (`.notdef`) now extracts as the text it stands for. Drawing is
  unchanged.
- **Text extraction keeps the width of annotation text (ticket 45).** Text
  from annotation appearances lost its measured width, so a shaped cluster
  wider than its character count suggests was followed by a false word
  space (`கொ ள்கை`). The width now comes along, scaled into page space,
  when the baseline stays horizontal. On 4,939 corpus pages no extracted
  text changes.
- **A damaged page content stream (ticket 39, option A, D-325).** Since
  3.32.0 an invalid Flate stream is rejected, and a page whose content was
  damaged threw `FilterException` when drawn or extracted. The page now
  shows and extracts what decoded before the damage, cut back to the last
  whitespace so no half-read operand runs, and reports
  `PdfWarningKind.StreamDamaged` (page number, `page content N G R`); when
  nothing decoded the page is empty. On a set of 50 damaged files, text
  extraction reads all 50 pages (was 20) and drawing all 50 (was 13, with
  the fix below); undamaged files are unchanged.
- **A path continued after `h` or `re` (ticket 39, D-332).** After a
  subpath was closed, a following `l`, `c`, `v` or `y` threw
  `InvalidOperationException` ("requires a current point") when the page
  was drawn. It now starts a new subpath at the closed subpath's start, as
  PDF and Poppler define the current point after `h`. A curve operator
  (`c`, `v`, `y`) with no current point at all is still skipped, now with
  the new `PdfWarningKind.ContentOperatorSkipped`.
- **PDF/A-1b Dublin Core metadata (ticket 49, small part, D-325).** XMP
  `dc:title`, `dc:creator` and the new `dc:description` now follow `/Info`
  `/Title`, `/Author` and `/Subject`, which PDF/A-1b requires (ISO 19005-1
  §6.7.3); where `/Info` has none, `PdfAOptions.Title` and `Author` are used
  as before. An `/Info` value XML cannot hold (a control character) is left
  as before. PDF/A-2b is unchanged.

### Packages
- The 12 Liberation fonts leave `Chuvadi.Pdf.PdfA` (its package goes from
  about 2.4 MB to 32 KB). The engine in `Chuvadi.Pdf.Authoring` reads them
  from the new `Chuvadi.Fonts.Liberation` 1.0.0 package (byte-identical
  files; the monorepo's fonts-liberation-v1.0.0 release, D-311), which
  `Chuvadi.Pdf.Authoring` now depends on.
- `Chuvadi.Fonts` goes from 1.0.0 to 1.1.0 (additive only; the Liberation
  package needs it). `build/fetch-fonts.ps1` fetches and checks both
  packages by SHA-256, `nuget.config` maps both to `.fonts-feed`, and
  `build/pack.ps1` copies both next to the chuvadi-pdf packages.

### Tooling
- `tools/check_style.py` (ticket 47): the `var` rule now runs on relative
  paths, as CI passes them, and ignores `var` inside strings, comments and
  names. The one real `var` it then found (`BrotliSimplePrefixCode`) is
  typed.
- `tools/gen_latin_glyph_names.py` generates the glyph-name table the
  `/Differences` form uses, from the AGL file in the repository.

### Backlog
- Shipped: M1, 39, 45, 46, 47, 48, the small part of 49. Closed: 32
  (already fixed in 3.31.0).
- New: 49, what `PdfAWriter` output still fails in veraPDF. On 413 corpus
  files it now passes 302 at PDF/A-1b and 373 at PDF/A-2b (before: none).

---
## [3.32.1] - 2026-11-09

Opt-in repair of damaged files while opening (D-280, Chuvadi Reader).

### Added
- **`PdfOpenOptions.RepairIfDamaged`** (default false). When set, a file
  whose structure cannot be read is repaired in memory with `PdfRepairer`
  and the repaired copy is opened. This covers a wrong `startxref`, a broken
  cross-reference table or trailer, junk before the header, or a truncated
  file.
  - **Warnings:** `PdfDocument.Warnings` starts with a line saying the file
    was repaired, why, and what was recovered, so `IsRecovered` is true.
  - **What still throws:** a wrong or missing password, a limit, an
    unsupported security handler and cancellation throw as before. When
    nothing usable is recovered, `PdfParseException` carries both reasons.
  - **Stream ownership:** the caller's stream is read from its start. Unless
    `LeaveOpen` is set, it is disposed once the repaired copy is open. The
    file on disk is not changed.
  - **Encryption and metadata:** an encrypted file keeps its `/Encrypt` and
    `/ID`. Objects stored inside its object streams cannot be recovered. The
    repaired copy gets no added `/Info` or XMP metadata.
  - **Overloads:** works through `Open(Stream, PdfOpenOptions)`,
    `Open(string, PdfOpenOptions)` and `OpenAsync(Stream, PdfOpenOptions)`.

### Unchanged
- `PdfRepairer.Repair` writes exactly the same bytes as in 3.32.0. Checked on
  64 damaged files, including two encrypted ones.

### Backlog
- 24 shipped (opt-in). New tickets:
  - 46: PDF/A font substitution ignores the original font's encoding.
  - 47: the style checker's `var` rule and paths.
  - 48: repair can pick an older revision of an incrementally updated file
    that uses object streams.

---
## [3.32.0] - 2026-11-09

New API from the Reader and Print asks (D-208, D-221, D-234, D-243,
D-268 to D-276): signature appearances with text and images, permissions as
flags, how a file was opened, destination details, layer ids and locks,
CropBox display, curved-stroke redaction, flatten by print setting, named
compression levels with colour and black-and-white image levels, and a
font-descriptor rule. Also fixes to Indexed images, form XObject state,
Deflate, and rotated pages in the rasterize fallback.

### Added
- **Signature appearances with text and an image** (1c). Without a
  pre-rendered stream, a visible signature now shows "Digitally signed by
  {CN}" and "Date: {UTC time}", sized to fit, inside the 0.5 pt border.
  - `SignatureAppearance.Text` replaces those lines.
  - `SignatureAppearance.Image` (JPEG or PNG) is drawn on the left third,
    or fills the box when there is no text.
  - Scripts the standard fonts cannot write use the matching LiPi Sans face,
    embedded as a subset.
  - `PreRenderedAppearanceStream` still wins when given.
  - Applies to `Sign`, `SignAsync` and `PdfCounterSigner.AddSignature`.
  - Any other image format throws `ArgumentException`.
  - `Chuvadi.Pdf.Signatures` now depends on Forms and Authoring.
- **Permissions as flags** (2):
  - `EncryptionInfo.Flags` (`PdfPermissions`), decoded from `/P`;
  - `EncryptionOptions.PermissionMask(PdfPermissions)`;
  - `EncryptionOptions.WithPermissions(PdfPermissions)`;
  - `EncryptionOptions.WithEncryptMetadata(bool)`, so metadata can be left
    unencrypted.
- **`EncryptionInfo.EffectiveFlags`** (D-279): `All` when the file was
  opened with the owner password, otherwise `Flags`. The owner password gives
  full access, so an app should check this before allowing printing, copying
  or editing.
- **How a file was opened** (3, PDF-16): `EncryptionInfo.OpenedWith`
  (`PdfPasswordKind`: `NoPassword`, `User`, `Owner`). Also
  `Decryptor.OpenedWith`, `PdfReader.OpenedWith`, and a
  `StandardSecurityHandler.ValidatePasswordR6` overload that says which
  password matched.
- **Destination details** (4):
  - a new `PdfDestination` type (page index, `PdfDestinationKind`, left, top,
    right, bottom, zoom);
  - `OutlineItem.Destination` and `LinkAnnotation.Destination`;
  - `DestinationReader.GetNamedDestinations(PdfDocument)`.
- **Layers** (5):
  - `OptionalContentGroup.IsLocked` and `.Id` (the object number);
  - `OptionalContentVisibility.WithOverridesById`;
  - `RenderOptions.LayerVisibilityById` and `SvgExportOptions.LayerVisibilityById`.
  - Id overrides beat name overrides, and they apply to locked layers too.
- **`RenderOptions.DisplayBox`, `SvgExportOptions.DisplayBox` and
  `WpfRenderer.DisplayBox`** (`PdfDisplayBox`: `CropBox`, the default, or
  `MediaBox`). See Changed.
- **`AnnotationFlattenOptions.Mode`** (M2). `View` or `Print` flattens what
  that mode shows and removes the rest; any other value throws
  `ArgumentException`. Null keeps the 3.31.2 rule.
- **Named compression levels** (P4, D-243, D-268 to D-274):
  - `PdfCompressor.Compress(PdfDocument, Stream, PdfCompressionLevel,
    ImageLevelOptions?, CompressionOptions?)`.
  - `PdfCompressionLevel`:
    - `Lossless`;
    - `Medium` (JPEG 75, 150 dpi);
    - `High` (JPEG 50, 100 dpi);
    - three image levels that make every page one image:
      - `ReducedColor`: up to 16 colours; dark greys become black so thin
        text stays dark;
      - `BlackAndWhiteWithColor`: colour marks kept, the rest black and
        white;
      - `BlackAndWhite`: 1-bit.
  - Medium and High also re-encode JPEG images already in the file, and keep
    the result when it is smaller.
  - `ImageLevelOptions`:
    - `Dpi`: 150 by default, 50 to 300 accepted;
    - `Fills` (`BlackAndWhiteFills.DotPattern` or `Solid`);
    - `AllowBelowReadableResolution`: below 130 dpi only when set; the result
      is then flagged in `BelowReadableResolution`.
  - `CompressionResult.Level`, `.ImageDpi` and `.BelowReadableResolution`.
- **Target mode by level:**
  - `CompressToTargetOptions.MaxLevel` and `.ImageOptions`;
  - `CompressToTargetResult.LevelUsed`, `.ImageDpiUsed` and
    `.BelowReadableResolution`.
  - The steps, in order: Lossless, Medium, High, ReducedColor 150,
    BlackAndWhiteWithColor 150, ReducedColor 130, BlackAndWhiteWithColor 130,
    BlackAndWhite 150, BlackAndWhite 130. It writes the first output that
    fits, and never goes below 130 dpi unless allowed.
  - Each page is rendered once per resolution.
  - Without `MaxLevel`, target mode is unchanged.
- **`PdfWarningKind.StreamDamaged`** (D-271). Reported for:
  - an object stream that cannot be decoded (its objects are missing);
  - an image or form XObject the raster renderer skips because its data
    cannot be decoded.
- **`build/release-assets.ps1`** attaches a release's packages and
  `SHA256SUMS.txt` to its GitHub release (D-234, D-240). `pack.ps1` now
  points to it.

### Changed
- **The CropBox is the shown page** (P2). Raster, SVG and WPF output show
  the CropBox intersected with the MediaBox, from its own corner.
  - Pages without a CropBox, or with one equal to the MediaBox, are
    unchanged; that is every page of the 417-file corpus.
  - `DisplayBox = PdfDisplayBox.MediaBox` gives the old output.
  - `RenderRegion`, text extraction and `PdfPage.Width` and `Height` are
    unchanged.
  - **Note for callers (added in 3.34.0, D-334).** This is a change in
    output size and origin for pages whose CropBox is smaller than their
    MediaBox. A consumer built against an earlier 3.x that sizes its canvas
    or places marks from `PdfPage.Width`, `Height` or `MediaBox` will be off
    on those pages: for example Pdf.Raster 0.1.0 running with chuvadi-pdf
    3.32.0 or later. Use `PageRasterizer.GetOutputSize` for the image size,
    and `PdfPage.GetDisplayBox` and `GetDisplayTransform` to map between the
    shown page and PDF coordinates (all added in 3.34.0); or set
    `DisplayBox = PdfDisplayBox.MediaBox` to keep the old output.
    `docs/developer-guide.md` lists every behaviour change within 3.x.
- **Redaction cuts curved strokes at the box edges** (P3). Before, a curved
  stroke crossing a box was removed whole; now the parts outside are kept.
  - Pieces are kept only when their control hull provably clears every box,
    so no ink is kept inside a box.
  - Curved fills crossing a box are still removed whole.
- **Passwords are tried as owner first** (3). This does not change
  decryption. A file whose user and owner passwords are the same reports
  `Owner`.
- **`PdfPage.Viewports` is read once per page** (6). Repeated calls return
  the same list.
- **Font descriptors** (D-244, D-245, D-263, D-275). Every font Chuvadi
  embeds (`AddTrueTypeFont`, PDF/A substitutes, the LiPi fallback in form
  fields and signatures) now follows one rule:
  - `/StemV` is 50 + (weight / 65)², for example 88 for regular and 166 for
    bold. It was always 80.
  - `/Flags` add FixedPitch and Serif from the font's own tables.
  - The LiPi fallback (Identity-H) is Symbolic (4), not Nonsymbolic (32).
  - `Chuvadi.Pdf.Forms` now depends on `Chuvadi.Pdf.Authoring`.
- **Invalid Flate streams are rejected** (D-271).
  - The inflater refuses an over-subscribed Huffman code set, as zlib does.
    Before, such a stream decoded silently to wrong bytes.
  - A damaged object stream adds a line to `PdfDocument.Warnings` saying why,
    and its objects are missing as before. The file still opens unless that
    stream holds the catalog.
  - Damaged page content still throws `FilterException` for that page.

### Fixed
- **Integers outside the 32-bit range** (D-278). One made the whole object
  fail to parse, so pypdf files with an unsigned `/P` (for example
  4294966228) did not open. Such a value is now read as a real, and `/P` is
  taken from its low 32 bits. `PdfWriter` also no longer writes large or
  tiny reals with an exponent (`4.29497E+09`), which is not PDF syntax.
- **Indexed (palette) images.** The raster renderer skipped every Indexed
  image, and SVG misread 1, 2 and 4-bit ones. Both now draw them, honouring
  `/Decode`. In the corpus, 31 GHS pictograms that were blank now match
  poppler.
- **Form XObjects inherit the graphics state at the `Do`** in the raster
  renderer (D-259): colours, line and text state, alpha and blend mode. For
  example, a 75% grey LibreOffice "DRAFT" watermark at half transparency
  printed as dark grey 128 and now prints as about 223.
- **The Deflate encoder could write a corrupt stream** on very large,
  skewed input of about 9 MB and up, such as big page PNGs (D-266). Code
  lengths are now limited correctly. Output is unchanged wherever it was
  already valid.
- **`CompressToTarget`'s rasterize fallback kept rotated pages sideways**
  (R1). They now come out upright.
- **`AnnotationMode` was dropped** by supersampling, `RenderRegion`,
  `RenderClipped` and the SVG raster fallback (R2). `RenderRegion` and
  `RenderClipped` now also keep `LayerVisibilityById`, `DisplayBox` and
  `ApplyPageRotation`.
- **`WpfRenderer` ignored the box's corner** (backlog 38). A page whose box
  does not start at 0,0 is drawn where it is, clipped to the box.

### Tooling
- `tools/check_style.py`: a member named `Flags` no longer counts as the
  `[Flags]` attribute (no `using System;` needed).

### Backlog
- `docs/BACKLOG.md`:
  - shipped the 3.32.0 plan, PDF-09 and 38;
  - new tickets:
    - WPF clipping (Q10);
    - curved fills in redaction;
    - SVG shading inside groups;
    - Indic `ToUnicode` in generated appearances;
    - the soft mask in effect at a form's `Do`;
    - annotation `/OC` and print-state layers;
    - damaged page content drawn blank with a warning (D-276).

---
## [3.31.2] - 2026-11-06

Bug fixes (D-207, D-210, D-217, D-219, D-221, D-226): signing real documents,
counter-signature appearances, links to named destinations, a bounded name
table, a thread-safe `FontRenderer`, pages whose MediaBox does not start at
0,0; plus the platform items C, D, E(a) and H and SDK package validation.

### Fixed
- **`PdfSigner.Sign` and `SignAsync` on real documents.** They took the first
  `/ByteRange` and `/Contents` in the written file, which on any document
  whose pages have a `/Contents` entry is a page, not the signature, so
  signing failed ("Could not locate /Contents value") or patched the wrong
  bytes. They now find the signature dictionary through the cross-reference
  table they have just written and search only inside that object.
- **`Sign` and `SignAsync` refuse a document that is already signed**, with
  `InvalidOperationException`: "This document already has a signature; use
  PdfCounterSigner.AddSignature to add another." Rewriting the file would
  break the existing signature.
- **`PdfCounterSigner.AddSignature` honours `PdfSigningOptions.Appearance`.**
  It was silently ignored. The signature now gets a visible widget on the
  given page, drawn as `PdfSigner` draws it (a border unless a
  pre-rendered appearance is given), and the page is rewritten in the
  incremental update so that its `/Annots` lists the widget. Earlier
  signatures still verify. The counter-signer also finds its placeholders
  inside its own signature object.
- **`PdfSigner.SignAsync` honours `PdfSigningOptions.Appearance`** (D-226).
  It skipped the widget step, so async signatures were always invisible
  although its documentation said it behaved like `Sign`. It now uses the
  same widget code as `Sign`.
- **`Appearance` together with `LtvOptions` no longer loses an object**
  (D-226). The appearance XObject and the first LTV stream were both
  numbered right after the AcroForm, and only one was written, so the DSS
  `/Certs` entry pointed at the appearance form. LTV objects are now numbered
  after the widget objects. Without an appearance, the numbering is as
  before.
- **Links to named destinations.** `AnnotationReader` dropped a link whose
  `/Dest` or `/GoTo` `/D` was a name or string; it now resolves it through
  the `/Names /Dests` tree and the catalog's `/Dests`, as outlines already
  did. On the 417-PDF corpus, links read go from 55 to 131.
- **Page indexes from the page tree.** Links and outlines took a page's index
  from the order of `/Page` objects in the object store, which need not be
  the page order. Both now walk `/Pages` → `/Kids` by the same rule as
  `PdfDocument.Pages`. No corpus file changes.
- **The PDF name table is bounded.** Every name read from any file was
  interned for the life of the process, so a long-running server grew with
  every new name. Names now compare by value (`==`, `Equals`, hashing, and
  `PdfDictionary` keys); the names Chuvadi uses and up to 4,096 others of up
  to 64 characters share one instance each; any other name is freed with its
  document. Output is identical; reference equality between two equal names
  is no longer guaranteed.
- **One `FontRenderer` can be shared between threads.** Its glyph caches
  are concurrent and hinting runs one glyph at a time per instance. Before,
  concurrent use (LiPi's shared renderers) could corrupt the caches or throw.
- **Pages whose MediaBox does not start at 0,0** (every page cropped by
  `PageCropper`, for example) are drawn where they are by the raster and SVG
  output. Before, the content was shifted by the box's corner and such pages
  could come out blank. Pages whose box starts at 0,0 are unchanged (no
  corpus file has another origin). The WPF output has the same fault and is
  not changed (backlog 38).
- **Raster: spans thinner than the rasterizer's 1e-9 guard.** A span that
  started on a pixel boundary and was thinner than the guard was treated as
  two columns: at the left edge it indexed column -1 and the page failed
  with `PdfCorruptionException`; elsewhere it added a whole pixel of
  coverage to two columns. It now counts as the one column it is in. Pages
  cropped with `PageCropMode.ClipOnly` hit this once P1 drew them from their
  own corner (their clip edge falls exactly on x = 0). No corpus file
  changes.

### Changed
- **`IsAotCompatible`** is set on every package under `src/` except
  `Chuvadi.Pdf.Rendering.Wpf` (D-217 C), which turns on the trim, AOT and
  single-file analyzers; there are no warnings.
- **`AllowUnsafeBlocks` removed** from Filters, Fonts, IO and Primitives
  (D-217 H). None of them has unsafe code; their DLLs no longer carry the
  `UnverifiableCode` and `SkipVerification` markers.
- **SDK package validation** (D-221). Every package is validated when packed.
  `build\pack.ps1 -BaselineVersion 3.31.1 -BaselineDir <folder>` also
  compares each package with that release and fails on any public API
  removed or changed since; CI packs without a baseline.

### Documentation
- **`docs/THREAD-SAFETY.md`** (D-217 D): one `PdfDocument` per thread;
  `FontFace`, `TextShaper`, `FontRenderer` (from 3.31.2) and `PdfName` are
  safe to share. Remarks on `PdfDocument`, `FontFace`, `TextShaper` and
  `FontRenderer` point to it.
- **`TextShaper.Shape(byte[], …)`** (D-217 E(a)): the face is found again by
  the array instance, and `FontPack.ReadAllBytes` returns a new array every
  call, so keep the array or use the `FontFace` overload. Its two remarks
  blocks are merged; the old second one, which said the face was loaded on
  every call, was out of date since 3.26.
- **`PageCropper`** (P5): `PageCropMode.Scrub` is the redaction-grade mode;
  `PageCrop.CropBox` is in default user space (points, origin bottom left,
  the MediaBox's space, before `/Rotate`).
- **`PdfName`**: value equality and the bounded table.

### Backlog
- `docs/BACKLOG.md`: shipped 3.31.2; PDF-09 open with the P4 options;
  PDF-13 retired as a duplicate of PDF-16; the 3.32.0 plan (D-208 items, P2,
  P3); ticket 38 (WPF MediaBox origin).

---
## [3.31.1] - 2026-11-06

`FontFace.Load` speed (D-201). No output change.

### Changed
- **Faster font loading.** Since 3.31.0 (D-169) the character map loader
  read every full-repertoire Unicode subtable (formats 12 and 13 under
  (3,10) and (0,4) / (0,6)) and kept only the best. It now reads them best
  first and stops at the first that gives mappings, so subtables that cannot
  be chosen are no longer read. The choice is unchanged: Windows before
  Unicode, then the first in the table directory, and a damaged or empty
  subtable falls through to the next. Affects `FontFace.Load`,
  `TrueTypeLoader`, `FontRenderer` and everything that loads TrueType or
  OpenType fonts. Output is identical (measured on fonts and on the PDF
  corpus).
- **The HarfBuzz corpus generator sets the language explicitly**
  (`tools/shaping-data/hb_conformance.py`, D-182). It sets direction, script
  and no language on every line instead of guessing them, so the result never
  depends on the machine's locale. Both corpora and the test fixture come out
  byte for byte the same.

### Backlog
- New tickets 35 to 37 in `docs/BACKLOG.md` (D-201): hinting item 15, SVG
  text when ToUnicode contradicts the encoding, and glyph choice in
  non-embedded fonts.

---
## [3.31.0] - 2026-11-05

Everything a printer needs from the structured display list; form fields and
comments drawn from their own entries; the LiPi Sans faces from Chuvadi.Fonts;
character maps above U+FFFF.

### Added
- **Annotation modes.** `AnnotationMode` (`View`, `Print`, `FormFieldsOnly`,
  `None`) on the structured `DisplayListBuilder.Build`, the raster builder,
  `RenderOptions`, `SvgExportOptions` and `TextExtractor`. `View` is the
  default and matches 3.30 (NoView and Hidden respected). `Print` lets the
  Print flag decide and ignores NoView. `FormFieldsOnly` keeps widgets and
  drops the page content and every other annotation. `None` drops all
  annotations.
- **Generated appearances.** `IAnnotationAppearanceProvider` (Documents),
  `FieldAppearanceGenerator` (Forms), `CommentAppearanceGenerator` and
  `GeneratedAppearances.Instance` (Annotations). Text fields (single-line,
  multiline, comb, password), combo and list boxes, check boxes, radio
  buttons and push buttons are drawn from their values; Highlight,
  Underline, Squiggly, StrikeOut, Square, Circle, Line, Polygon, PolyLine,
  Ink, FreeText, Text (7 note icons), Stamp (14 standard names as words,
  red when no `/C`), Caret and FileAttachment (4 icons) from their entries.
  Signature fields and Sound are never generated. Generated streams live in
  memory only; the document is not changed. Characters the field's font
  cannot show are drawn with the LiPi Sans face of their script, in the
  style of the `/DA` font, with a `FontSubstituted` warning.
- **Fonts on the structured list.** `DisplayListFont` (program bytes and
  kind, subset flag, units per em, widths, glyph ids, glyph outlines,
  embedding rights, CID system info, substitute), `TextOp.Font`,
  `DisplayListGlyph.Code` and `CodeLength`, `PageDisplayList.Fonts`.
- **`FontEmbeddingReport.Check(PdfDocument)`**: an all-fonts-embedded
  verdict (`AllEmbedded`, `NotAllEmbedded`, `Unknown`) covering pages, form
  XObjects, patterns, Type 3 resources and annotation appearances, with each
  font that is not embedded (`FontUse`) and `NeedsGeneratedAppearances`.
- **Images:** `ImageOp.Source`, `Decode`, `IsStencilMask`, `StencilColor`,
  `JpegColorTransform`, `JpegComponents`, `IsCmykJpeg`, `IccProfileStream`,
  `GetIccProfile()`.
- **Shadings:** `ShadingOp.ShadingType`, `Shading`, `IsExpressible`,
  `GetShadingData()`.
- **Page boxes:** `PageDisplayList.MediaBox`, `CropBox`, `BleedBox`,
  `TrimBox`, `ArtBox` (clipped to the media box); `PdfPage.BleedBox`,
  `TrimBox`, `ArtBox` and `AcroForm`.
- **Op tags:** `RenderOp.Annotation` (`AnnotationSource`: the annotation, its
  subtype, flags, field name and whether its appearance was generated).
- **Transparency groups (D-173):** `RenderOp.GroupIds` (the ids of the groups
  an op is inside, outermost first, for example `[7, 9]`),
  `RenderOp.GroupTransparency` (`OpTransparency`: the op's own opacity, blend
  mode and soft mask inside its group) and `PageDisplayList.TransparencyGroups`
  (`TransparencyGroup`: id, parent, depth, isolated, knockout, colour space,
  box, matrix, the group's own opacity, blend mode and soft mask, its form, and
  its run of ops). Each group's ops are one unbroken run, nested inside its
  parent's; groups that paint nothing are left out; the same form drawn twice
  is two groups. `PageDisplayList.HasTransparency` also counts groups. No new
  op types; SVG, WPF and raster output do not change.
- **Variation sequences:** `FontFace.GetVariantGlyphIndex(codePoint,
  variationSelector)`, from the cmap format 14 subtable.
- `PdfWarningKind.AppearanceNotGenerated`.
- `PdfFont.GetEncodingCharacter(code)`: the character a simple font's
  encoding gives a code, without ToUnicode.

### Changed
- **Default (View) output.** Forms with `/NeedAppearances` true show their
  values (forms filled by `FormFiller` included); widgets with a value but no
  `/AP`, and comments without `/AP`, are drawn. Text extraction returns those
  values and FreeText contents. `AnnotationFlattener` flattens generated
  appearances. Annotations with a current `/AP` are unchanged.
- **Bold Indic text in fonts a PDF does not embed** is drawn with the LiPi
  Sans Bold face of its script instead of Regular. Ink is heavier and wider;
  the position of the text after each run does not change. Italic Indic text
  stays upright.
- **LiPi Sans comes from `Chuvadi.Fonts` 1.0.0** (static TTFs) in Authoring
  and Raster. The Regular faces have the same outlines, advances and hinting
  tables as the old defaults. Authoring output: `/W`, ToUnicode and rendered
  pages are the same; the embedded subsets are 9% to 36% smaller.
- **Structured display list dependencies (O10, D-170).** The DisplayList
  package now depends on Annotations, Forms, Text.Shaping and Chuvadi.Fonts:
  4.62 MB of assemblies (Chuvadi.Fonts.dll 4.11 MB, Text.Shaping 0.37 MB,
  Annotations 0.08 MB, Forms 0.06 MB), so every caller gets generated
  appearances. Raster no longer depends on DisplayList. Forms and Annotations
  gain the font packages; Text and Operations gain Annotations.
- `PdfBlendMode`, `BlendModes`, `Type3Font` and `Type3Glyph` moved to the
  Walking assembly (same namespace, type-forwarded from DisplayList).
- `AdobeGlyphList` and `GlyphNameToUnicode` moved from Fonts.Rendering to
  the Fonts assembly (same namespace, type-forwarded), so simple-font
  encodings can use the whole list. The Fonts assembly grows from 36 KB to
  221 KB.
- Shadings of types 1 and 4 to 7 now appear in the structured op stream as
  `ShadingOp` with `IsExpressible = false` (they were skipped). SVG and WPF
  skip them, so their output does not change.
- `FontRenderer.MeasureText` and `LayoutText` step by code point: a
  surrogate pair is one glyph.
- The API documentation generator keeps a type's summary across attribute
  lines such as `[Obsolete]` and `[Flags]`, and names expression-bodied
  properties whose body calls a method correctly.

### Deprecated
- `Chuvadi.Pdf.Fonts.Bundled` (`BundledFonts`) and `Chuvadi.Pdf.Fonts.Woff2`
  (`Woff2Unpacker`, `Woff2Packer`, `BrotliEncoder`, `BrotliStoredEncoder`)
  are `[Obsolete]` (warning CS0618). Use `Chuvadi.Fonts` (`FontPack`). They
  are removed at the Pdf move.

### Fixed
- **Character maps above U+FFFF.** The TrueType loader reads cmap formats
  12 and 13 and prefers them in the OpenType order (full-repertoire Windows
  3,10 and Unicode 0,4/0,6 before BMP-only 3,1 and 0,3). Before, the first
  format 4 subtable won, so every character above U+FFFF was `.notdef` in
  FontFace, FontRenderer, the shaper, Authoring and the raster renderer
  (45 code points of LiPi Sans in the oracle's vector). A damaged format 12
  or 13 subtable is skipped. Formats 2, 8 and 10 are still not read.
- **Variation sequences** listed in a font's format 14 subtable are shaped to
  their variant glyph (one glyph, selector dropped, as HarfBuzz does).
- **OpenType fonts with CFF outlines** look Unicode characters up in their
  sfnt cmap first, then by glyph name.
- **Surrogate pairs** select the glyph of the whole character (before, the
  high surrogate alone): in the shared glyph rules for CFF simple fonts,
  whose glyph still comes from the decoded text, and in the SVG font
  embedder's cmap.
- **The same font name in a page and a form XObject** (for example `/F1`)
  gives two fonts on the structured list (the second keyed `F1#2`), each with
  its own encoding and widths. Before, the form's text used the page's font:
  on a Certificate of Incorporation page, regular-weight text was measured
  with Times-Bold widths (SVG text 3.95 pt from MuPDF's positions on average;
  now 0.91 pt).
- **`/Differences` encodings (D-172).** Every Adobe Glyph List name in a
  `/Differences` array is applied. Before, only about 90 punctuation and
  symbol names were known, so entries naming letters, digits, accented
  letters, Greek or maths symbols ("A", "eacute", "one", "rho", "infinity",
  "nacute", "multiply") were ignored and the code was read as itself. An
  encoding dictionary without `/BaseEncoding` now starts from the standard 14
  font's built-in encoding: StandardEncoding for the twelve text fonts, and
  the Symbol and ZapfDingbats fonts' own encodings (PDF 32000-1:2008
  §9.6.6.1, Annex D). Other fonts keep WinAnsiEncoding as that base. The
  `/StandardEncoding` table has all 149 codes (before, only 0x20 to 0x7E).
  On the 417-PDF corpus: text, search and SVG change on 21 pages of 13 PDFs
  (TeX and Latin Modern documents), and raster on 5 PDFs; every change moves
  closer to MuPDF (for example "Gryń", "200×200 bp", "ρ = ∞", "e^87i·φ").
- **Glyph choice in embedded TrueType simple fonts follows the spec (D-175).**
  The glyph comes from the font's encoding (base encoding and `/Differences`,
  through the Adobe Glyph List, then the Unicode cmap), not from its
  ToUnicode CMap, which stays for extraction and search (PDF 32000-1:2008
  §9.6.6.4), as MuPDF does. CFF programs keep the 3.30 order. No change on
  the 417-PDF corpus.
- **Implicit base encodings (D-176).** A `/Differences` dictionary without
  `/BaseEncoding` in a font that is not standard 14 starts from the base
  Table 114 gives: an embedded program's built-in encoding (a Type 1
  program's `/Encoding`, a CFF program's Encoding; StandardEncoding for a
  non-symbolic TrueType program), otherwise StandardEncoding for a
  non-symbolic font. A symbolic font whose built-in encoding cannot be read
  keeps WinAnsiEncoding. A standard 14 font with no `/Encoding` uses its
  built-in encoding (StandardEncoding, or Symbol's or ZapfDingbats' own)
  instead of WinAnsiEncoding. The encoding tables of 58 corpus fonts change,
  in codes their pages do not use: no output change on the 417-PDF corpus.
- **SVG clip-path and mask ids** are the same on every run for the same input
  (a fixed FNV-1a hash replaces `string.GetHashCode`). Rendering does not
  change; the id values do.

### Build
- Restore needs `Chuvadi.Fonts.1.0.0.nupkg`: run `build/fetch-fonts.ps1`
  (downloads the fonts-v1.0.0 release asset and checks its SHA-256). It is
  wired into `setup.ps1`, `pack.ps1` and the CI workflows; `nuget.config`
  maps `Chuvadi.Fonts` to the local `.fonts-feed` folder. Every consumer feed
  needs the package next to the 3.31.0 packages.
- `CffStandardStrings` moved from Fonts.Rendering to Fonts (internal, with
  `tools/gen_cff_standard_strings.py` writing there); its output is
  unchanged.
- 3,165 tests across 30 test projects.

## [3.30.2] - 2026-11-03

SVG text in embedded fonts sits where the PDF puts it.

### Fixed
- **SVG: text in embedded fonts is placed at the PDF's glyph positions.**
  Every character of a `<text>` element now gets its own `x` position from
  the PDF (its `/Widths`, character and word spacing and `TJ` adjustments),
  as text in fonts that are not embedded already did. Before, a run in an
  embedded font was anchored once and the browser advanced each glyph by
  the font's own widths. Where those differ from the PDF, text drifted: a
  space the PDF gives no width but the font draws 500 units wide pushed the
  run one space to the right (an Elsevier paper shows " patients" a space
  past its search highlight), and letters overlapped or spread unevenly.
  Search highlights and selection now line up with the drawn text.
- **CFF advance widths after `hmoveto`, `vmoveto` and an opening
  `hintmask`.** A Type 2 charstring may start with the glyph's width as an
  extra operand. It was found only when the operand count was odd, which is
  right for stem hints, `rmoveto` and `endchar` but not for `hmoveto` and
  `vmoveto` (one operand, so a width makes two), and it was not looked for
  before an opening `hintmask`. Those glyphs got `defaultWidthX` instead.
  This corrects the advance widths of embedded CFF fonts in SVG output
  (the cause of overlapping letters such as "Recomended" and
  "indcators") and in raster output where a font has no `/Widths`.
- **Output changes** on 417 PDFs (up to 5 pages each): SVG output changes on
  118 pages, only in the `x` positions of `<text>` elements and in the
  advance widths inside embedded CFF fonts; raster output changes on 2
  pages (TeX Gyre font tables whose CFF fonts lack some widths, now spaced
  evenly); extracted text and warnings are unchanged.

---
## [3.30.1] - 2026-11-02

FreeType credit (D-106, part 1). No code or output change.

### Changed
- **The FreeType credit ships with the hinting code.** The TrueType
  instruction interpreter and the autohinter in
  `Chuvadi.Pdf.Fonts.Rendering` are derived in part from FreeType, which
  Chuvadi uses under the FreeType License (FTL). `THIRD-PARTY-NOTICES.md`
  now credits FreeType, the `Chuvadi.Pdf.Fonts.Rendering` package carries
  that file and the unaltered licence (`licenses/FreeType/FTL.TXT`), the
  README credits it, and the derived source files say so in their headers.
  The licence is also in the repository as `third-party/freetype/FTL.TXT`.

---
## [3.30.0] - 2026-11-02

Fonts that went missing (D-105). Text drawn with a font Authoring cannot
write is refused instead of coming out blank; font names are always written
as valid PDF names; the raster renderer draws fonts a PDF names but does not
embed; and fonts in SVG output load in browsers (Chromium, WebView2).

### Added
- **Chuvadi.Pdf.Fonts.Bundled**, a new package (34 packages now) holding the
  one copy of the bundled LiPi Sans faces (Latin and nine Indic scripts,
  WOFF2) and their SIL OFL 1.1 licence: `BundledFonts.FaceNames`,
  `FaceForScript` and `GetFontProgram` (decoded once, shared). Authoring and
  the raster renderer both use it (D-87).
- `Standard14Substitute` (Chuvadi.Pdf.Fonts.Rendering): picks the standard
  14 font (Liberation Sans, Serif or Mono outlines) that stands in for a
  font a PDF does not embed, from its name, descriptor flags, `/FontWeight`
  and `/ItalicAngle`; `OutlineSource` names the Liberation face.
- `RenderableFont.AsSubstitute(fontDict, resolver, standard14Name)`.
- `TrueTypeFontPatch.WithRequiredTables(fontBytes, postScriptName, isBold,
  isItalic)`: adds minimal `OS/2`, `name` and `post` tables where missing
  and re-assembles the font with every table 4-byte aligned.

### Changed
- **`PageBuilder.DrawText` and `DrawTextBlock` throw `ArgumentException`
  (parameter `font`) for a font name that is not one of the standard 14, a
  font added with `AddTrueTypeFont`, or `"Lipi"` after `UseLipiFonts`.** The
  message lists the names that are available. Before, any other name was
  written as a Type 1 font with no program and no widths, so its text came
  out blank in every viewer. `"Lipi"` is the one name `UseLipiFonts`
  registers (case-sensitive); the per-script face names are internal.
- **Font resource names are written as valid PDF names.** A name with a
  space or a delimiter (`AddTrueTypeFont("Body Serif", …)`) is reduced to its
  letters, digits, `.` and `_` (`BodySerif`); before, the space was written
  into the content stream and broke the `Tf` operator, so the text was
  invisible. Two fonts whose keys would be the same get a number
  (`HelveticaBold`, `HelveticaBold2`); before, the second replaced the first.
  Keys of other names are unchanged.
- **The raster renderer draws text in fonts the PDF does not embed** (simple
  Type 1 and TrueType fonts, and Type 0 fonts through their ToUnicode map)
  with a standard 14 substitute: Liberation Sans for Arial, Helvetica and
  other sans faces, Liberation Serif for Times and other serif faces,
  Liberation Mono for Courier and fixed-pitch faces, bold and italic from
  the name or descriptor. Characters stay at the PDF's `/Widths` (or `/W`)
  positions with their natural width, and a `FontSubstituted` warning names
  the font and the substitute. Before, such text drew nothing, with no
  warning. Picture fonts (Wingdings, Webdings) have no substitute and are
  reported. Indic text in a Type 0 font that is not embedded (Mangal,
  Nirmala UI and the like) is decoded through its ToUnicode map, shaped with
  the bundled LiPi Sans face for its script and drawn from the run's start,
  while the codes keep their `/W` positions (D-100). Text in scripts neither
  covers (Arabic, CJK) is not drawn and is reported as `GlyphSkipped`.
- **The LiPi Sans fonts moved from Chuvadi.Pdf.Authoring into
  Chuvadi.Pdf.Fonts.Bundled**, with their licence file. Authoring now depends
  on the new package (its install size is unchanged); Chuvadi.Pdf.Rendering.Raster
  now depends on it and on Chuvadi.Pdf.Text.Shaping, so apps that install the
  raster renderer download the fonts and the shaper too.
- **Resource names with `#xx` escapes resolve** in the raster and SVG
  renderers (fonts, colour spaces, images, graphics states, shadings,
  patterns): `/F#20A` finds the key `F A`. Before, the name was looked up
  undecoded and missed.
- **SVG output: embedded fonts load in browsers.** Fonts without `OS/2`,
  `name` or `post` get minimal ones and every font is re-assembled with
  aligned tables; a simple TrueType font with only a Macintosh cmap gets a
  Windows Unicode cmap mapping the characters the SVG text uses; a simple
  CFF font whose glyph names are not in the Adobe Glyph List (TeX maths
  fonts) is mapped through its `/Differences`; a bare CFF program that still
  cannot be wrapped is no longer embedded (the text uses the fallback font
  instead of an undecodable one). On the 417-file corpus, Chromium rejected
  13 of 493 embedded fonts before and none after.
- **Output changes** on 417 PDFs (up to 5 pages each, 534 pages): raster
  output and extracted text are unchanged on every page; SVG output differs
  only in embedded font data, on 70 pages in 25 files.

---
## [3.29.0] - 2026-11-02

Reading order and raised text (backlog 28 and 29, D-104). Layout extraction
follows a Tagged PDF's structure tree, otherwise reads columns of running
text one after another, and superscripts and subscripts are written with
their Unicode forms ("mc²", "H₂O", "09th"). Measured against what Adobe
Acrobat and WPS produce for the same files; the aim was to do better than
both. Plain text is a derived representation: the PDF is never changed.

### Added
- `BaselineShift` (`None`, `Raised`, `Lowered`) and, on `TextFragment`,
  `BaselineShift`, `BaselineShiftAmount` (user space, positive when raised),
  `IsActualText`, `MarkedContentId` and `WithBaselineShift`. Fragments from
  `TextExtractor.ExtractFragments` carry the shift, so an application can
  render real `<sup>`/`<sub>` without inspecting characters.
- `SuperscriptMode` (`Unicode`, `Join`, `Separate`) and a `Superscripts`
  property on `TextExtractor`, `LayoutExtractor` and `OperatorExtractor`.
- `PageLayout`, `LayoutBand`, `LayoutColumn` and `ReadingOrderSource`
  (`Rows`, `Columns`, `Tagged`); `TextExtractor.AnalyzeLayout(page)` returns
  the bands and columns (rectangles, in reading order) that layout
  extraction uses, and `TextExtractor.ExtractText(page, layout)` reads a page
  by a layout the application supplies (`PageLayout.Rows`, `PageLayout.Tagged`
  or its own bands). `LayoutExtractor.Analyze` and
  `LayoutExtractor.Extract(fragments, layout)` do the same from fragments.
- `TextExtractor.UseTaggedOrder` (default true).
- `PdfPage.StructTreeRoot`: the document's structure-tree root.
- `SearchOptions.MatchCompatibilityForms` (default true).

### Changed
- **Superscripts and subscripts** (default `SuperscriptMode.Unicode`): a
  raised ordinal ending after a digit joins it ("09th"); other raised or
  lowered text is written with the Unicode superscript or subscript
  characters when every letter and digit has an exact one ("mc²", "x² + y²",
  "10⁻³", "H₂O", "Cioniniᵃ", "earlier¹"); text with no exact form (for
  example "q", the capitals C, F, Q, S, X, Y and Z, or a word of more than
  three letters) stays a separate word as in 3.28; text from `/ActualText` is never rewritten, and text the
  PDF already maps to superscript or subscript characters is kept. Each
  form normalises (NFKC) back to its plain character; a raised hyphen-minus
  becomes the superscript minus. `SuperscriptMode.Separate` gives the 3.28
  output, `SuperscriptMode.Join` the way common viewers copy it ("mc2").
  A fragment is raised or lowered when it is at most 0.85 of its line's
  main size, off the main baseline by more than 0.1 of it, and touching the
  text next to it.
- **Layout mode follows the structure tree** of a Tagged PDF when the tree
  orders at least 80% of the page's characters; text outside the tree
  (headers, page numbers) is read as rows above or below it. Operator mode
  is unchanged.
- **Layout mode reads columns**: lines that share a vertical gutter form a
  band, a line crossing the gutter (a title, a footer, a footnote) ends it,
  and a band whose columns all look like running text is read column by
  column; when that test is unsure the band is read as rows, so tables and
  label–value forms are not split. Pages with no such band read as in 3.28.
- **Layout line grouping**: a fragment joins the nearest line within half of
  the larger of its size and the line's average size (before: the first line
  within half the line's size), so text after a superscript drawn first, and
  glyph names in font tables, land on the right line.
- **Search** (`DocumentSearch`) matches Unicode compatibility forms as their
  plain characters (NFKD, minus sign as hyphen-minus): "H2O" finds "H₂O",
  "fi" finds the "ﬁ" ligature. Match offsets and lengths stay in the page
  text. `MatchCompatibilityForms = false` restores exact matching.
- **Extracted text changes** for files with columns, tags or raised text.
  Checked on 417 PDFs (1,655 page and strategy pairs): 134 changed, in 24
  files, with no character lost or added apart from the superscript and
  subscript forms; every change is listed before and after in the release
  notes. Speed: the layout stage is faster than in 3.28 (running sums in line
  grouping); operator mode spends about 0.1 ms more per page classifying
  raised text.

---
## [3.28.0] - 2026-11-02

Text positions in user space (backlog 26), with superscripts and rotated text
read properly (D-99). Text extraction places each fragment where it is drawn
on the page, so files that scale or move text with `Tm` or `cm`, or draw it
inside form XObjects, extract with their real line breaks, word gaps and
sizes. Authoring no longer references Pdf.Documents.

### Added
- `TextFragment.Angle`: the baseline direction in degrees (0 for ordinary
  text, 90 for text running up the page), and a constructor taking it.

### Changed
- **`TextFragment` positions, widths and font sizes are in user space**
  (`ContentStreamParser`, and so `TextExtractor.ExtractFragments`). Before,
  they were in text space: the CTM was tracked but not applied, a form
  XObject's `/Matrix` and the CTM at its `Do` were ignored, and `FontSize`
  was the `Tf` size alone. Now X and Y are the glyph origin mapped through
  the text matrix and the CTM, `Width` is the advance along the baseline in
  user space, and `FontSize` is the `Tf` size scaled by the vertical scale of
  the text rendering matrix. Pages with plain unscaled text are unchanged.
- **Text-space moves are scaled with the text.** `Td`, `TD`, `T*`, `'`, `"`,
  glyph advances and `TJ` numbers moved the text matrix in its outer space
  (Tm × T); they now move it in text space (T × Tm, §9.4.2), and `TJ` numbers
  include horizontal scaling (`Tz`, §9.4.4).
- **Rotated text is read as lines of its own** in both strategies: gaps and
  lines are measured along and across each fragment's baseline, and a change
  of direction starts a new line. Layout mode places a rotated line by the
  page position of its first fragment.
- **Superscripts and subscripts stay separate tokens**: a fragment at most
  0.85 times its neighbour's size and moved off the baseline by more than 0.15
  of the larger size is separated by a space where letters or digits touch
  ("Cionini" + raised "a" reads "Cionini a"; ordinals such as a raised "th"
  read "09 th"). The operator strategy's same-line test uses the larger of
  the two sizes, so text after a superscript stays on its line.
- **Extracted text changes for such files**, in both strategies: line
  breaks, word gaps and line order. Checked on 417 PDFs (1,655 page and
  strategy pairs): 80 changed, in 20 files, with no character lost or added;
  every change is listed before and after in the release notes.
- **Chuvadi.Pdf.Authoring no longer depends on Chuvadi.Pdf.Documents.** The
  reference was added when Authoring was created and never used. Every known
  consumer that uses Pdf.Documents references it directly or through another
  package, so none relies on getting it through Authoring.

---
## [3.27.0] - 2026-11-01

Fallback warnings and follow-ups to 3.24–3.26. Fallbacks that used to be
silent are now reported to the application, the nesting refusal says where
and how deep, capital spacing is on by default again, `Shape(byte[])` is fast
again, and table-of-contents leaders are kept apart from their labels in
extracted text. Changes to defaults and output are listed under "Changed".

### Added
- **Fallback warnings** (D-62, D-63): `PdfOpenOptions.OnWarning`
  (`Action<PdfWarning>`) receives a `PdfWarning` (`Kind`, `PageNumber`,
  `Subject`, `Reason`) when an embedded font program cannot be read and a
  substitute is used (`FontSubstituted`), a damaged glyph draws nothing
  (`GlyphSkipped`), or a shading is skipped (`ShadingSkipped`, raster and
  SVG). One warning per kind and subject per page render. Also
  `PdfDocument.OnWarning`, `PdfObjectStore.OnWarning`, `PdfWarningKind`.
  Without a receiver nothing changes. See docs/EMBEDDING.md section 8.
- **Font licence texts in the packages that embed the fonts** (D-61). The
  Authoring package carries `licenses/LiPi-Sans-OFL-1.1.txt` and the PdfA
  package `licenses/Liberation-OFL-1.1.txt` (the 12 Liberation 2.1.5 faces).
  Both hold the copyright notices read from the font files and the full SIL
  Open Font License 1.1. `assets/fonts/lipi-sans/LICENSE.txt` held an
  abridged licence; its header is kept and the licence text is now complete.

### Changed
- **`ShapingFeatures.Cpsp` defaults to `true` again** (D-68), as the
  OpenType feature registry recommends; 3.26 had turned it off. Fonts with a
  `cpsp` feature (for example LiPi Sans Latin) space capitals a little wider
  by default. `new ShapingFeatures { Cpsp = false }` gives the 3.26 output.
- **Nesting refusal message** (D-82): the `PdfParseException` for values
  nested deeper than 512 levels now names the object, the depth found and
  the limit, for example "Object 3 0: A value is nested 513 levels deep; the
  limit is 512 levels. …". The limit, how levels are counted, the exception
  type and when it is thrown are unchanged.
- **Text extraction: dot leaders** (D-72). A fragment of four or more leader
  characters (`.`, `…`, `·`, `_`, `-`) that touches a word is separated from
  it by a space, in both extraction strategies: "ROOMS........ 3" (3.26) is
  "ROOMS ........ 3" again. Three dots ("Wait...") and leaders inside one
  string are left as drawn.

### Fixed
- **`TextShaper.Shape(byte[])` loaded the face and built the shaping plan on
  every call** (3.26). The face is now kept while the array is alive, so
  repeat calls cost the same as the `FontFace` overload: Latin about 37 µs
  (3.25: about 500 µs), Tamil about 21 µs (3.25: about 55 µs), Devanagari
  about 74 µs (3.25: about 68 µs) on a 2-core sandbox. A test in the gate
  holds the byte overload within twice the `FontFace` overload. Changing an
  array's bytes after shaping with it is not detected; pass a new array.

---
## [3.26.0] - 2026-10-28

HarfBuzz-conformant shaping. `TextShaper` is now a port of HarfBuzz's
OpenType shaper (the Indic shaper for the nine LiPi Indic scripts, the
default shaper for Latin runs), checked glyph for glyph against HarfBuzz
15.0.0. Also two fixes from Chuvadi.Layout: no word gap after an
`/ActualText` span in text extraction, and transparency blended in sRGB on
every rasterizer path. No public API is removed or changed; the additions are
listed below.

### HarfBuzz conformance (per script)

Corpus 1 is the 19,641-syllable comparison (single syllables: consonant,
consonant + virama, every consonant + matra, conjuncts, reph forms, signs).
Corpus 2 is 9,852 lines of words and stress strings (multi-syllable words,
ZWJ/ZWNJ, broken clusters, random character strings, and Latin text on four
fonts, one without GPOS). Fonts: the shipped LiPi Sans faces, Carlito,
Liberation Serif and the synthetic CFF fixture. "Match" means the same glyph
ids, the same clusters and the same advances and offsets as HarfBuzz, line
for line.

| Script     | Corpus 1 lines | 3.25.0 glyph ids | 3.26.0 full match | Corpus 2 lines | 3.26.0 full match |
|------------|---------------:|-----------------:|------------------:|---------------:|------------------:|
| Tamil      | 1,817 | 17.2% | 100% | 1,000 | 100% |
| Devanagari | 2,428 | 35.2% | 100% | 1,000 | 100% |
| Bengali    | 2,102 | 40.4% | 100% | 1,000 | 100% |
| Gurmukhi   | 2,039 | 80.0% | 100% | 1,000 | 100% |
| Gujarati   | 2,250 | 22.2% | 100% | 1,000 | 100% |
| Odia       | 2,158 | 13.8% | 100% | 1,000 | 100% |
| Telugu     | 2,268 | 11.4% | 100% | 1,000 | 100% |
| Kannada    | 2,264 | 11.0% | 100% | 1,000 | 100% |
| Malayalam  | 2,315 | 68.3% | 100% | 1,000 | 100% |
| Latin      | –     | –     | –    |   852 | 100% |
| **All**    | **19,641** | **33.3%** | **100%** | **9,852** | **100%** |

(3.25.0 was measured on glyph ids only; its clusters and positions were not
comparable.) `tools/shaping-data/hb_conformance.py full` regenerates both
corpora with HarfBuzz; `CHUVADI_HB_CORPUS=<file> dotnet test
tests/Chuvadi.Pdf.Text.Shaping.Tests` checks every line. A 2,162-line sample
runs in the normal test suite.

### Changed
- **`TextShaper` engine** (Text.Shaping): a C# port of HarfBuzz's OpenType
  layout engine (all GSUB lookup types including multiple, alternate and
  reverse chaining; all GPOS lookup types with mark, ligature and cursive
  attachment; lookup flags, mark filtering sets, GDEF classes; the feature
  map with stages, masks and per-syllable matching; FeatureVariations at
  the default instance), its normalizer, and its Indic shaper (syllable
  grammar, initial and final reordering, reph/pref/half/below forms, old
  and new script tags, dotted circles for broken clusters, vowel
  constraints). Fonts without GPOS get HarfBuzz's fallback mark
  positioning. HarfBuzz's operation budget bounds the work a hostile font
  can cause, reads outside a font's tables read as empty instead of
  throwing, and an Indic run in a font without a usable GSUB is still
  reordered. Notices: `THIRD-PARTY-NOTICES.md` (packed with
  Chuvadi.Pdf.Text.Shaping).
- **`ShapedGlyph.Cluster` is the UTF-16 index of the cluster's first
  character, for every script**, with HarfBuzz's cluster merging (a split
  vowel, a reordered matra and a ligature share their cluster). Indic runs
  stored a code point here before. This is what
  `DrawShapedRun(…, text, clusterStarts, …)` needs.
- **Advances and offsets are exact**: font units × 1000 / unitsPerEm, no
  longer rounded to whole thousandths per glyph.
- **`ShapingFeatures.Cpsp` defaults to false** (HarfBuzz does not apply
  capital spacing by default; it is meant for all-capital text). The other
  defaults are unchanged. Features switched off or on in `ShapingFeatures`
  are passed to the shaper as HarfBuzz user features.
- Latin runs follow HarfBuzz too: e + U+0301 composes to é when the font has
  é; ZWJ, ZWNJ and other default-ignorable characters become the space glyph
  with zero advance.
- `ShapingBench` (Shape(FontFace), short job): Latin line 173 → 32 µs,
  Tamil 19 µs, Devanagari 47 µs (the full Indic pipeline; 3.25 did much less
  work), allocation 26 → 11 KB. `Shape(bytes)` loads the face and builds
  the shaping plan on every call (about 0.3–0.4 ms; Tamil was 35 µs), so
  loops should use the `FontFace` overload.

### Fixed
- **Text extraction put a space inside a word after an `/ActualText` span**
  (Layout: Tamil "கொடு" drawn one glyph per Tj extracted as "கொ டு"). The
  content parser now advances by the font's glyph widths (`/Widths`, or `/W`
  and `/DW` of a Type0 font with the Identity-H encoding) instead of 0.6 em
  per character, gives each fragment its width, and an `/ActualText`
  fragment spans the glyphs it replaces; both extractors measure gaps from
  the real widths. Fonts without widths (standard 14) keep the old estimate.
- **Transparency blended in linear light on two rasterizer paths**: aliased
  fills (`AntiAlias = false`) and images (an image's `/ca` or alpha, and
  blend-mode/soft-mask composites) mixed 50% black over white to 187. They
  now composite in sRGB like the anti-aliased fills already did and like
  Acrobat and pdf.js: 128. Linear light is kept for anti-aliased edge
  coverage only (`RenderOptions.GammaCorrect`).

### Added
- `TextFragment.Width` and a constructor taking it (Content);
  `PdfFont.TryMeasure(bytes, out width, out codeCount, out spaceCount)`
  (Fonts).
- Tests: per-script HarfBuzz conformance, Indic behaviours (split-vowel
  clusters, dotted circle, reph, pre-base matra, ZWNJ, FeatureVariations,
  chillu), shaper robustness (self-recursive lookup, corrupted tables, no
  GSUB), extraction of shaped Tamil runs and with font widths,
  sRGB transparency on every path.
- `tools/shaping-data`: the HarfBuzz Unicode/Indic table dump and generator,
  and the conformance corpus builder.

---
## [3.25.0] - 2026-10-28

Fonts and drawing for Chuvadi.Layout: a reusable font-face measuring API, a
shaping overload that takes the loaded face, embedding of CFF-based OpenType
fonts and collection faces, searchable shaped text, and clipping, opacity,
links, dashes and image crops on `PageBuilder`. Additive only.

### Added
- **`FontFace`** (Fonts.Rendering): `FontFace.Load(bytes, faceIndex)` parses
  a TrueType, CFF-based OpenType or collection face once; thread-safe.
  `UnitsPerEm`; hhea, OS/2 typo and OS/2 win ascender/descender/line gap and
  the resolved `Ascender`/`Descender`/`LineGap` (USE_TYPO_METRICS);
  `CapHeight`, `XHeight` (measured from H and x before OS/2 v2); underline
  and strikeout position and thickness; `ItalicAngle`; names, weight, bold,
  italic, fixed pitch; `GetAdvanceWidth`, `GetLeftSideBearing`,
  `GetGlyphBounds` (`FontBox`), `GetGlyphIndex`, `CharacterMap`,
  `MeasureText`, `Scale`; `GetFaceCount` for collections.
- **`TextShaper.Shape(FontFace, text, script, features)`**: same output as the
  byte overload; the face's GSUB/GPOS feature lookups are cached with it.
  BenchmarkDotNet `ShapingBench` (`--filter *Shaping*`): Latin line 418 →
  173 µs, Tamil 35 → 7.5 µs, Devanagari 42 → 16 µs, 65–89% less allocation.
- **CFF and collection embedding.** `PdfDocumentBuilder.AddFont(name, data,
  faceIndex)`; `AddTrueTypeFont` also accepts .otf and .ttc. CFF outlines
  embed as `CIDFontType0` with `/FontFile3 /CIDFontType0C`: the CFF is
  rewritten as CID-keyed (Adobe-Identity-0) holding only the drawn glyphs,
  each with its original glyph id as CID, unused subroutines emptied, and
  Flate-compressed (a page of CJK from a 16 MB collection: ~100 KB). Public
  `FontEmbedder.Build(FontFace, …)` with an overload taking glyph text for
  ToUnicode. Checked with MuPDF and fontTools on Inter, Latin Modern and two
  Noto CJK collection faces (CID-keyed, 18 font dictionaries).
- **Searchable shaped runs.** `PageBuilder.DrawShapedRun(glyphs, text,
  clusterStarts, x, y, font, size, color)`: one-glyph clusters get ToUnicode
  entries (a ligature maps to its text); several-glyph clusters, and glyphs
  already standing for other text, are wrapped in `/Span <</ActualText>>`.
  `DrawText` with the LiPi font now does the same for shaped Indic runs
  (they extracted as glyph ids before).
- **`GlyphRunStyle`** for shaped runs: `HorizontalScale` (advances scale
  too), `Skew` (synthetic italic), `StrokeWidth` (synthetic bold, render
  mode 2).
- **Clipping and opacity scopes**: `PushClip(x, y, w, h)`, `PushClip(Path,
  FillRule)`, `PopClip()`, `PushOpacity(opacity)`, `PushOpacity(fill,
  stroke)`, `PopOpacity()` (nested opacities and an image's own opacity
  multiply). Pops must match; scopes left open are closed at save, before
  the header and footer.
- **Links over a rectangle**: `AddLink(x, y, w, h, Uri)` and `AddLink(x, y,
  w, h, targetPageIndex, targetTop)` (a `/GoTo` to `/XYZ`; the target is
  checked at save).
- **`LineStyle`** (width, dash pattern and phase, cap, join, miter limit) with
  new `DrawLine`, `DrawRectangle` and `DrawPath` overloads.
- **Image crops**: `DrawImage(bytes | frame, x, y, w, h, ImageCrop)` in source
  pixels, by clipping (lossless; the hidden pixels stay in the file).
- **Text extraction honours `/ActualText`** (inline or named property lists;
  an outer span wins; an empty one removes its text).
- `FontRenderer.GetGlyphIndexForCid`.
- Synthetic font fixtures (tests/Fixtures/Fonts, made by
  tools/make_font_fixtures.py): CFF, CID-keyed CFF with two font
  dictionaries, TrueType, and a collection of both.
- A test that every packable project's package id starts with `Chuvadi.` and
  sets no version of its own.

### Fixed
- **CFF charstrings: the shortint operand (byte 28) was read as an
  operator**, so glyphs using it failed or drew wrongly (16% of Inter, 25% of
  a Noto CJK face).
- **CID-keyed CFF fonts used the top dictionary's subroutines and widths**
  instead of each glyph's font dictionary (FDArray/FDSelect).
- **CFF glyph bounds always had xMin ≤ 0** (unused control points counted).
- **Raster: a composite font's CIDs were used as glyph ids**, ignoring a
  CIDFontType2's `/CIDToGIDMap` stream and a CID-keyed CFF's charset.
- **Raster: line widths and dash lengths ignored the CTM scale.** A page
  drawn under a scaling `cm` (Ghostscript writes `0.1 0 0 0.1 0 0 cm`; CAD
  exports do similar) drew every line many times too thick — an engineering
  floor plan was black with lines. Both now scale with the CTM (sqrt of its
  determinant). On 157 sample PDFs every page whose rendering changed moved
  closer to MuPDF's.
- **Raster: dash patterns were ignored** — dashed lines drew solid. Zero-length
  dashes with round or square caps draw dots (Office's dotted borders).
- **Raster: round line caps were drawn as butt caps, and a square cap
  shortened the start of the line** instead of extending it. Both now match
  MuPDF pixel for pixel on test lines.
- **SVG: dash lengths ignored the CTM scale**, and a rotated CTM (a = d = 0)
  gave stroke width 0. Width and dashes now scale by sqrt of the CTM's
  determinant (a non-uniform scale gives the geometric mean of the two
  scales instead of the larger one). `PathOp.Stroke` dash lengths are now in
  page space like its width; `LineSegment.Dash` stays in user space as
  documented, and `LineSegment.Width` changes only under a non-uniform CTM.
- **Raster: a closed right triangle, or four box corners in "Z" order (a
  bow-tie), used as a clip clipped to the bounding box** (taken for a
  rectangle).
- **Text extraction read inline-image data as content**: bytes such as
  `<<` or `Tj` inside `BI … ID … EI` could swallow or invent text. The data
  is now skipped. An array or dictionary missing its `]` / `>>` ends at the
  next operator instead of swallowing the rest of the stream.
- **Raster: constant opacity (`/ca`, `/CA`) and soft masks composited in
  linear light**, so 50% red over white was (255, 188, 188); now sRGB
  (255, 128, 128) like other viewers. Anti-aliased edges of opaque fills are
  still blended in linear light.
- Chained-context GSUB (format 3) checks the current glyph's coverage before
  the backtrack (no change in output; Latin shaping 2× faster).

### Changed
- No source break: the byte-array `TextShaper.Shape` overload carries
  `[OverloadResolutionPriority(1)]`, so a call with a bare `null` font still
  binds to it (C# 13 or later; .NET 10 projects use C# 14), checked by a
  compile test.
- `TrueTypeFontEmbedder` delegates to the shared embedder (same output).

### Known gaps
- Indic shaping matches HarfBuzz for about a third of syllables (Tamil 17%,
  Devanagari 35% on a 19,641-syllable comparison): the Indic features are not
  applied and reph/pre-base handling is incomplete. Planned for 3.26.

---
## [3.24.0] - 2026-10-28

Embedding API for untrusted documents (the Anjal Mail asks A1–A5): a
password-required signal, documented "viewing never runs anything"
guarantees with a sanitizer for output, cancellation, resource limits, a
no-disk guarantee and an enumerated exception set. Additive only — see
docs/EMBEDDING.md.

### Added
- **A1 — passwords.** `PdfPasswordRequiredException` (derives from
  `PdfParseException`, which open used to throw, so existing handlers still
  catch it) with `PasswordSupplied`; `PdfDocument.RequiresPassword(Stream)` /
  `(string)` and `PdfReader.RequiresPassword` check before opening.
  `PdfParseException` is no longer sealed.
- **A2 — guarantees and disarming.** docs/EMBEDDING.md states that opening,
  rendering and text extraction never run scripts, follow actions, open
  embedded files, touch the network, start processes or write to disk;
  `EmbeddingGuaranteeTests` checks the compiled viewing assemblies for any
  network, process, temp-file or file-system API. New `PdfSanitizer.Sanitize`
  / `Inspect` (Operations) with `SanitizeOptions` / `SanitizeReport`: writes
  a copy without `/OpenAction`, `/AA`, JavaScript, non-navigation actions
  (`/A` and a link's `/PA`, including chained `/Next`), embedded files (and
  the data of any embedded-file stream left reachable), XFA and multimedia
  annotations; `Inspect` reports exactly what `Sanitize` removes.
- **A3 — cancellation.** `CancellationToken` overloads of
  `PageRasterizer.Rasterize`, `RasterizeToPng`, `RenderRegion` and
  `TextExtractor.ExtractText`, `ExtractFragments`, honoured within a second
  (checked per content operator, scanline, image row, MiB of decoded output
  and JPEG MCU row). `PdfWorkScope` (Primitives) carries a token and limits
  for any other work (`using (PdfWorkScope.Enter(limits, token)) { ... }`).
- **A4 — limits.** `PdfLimits` (`MaxInputBytes`, `MaxPageCount`,
  `MaxMemoryBytes`, `MaxDecodedStreamBytes`, `MaxContentOperators`,
  `MaxOutputPixels`), passed with `PdfOpenOptions` (`PdfDocument.Open(Stream,
  PdfOpenOptions)`, `Open(string, PdfOpenOptions)`, `OpenAsync(Stream,
  PdfOpenOptions)`), kept on `PdfDocument.Limits` / `PdfObjectStore.Limits`
  and applied by every `PageRasterizer` and `TextExtractor` call;
  `PdfLimitExceededException` names the limit. Decompression bombs stop at
  the decoded-size limit while decoding; memory is charged as buffers grow;
  each glyph shown (and every 4,096 stray operands) counts toward page
  complexity; open-time work runs under the limits.
- **A5 — no disk.** Nothing is spilled to temporary files anywhere in the
  library; the only file access on the viewing path is opening the path a
  caller passes.
- **Exception set.** Through the `PdfOpenOptions` open overloads and the
  rasterizer/extractor methods, bad input yields only `PdfException` subtypes
  or `OperationCanceledException`; failures the code did not anticipate are
  reported as `PdfParseException` (open) or `PdfCorruptionException`
  (render/extract) with the original as the inner exception. Mutation
  fuzzing (8,000 damaged variants per run) found nothing outside the set.

### Fixed
- **Code that skips damaged objects no longer swallows cancellation or a
  limit** (31 broad `catch` blocks across rendering, SVG, text and the reader
  now let `OperationCanceledException` and `PdfLimitExceededException` through).
- **A damaged font program no longer fails the page.** A font whose program
  cannot be loaded falls back like a missing one, and a glyph whose outline
  data is damaged draws nothing (raster).
- **Page boxes with indirect or non-numeric entries** are read (indirect
  numbers resolved) or treated as empty instead of throwing
  `InvalidCastException`.
- **Text layout grouping and operator-order assembly** are cancellable (the
  layout grouping is quadratic on pathological pages).
- **Process-killing input.** Objects nested more than 512 levels deep are
  refused at parse (a 100 KB file of nested arrays overflowed the stack);
  self-referential stitching functions, Indexed colour spaces whose base is
  themselves and deeply nested Type 4 programs fail cleanly instead of
  overflowing the stack.
- **A `/Parent` cycle hung page access forever** (inherited-attribute
  lookup); the walk now stops at a cycle.
- **A huge or negative page-tree `/Count`** allocated a per-page array up
  front (out of memory, or `OverflowException`); pages are now cached as they
  are accessed.
- **The owner password did not open RC4 (R2/R3) and AES-128 (R4) files**; it
  now does (PDF 32000-1 Algorithm 7). R6 already worked.
- **Object streams listed as stored inside each other** (found by mutation
  fuzzing) made object loading recurse until the process died; an object
  stream claimed to be inside another is now treated as missing (§7.5.7
  forbids it).
- **Long reference chains** (an outline of tens of thousands of items
  linked by `/Next`) overflowed the stack in the reachability walks shared
  by `PdfCompressor` and `PdfSanitizer`; those walks are now iterative.
- **An impossible encryption key `/Length`** for R2–R4 threw
  `ArgumentException`; it is now a `PdfEncryptionException`.
- **Comment-only content** is cancellable (skipped comments and blank space
  are counted across lines), and cancellation is checked when a content
  stream ends.
- **Malformed shadings and functions** (a non-number in `/Coords`, `/Domain`
  or a function's number arrays, `/C0` and `/C1` of different lengths, an
  empty `/Domain`, a short `/Range`) skip the shading instead of failing the
  page; a page with an empty MediaBox reports `PdfCorruptionException` with a
  plain message.

---
## [3.23.0] - 2026-10-28

Backlog 17, 18 and 19: the remaining Scrub residuals (colour-function
shadings, soft-mask groups), the last pattern-cell redaction edges, and the
verifier's false alarm on palette images; plus a radial-shading rendering
fix found while verifying the Scrub trim.

### Fixed
- **Pattern cells drawn by resource-inheriting forms (backlog 18).** A form
  without its own `/Resources` that paints a tiling pattern across a box
  now has the pattern's cell redacted. When every invocation gives it the
  same resources, the form gets its own copy of them (equivalent for
  painting) and takes the rewritten cell and blanked images like any form;
  a partly covered image in it is now blanked instead of removed. When
  invocations differ, each pattern it paints is rewritten in place (same
  object number) against the form's contexts, so other uses of that
  pattern object also see the redacted cell (B15: over-redact).
- **Decoy "shown elsewhere" tiles (backlog 18).** A tile copy kept as proof
  that a cell is visible anyway stops counting once anything is painted
  over it later — fills, strokes (by line width and miter), text (Type 3 or
  unresolved fonts: anywhere), images, inline images, shadings, forms, a
  repaint of the same pattern under blending or soft masks, and annotations
  on the page.
- **Text state across `q`/`Q`.** Leading (`TL`), font and font size are now
  saved by `q` and restored by `Q` during redaction, and `TL` is tracked at
  all, so text placed by `T*`, `'` or `"` after a `TL` or a restored state
  is found on its real line (it could previously escape a box).
- **Verifier on palette images (backlog 19).** `RedactionVerifier` reads a
  partly blanked `/Indexed` image's raw index samples, so blanked index 0 is
  no longer reported as surviving content. New public
  `ImageOp.IndexSamples` (DisplayList) carries the indices of 8-bit indexed
  images.
- **Scrub: a fill enclosing the box** whose vertices all lie outside it was
  dropped; it is now kept and clipped.
- **Radial shadings under unequal scaling or skew (raster and SVG).** The
  builders mapped the circle centres to the page and scaled both radii by
  one factor, so a radial shading under a CTM (or pattern `/Matrix`) that
  scales x and y differently or skews painted the wrong circle at most
  pixels. Such a shading now keeps its shading-space circles plus the
  transform: the rasterizer maps each pixel back, and SVG emits a
  `gradientTransform`. New public `ShadeOp.ShadingToPage` (Raster) and
  `ShadingOp.ShadingToPage` (DisplayList); null, and output unchanged, for
  rotation, uniform scale and translation.

### Added
- **Scrub trims colour-function shadings (backlog 17).** Function-based,
  axial and radial shadings (types 1–3), as `sh` resources and in shading
  patterns, keep only the sampled-function data (Type 0, also inside
  Type 3 stitching functions) for the inputs the box can show, with a
  2-sample margin. Radial shadings are trimmed only where the painted area
  covers the whole box (otherwise kept whole); near the cone's apex the
  exact extremes are sampled.
- **Scrub scrubs soft-mask groups (backlog 17).** An ExtGState `/SMask`
  group used under a single placement keeps only what can show in the
  box; unused ExtGStates leave the resources.

### Known residuals (see BACKLOG 17)
- A soft mask set under different placements is kept whole; Type 3 glyph
  procedures are not scrubbed; Type 2/4 functions are not trimmed.

---
## [3.22.1] - 2026-10-22

Backlog 18: the content of tiling-pattern cells under a redaction box.

### Fixed
- **Pattern cells kept redacted content (backlog 18).** Redaction removed
  the painted pattern area inside a box, but a tiling pattern's cell (an
  image, text) stayed whole in the file when the pattern still painted
  elsewhere — so a large single-tile pattern kept the part under the box.
  Each tile copy that reaches the part of a box where the pattern was
  painted now rewrites the cell as if placed there: text and paths it
  would show there are removed and images are blanked pixel by pixel (as
  in 3.22.0), in a new pattern object; the original leaves the file unless
  something else uses it. Nested patterns are followed; patterns in forms
  with their own resources are handled. A cell is left whole when a tile
  copy elsewhere on the visible page provably shows the entire cell (a
  plain rectangle fill, no ExtGState, layer or text clip in effect), so
  repeating hatches are not stripped from the whole page.
- **`RedactionVerifier` looks inside tiling patterns.** Cell content
  painted inside a region (through the pattern's clip) is reported like
  any other content; pattern cells were previously skipped.

### Changed
- `RedactionTextStateTests.TilingPatternStillPaintedOutsideTheBox_IsKept`
  now uses a removed fill that shows none of the cell's text (the old
  geometry touched the text's glyph box, which 3.22.1 rightly redacts).

---
## [3.22.0] - 2026-10-19

Backlog scope A: pixel-level partial image redaction, Scrub residuals
(pattern cells, mesh shadings, crossing annotation appearances), pattern
strokes in SVG, and the API doc generator's multi-line signatures.

### Added
- **Partial image redaction (backlog 16).** A redaction box over part of an
  image now blanks only the pixels whose cells reach into the box; the rest
  of the image is kept, so redacting one word on a scanned page no longer
  removes the whole scan. Works for image XObjects and inline images (an
  inline image is re-emitted as an image XObject), any bits per component,
  any colour space, rotated or skewed placements, JPEG images (decoded and
  re-encoded losslessly with Flate), soft masks and stencil masks (blanked
  over the same region on their own pixel grids). Blanked colour samples
  are zero, blanked stencil samples paint nothing, blanked soft-mask samples
  are transparent. An image wholly inside a box, one that cannot be decoded,
  or one in a form that inherits its resources is still removed whole
  (B15). The original image leaves the file unless something else still
  draws it; the blanked copy keeps its `/OC`, `/Matte` and `/StructParent`.
- **`RedactionVerifier` understands blanked images.** An image is reported
  only when a pixel reaching into the region is not blank: all colour
  samples zero, or fully transparent over a colour that is one value across
  the whole image (a stencil). A picture hidden under a transparent soft
  mask is still reported, and so is an image whose pixel layout cannot be
  determined from its data (JPEG, or an ambiguous component count). Palette
  (`/Indexed`) images are still reported conservatively.
- **Pattern strokes in SVG (backlog 2).** A stroke whose colour is a tiling
  or shading pattern (`SCN /Name`) paints the pattern through the stroke
  outline — an SVG mask of the path stroked with its width, caps, joins,
  miter limit and dashes — instead of the previous flat colour.
  `ClipOp.Stroke` (stroke-outline clip) and `ClipOp.StrokeReach` are new in
  the display list; `RenderOpBounds.Bounds(ClipOp)` grows by the stroke's
  reach.

### Changed
- **Scrub residuals (backlog 7).** Under `PageCropMode.Scrub`:
  - A tiling pattern's cell keeps only what can show in the box: the cell
    is scrubbed against the part of the box the pattern is actually painted
    over, through the one tile copy that reaches it (or to the bounding box
    of the visible parts when several do); a cell that cannot show is
    emptied. Patterns and shadings the scrubbed content no longer uses are
    pruned from the resources.
  - Mesh shadings (types 4–7, used by `sh` or by a shading pattern) keep
    only the triangles or patches that can paint inside the box; kept
    elements are re-encoded stand-alone (shared edges spelled out), a
    lattice becomes a free-form mesh, and a mesh that cannot be parsed
    unambiguously is removed (B15).
  - A tile copy that is visible on its own is scrubbed exactly through its
    placement (rotated patterns included); spaced tiles keep only the strips
    that show.
  - Every kept annotation's appearance streams (`/AP` N, R, D and their
    states) are scrubbed to the box — an appearance can draw past its
    `/Rect`.
  - The page thumbnail (`/Thumb`) and `/PieceInfo` of a scrubbed page are
    removed.
- **Redacted pages drop `/Thumb` and `/PieceInfo`** (and rewritten forms,
  and forms, patterns and appearances rewritten by Scrub, drop
  `/PieceInfo`). A thumbnail pictures the unredacted page; `/PieceInfo`
  holds the authoring application's private data.
- **API docs: multi-line signatures (backlog 15).** `tools/gen_api_docs.py`
  joins a declaration whose parameter list spans several lines (and cuts a
  following expression body), so members such as `PageBuilder.DrawLine`,
  `DrawText` and `DrawArrow` are now documented; 126 pages regenerated.
- `Chuvadi.Pdf.Redaction` now references `Chuvadi.Pdf.Operations` (it reuses
  the sample-level image editor).

### Fixed
- **A structure-tree label kept a removed image in the file.** A tagged
  figure's object reference (`OBJR`) to an image removed by redaction or by
  Scrub kept the image's pixels in the output. An object reference no longer
  keeps a removed object; it is left pointing at nothing. Documents redacted
  or scrubbed before 3.22 whose images are tagged this way should be
  processed again.
- **Shared or inherited resources kept a removed image in the file.** An
  image listed in `/Resources` inherited from a page-tree node, or in an
  indirect resource dictionary shared with other pages or nodes, stayed in
  the output after redaction. Each rewritten page now gets its own resource
  copy, and the shared node or dictionary keeps only what the remaining
  pages draw (a page whose content cannot be decoded draws nothing). A
  dictionary also used by a form, pattern, appearance or font is left whole.
- **A resource-inheriting form kept a removed image in the file.** When a
  form without its own `/Resources` drew a redacted image, resource pruning
  read the form's original content and kept the image; the rewritten
  content is now used.
- An inline image removed inside a marked-content sequence now strips the
  sequence's `/ActualText` / `/Alt` (as an image XObject already did).
- Scrub-cropped images keep their `/OC` (a hidden-layer image stays
  hidden), `/Matte` and `/StructParent`.

---
## [3.21.0] - 2026-10-19

Acrobat-aligned behaviour for two long-standing limits (tiling patterns with
many tiles, damaged cross-reference recovery), plus the last two Reader asks:
layer visibility when rendering (PDF-14) and real-world measurement (PDF-15).

### Added
- **Layer visibility (PDF-14).** `OptionalContentVisibility` evaluates the
  document's default layer configuration (`/BaseState`, `/ON`, `/OFF`, and
  the `/AS` auto-state for viewing) and every way content joins a layer:
  `BDC /OC` sequences, and `/OC` on form XObjects, image XObjects and
  annotations, including membership dictionaries (`/P` AnyOn, AllOn, AnyOff,
  AllOff) and `/VE` visibility expressions (And, Or, Not).
  `OptionalContentVisibility.FromDocument(document, layerVisibility)`,
  `WithOverrides`, `IsLayerVisible(name)`, `IsVisible(entry)` and
  `AllVisible`. Each page exposes the document default as
  `PdfPage.OptionalContent`.
  - Rendering options: `RenderOptions.IgnoreOptionalContent` /
    `LayerVisibility` and `SvgExportOptions.IgnoreOptionalContent` /
    `LayerVisibility` (layer name → visible, for a layers panel).
  - Builders: `DisplayListBuilder.Build(document, page, OptionalContentVisibility, …)`
    for the text display list and `Build(page, objects, OptionalContentVisibility, …)`
    for the raster one.
  - Extraction: `TextExtractor.OptionalContent` and
    `ContentStreamParser.OptionalContent`.
- **Measurement (PDF-15).** `PdfPage.UserUnit` (`/UserUnit`, default 1.0),
  `PdfPage.Viewports` (`/VP`: `PdfViewport` with `Bounds`, `Name`,
  `Measure`), `PdfPage.FindViewport(x, y)` (the last viewport containing the
  point, per the specification) and `PdfPage.MeasureDistance(x1, y1, x2, y2)`.
  `PdfMeasure` (rectilinear `/RL`; `/GEO` is reported but not converted)
  provides `Distance`, `Area` and `Coordinates` in the drawing's units, with
  formatting that follows the document's number formats (`PdfNumberFormat`:
  precision, fractions, separators, unit chains such as `10 ft 6 in`).
  `PdfMeasure.FromDictionary` also reads an annotation's `/Measure`.

### Changed
- **Layers hidden in the document's default configuration are no longer
  drawn or extracted**, as in Acrobat. This affects SVG, raster,
  `PageContentInventory` / `HasDrawableContent`, text runs and search, and
  `TextExtractor`. Content in OFF layers that rendered through 3.20 now
  disappears. Set `IgnoreOptionalContent` (or pass
  `OptionalContentVisibility.AllVisible`) for the previous behaviour.
  Redaction, Scrub and `RedactionVerifier` still see every layer: hidden
  content is in the file, so it is still removed and still reported.
- **Tiling patterns needing more than 4,096 tiles are painted** instead of
  being skipped. Raster renders the cell once into a bitmap at device
  resolution and repeats it across the region. The cost depends on the output
  area, not the tile count. A period under about a pixel paints the cell's
  average tone. Repeated fills on one page are capped at 32× the output area
  so hostile files stay bounded. SVG emits a native `<pattern>`
  (`TilingPatternOp.IsRepeating`). Fills of 4,096 tiles or fewer render
  exactly as before. New raster op: `TilingFillOp`.
- **Damaged cross-reference recovery** no longer hands back the wrong object.
  When an xref offset holds a different object and no definition of the
  requested one exists, the object is treated as missing (null, PDF
  32000-1:2008 §7.3.10) instead of resolving to the unrelated object. The one
  exception: a definition with a mislabelled header is accepted when no other
  entry uses its number.
- **Every recovery is reported.** `PdfReader.Warnings`, `PdfDocument.Warnings`
  and `IsRecovered` now also cover objects repaired as they are first loaded:
  recovered by scanning, read from a mislabelled definition, or treated as
  missing. Objects load lazily, so these can appear after open. Check them
  after rendering or extracting. A stale entry repaired by scanning, which
  3.18 fixed silently, now adds a warning.

### Fixed
- The xref recovery scan accepts any PDF whitespace between the parts of an
  object header (`12  0 obj`, `12 0\nobj`, `12\r\n0 obj`), not only single
  spaces.

---
## [3.20.0] - 2026-10-14

The "redaction assurance" release: tagged-content redaction and a
verification API (PDF-03), fixes from an adversarial review of redaction, and
the remaining finishing items (form-aware downsampling, autohinter X fitting,
perspective deskew).

### Security
- **Text under the content stream's own `cm` is now redacted where it is
  drawn.** The text matcher placed glyphs with the text matrix alone (plus
  the placement of an invoking form), ignoring any `cm` in the page's own
  content. Text in a scaled, translated or flipped coordinate system — common
  in generated PDFs — was therefore not removed when the box was drawn over
  it. Glyphs are now tested in page space, and still in the old frame too so
  rectangles built from text-fragment positions keep matching (a glyph goes
  if either hits). **Documents redacted before 3.20 whose text is placed
  through `cm` should be re-redacted.**
- **Text rise (`Ts`) and horizontal scaling (`Tz`) are honoured.** Raised or
  stretched text inside a box was measured at the wrong place and could
  survive.
- **Survivors no longer slide into the redacted area.** The gap left for
  removed glyphs omitted character and word spacing (`Tc`/`Tw`), so the rest
  of the line moved left — visible text drawn over the redaction area. The
  gap now reproduces each removed glyph's full advance.
- **Tagged content: replacement text is stripped everywhere.** `/E`
  (expansion text) joins `/ActualText` and `/Alt`; property lists referenced
  by name from `/Properties` are cleaned; a Figure's `/Alt` is stripped when
  its image is removed.
- **Objects only removed content used now leave the file:** an image's
  `/SMask` and `/Mask`, and tiling patterns, shadings and graphics states the
  rewritten content no longer paints with. Anything still used elsewhere is
  kept.

### Added
- **Redaction verification (PDF-03).** `RedactionVerifier.Verify(document,
  rectangles)` reports every glyph, image, path and annotation still drawn
  inside the rectangles (the redaction overlay itself is ignored);
  `RedactionVerifier.ExtractWithin(document, page, region)` lists what any
  region paints. Built on the render display list, so forms, Type 3 glyphs
  and annotation appearances count exactly as drawn. New types
  `RedactionVerification`, `RedactionSurvivor`, `RedactionSurvivorKind`.
  `Chuvadi.Pdf.Redaction` now depends on `Chuvadi.Pdf.Rendering.DisplayList`.
- **Four-point perspective correction.** `PerspectiveCorrection.Correct`
  (in `Chuvadi.Pdf.Images`) straightens a quadrilateral marked on a photo to
  an upright rectangle (homography, premultiplied bilinear sampling), with
  `SuggestSize` for the natural output size.
- **Autohinter X-axis stem fitting** under `HintingMode.Full`: vertical stems
  of unhinted fonts get whole-pixel widths and positions (monotone mapping in
  between). `HintingMode.Light` (the default) is unchanged.

### Changed
- **Downsampling measures images inside form XObjects.** The placement
  scanner walks invoked forms (composing `/Matrix`, inheriting resources), so
  images in forms are downsampled to their real painted size instead of being
  skipped. Images it cannot measure — used by a pattern, soft mask, Type 3
  font or annotation appearance, in a form nested too deep or recursively, or
  in content that fails to decode — are still left alone.

### Fixed
- **Text rise in the text display list.** `Ts` was ignored, so superscripts
  and subscripts sat on the baseline in SVG output, text-run bounds and
  search hit boxes.
- **`/ToUnicode` CMaps compressed with a filter array** (or needing
  `/DecodeParms`) were decoded as plain Flate and lost their Unicode mapping.
- Content streams with a filter array are decoded fully by the downsampling
  scanner, and JBIG2 globals with a filter array by the Scrub image cropper.

---
## [3.19.0] - 2026-10-14

The "Reader delegation" release: library capabilities the Chuvadi Reader asked
for (PDF-04..PDF-08, PDF-11), pattern completeness, and a scrub/redaction
leak fix.

### Security
- **Scrub crop no longer leaves the original page content in the file.**
  Through 3.18.1, `PageCropMode.Scrub` rewrote the page content stream but
  kept the original content-stream objects in the output, so off-box text was
  hidden from viewers yet still recoverable from the file bytes; image and form
  XObjects crossing or outside the crop box likewise stayed in `/Resources`
  with their full content. Scrub now prunes every resource the scrubbed
  content no longer uses and omits all objects of the original page that the
  output no longer reaches (objects shared with other pages, annotations or
  the outline stay). **Documents cropped with Scrub before 3.19 should be
  treated as not redacted and re-cropped.**
- **Redaction removes images from the file, not just the page.** An image
  XObject whose `Do` was removed by a redaction box stayed in `/Resources` and
  in the output; it is now pruned from the rewritten page's (and form's)
  resources and omitted when nothing else uses it. Shared images still used
  on other pages are kept.

### Added
- **Arrows (PDF-04).** `PageBuilder.DrawArrow` (shaft plus filled head at the
  end, start or both), `Path.ArrowHead`, and the pure `ArrowGeometry`
  (`HeadPoints`, `ShaftEnd`, defaults) so on-screen previews use exactly the
  geometry the PDF gets.
- **Multi-stamp in one pass with opacity (PDF-05).** `PageStamper.PlaceMany`
  places any number of `StampRequest`s (any target pages, any sources,
  per-stamp transform, placement and `Opacity`) and writes once; translucent
  stamps get collision-free `CvStampGsN` ExtGStates.
- **Imposition parity (PDF-06).** `Imposition.PlanNUp` / `PlanBooklet`
  return the exact sheet geometry (`ImpositionSheet` / `ImpositionPlacement`)
  that `NUp` / `Booklet` write; `Imposition.FitTransform` fits any page's
  visible area into a rectangle.
- **Display rotation (PDF-07).** `SvgExportOptions.Rotation` and
  `RenderOptions.Rotation` (clockwise 0/90/180/270; the canvas or buffer takes
  the rotated size), plus opt-in `ApplyPageRotation` on both to honour the
  page's own `/Rotate`. Defaults are unchanged.
- **Content inventory (PDF-08).** `PageContentInventory` (text runs, paths,
  images, shadings; `HasDrawableContent`, `HasVisibleText`) built from the
  render display list, so forms, Type 3 glyphs, annotation appearances and
  invisible text are classified exactly as rendered; extensions
  `document.HasDrawableContent(i)` and `document.GetContentInventory(i)`.
- **Header/footer preview (PDF-11).** `HeaderFooter.ResolveSegments`
  returns each segment exactly as `Apply` draws it (token-substituted text,
  x, baseline, width) and `HeaderFooter.ResolveText` resolves one template;
  `Apply` now draws from the same computation, so preview equals output.
- **Tiling patterns in SVG.** PatternType 1 fills render in the SVG pipeline
  (cell defined once in `<defs>`, one `<use>` per tile, clipped to the
  path); uncoloured patterns paint in the `scn` colour. New
  `TilingPatternOp` in the text display list.
- **Pattern strokes in raster.** A pattern set as the stroke colour (`SCN`)
  now paints the stroke area (tiling and shading patterns), via a new
  stroke-outline `ClipPath` kind.

### Fixed
- **Imposition ignored the crop box and `/Rotate`.** Cells were sized from
  the MediaBox while the placed form used the CropBox (and its origin
  offset), and rotated pages were placed sideways; placement now fits the
  visible, upright page exactly like a viewer shows it. Invalid geometry
  (margins or gutters that leave no cell, negative spacing, an empty source)
  is rejected with `ArgumentException` instead of producing blank sheets.
- **Scrub crop of images crossing the crop box** is now byte-level for both
  inline images and image XObjects: the in-box pixel sub-rectangle is copied
  at the sample level (any filter — including CCITT and JBIG2 scans — any
  bits per component, colour space and `/Decode` preserved, `/SMask` and
  `/Mask` cropped alongside); rotated or skewed placements blank every pixel
  whose cell lies outside the box. An image that cannot be decoded for
  cropping is removed (over-redaction in doubt), where 3.18 kept it whole.
- **Scrub crop of form XObjects** recurses into the form in its own space
  instead of keeping it verbatim; scrubbed forms get pruned resources.
- **Scrub crop of pages with inherited `/Resources`** keeps resolving the
  inherited fonts and XObjects.
- Shading patterns used inside form XObjects map through the form's space.
- Supersampled rasterization now honours `AntiAlias` and `GammaCorrect`.

---
## [3.18.1] - 2026-10-14

A hotfix: FlateDecode failed on real-world PDFs whose streams carry bytes
after the zlib data (PDF-12, Chuvadi Reader).

### Fixed
- **FlateDecode trailing-byte tolerance.** The decoder located the Adler-32
  trailer at the last 4 bytes of the stream buffer, so a stream with trailing
  bytes after the zlib data (an EOL before `endstream`, producer padding, or
  a truncated trailer) misread the checksum and threw
  `FilterException: Adler-32 checksum mismatch` — an Elsevier journal rendered
  page 1 only, with every later page failing on a trailing `0x0A`. The DEFLATE
  payload now ends where the final block ends, and the Adler-32 trailer is
  advisory: a wrong, truncated, or junk-followed trailer after a structurally
  sound stream no longer fails the decode, matching Acrobat, pdf.js, PDFium,
  and mutool. Structural corruption (invalid block types, broken Huffman
  codes, out-of-range back-references, truncated payloads) still throws.

### Changed
- A FlateDecode stream that inflates cleanly but carries a corrupt Adler-32
  trailer now decodes successfully instead of throwing. The checksum was the
  only detector for corruption that leaves the DEFLATE structure valid; that
  detection is traded for opening the files every mainstream viewer opens.

---
## [3.18.0] - 2026-10-14

A backlog-drain release: one large PR closing the verified-open items of the
post-3.17 roadmap.

### Added
- **Precise vector redaction** (`VectorRedactionMode`, default
  `SubtractPrecise`): a painted path intersecting a redaction region loses only
  the geometry whose ink can touch the region — untouched subpaths survive
  verbatim, straight stroked segments split at the region boundary (expanded
  by the stroke ink allowance: half line width × miter limit, with `w`/`M`/
  ExtGState `/LW`/`/ML` tracked), and axis-aligned rectangle fills decompose
  into the surviving sub-rectangles. Everything doubtful (curves crossing a
  region, fill-rule interactions, non-invertible matrices, unresolvable stroke
  state) still drops whole, and removal stays byte-level (B15).
  `DropIntersecting` preserves the pre-3.18 whole-path drop.
- **SVG density handling** for CAD-scale vector pages: consecutive same-style
  paths batch into one `<path>` element when paint-order exact (12,036 → 567
  elements, −31 % bytes on the reference LINAC room plan);
  `SvgExportOptions.PathSimplifyTolerance` (Ramer–Douglas–Peucker on straight
  runs), `MaxVectorOps` budget, and `SvgDensityFallback.Simplify`/`Rasterize`
  (whole-page `<image>` at `RasterFallbackDpi`).
- **`CompressToTarget` rasterize fallback** (opt-in,
  `CompressToTargetOptions.AllowRasterizeFallback`): when the JPEG-quality
  search hits the vector floor, pages rasterize to a descending-DPI image-only
  PDF until the target is met; reported via
  `CompressToTargetResult.UsedRasterizeFallback`/`RasterizeDpiUsed`.
- **Dynamic XFA auto-compose**: `XfaRenderer.OpenComposed(stream)` returns the
  composed static document for `/NeedsRendering` files and the original
  document for everything else (hybrid and static XFA included), falling back
  to the original when no usable template exists.
- **ImageMask stencils render** (`/ImageMask true`): composited with the
  current fill colour in BOTH the raster and SVG pipelines, honouring
  `/Decode [1 0]`.
- **Pattern fills**: tiling patterns (PatternType 1, coloured and uncoloured)
  replay their cell across the filled region and shading patterns
  (PatternType 2) fill with their gradient in the raster pipeline; shading
  patterns also fill in the SVG pipeline (clip + gradient). The content-stream
  walker gained `IContentOperatorSink.SetPatternColor` (a default interface
  method — existing sinks are unaffected).
- **Compression image recoding**: `CompressionOptions.DownsampleImagesToDpi`
  (box-resample to a target DPI, driven by each image's largest painted
  placement scanned from page content) and
  `CompressionOptions.SmartCodecSelection` (per-image smallest-of: indexed
  palette under Flate at 1/2/4/8 bpc, JPEG, or the original bytes).
- **CMYK image completeness**: decoded CMYK frames (separated TIFFs) embed as
  raw DeviceCMYK under FlateDecode (the K channel is no longer mistaken for
  alpha), and the raster sink converts 8-bpc DeviceCMYK/ICC-4 samples for
  display (the SVG sink already did).
- **Composite autohinting in unhinted fonts**: composite glyphs flatten and
  Y-fit through the geometric autohinter exactly like simple glyphs, so
  accented letters grid-fit consistently with their bases.
- **Xref-offset recovery**: a classic xref entry whose offset is provably
  wrong (a different object number at the offset, or unparseable bytes) now
  recovers by scanning the file for the object's real definition — newest
  definition wins. Healthy files keep the strict fast path; the earlier
  narrow page-tree recovery is subsumed.
- **Perf regression baseline**: `dotnet run --project benchmarks/Chuvadi.Benchmarks
  -- --perf-report` times a deterministic micro-suite (open, rasterize, SVG,
  compress on the synthetic corpus) against a committed baseline
  (`--update-perf-baseline` refreshes it); a non-required, advisory CI job
  runs it on every push.

### Fixed
- **Scrub-mode crop gaps**: annotations entirely outside the crop box are
  removed from `/Annots` and their objects (and `/AP` appearance streams)
  omitted from the output; inline images (`BI`/`ID`/`EI`) are consumed as a
  unit (no more token-stream corruption) and dropped when fully off-box; `sh`
  under a fully off-box clip is dropped as dead bytes.
- **`PageCropper` no longer drops lazily-unloaded objects**: the crop pipeline
  preloads the reachable object graph (B16) before re-emitting, so
  annotations, fonts and outlines the crop never touched survive in both
  `ClipOnly` and `Scrub` output (previously any object not already resolved
  was silently omitted, and new object numbers could collide).
- **`Chuvadi.Pdf` meta-package** now lists the newer modules directly
  (Xfa, PdfA, Text.Shaping, Rendering.Raster, Rendering.Walking).
- **`tools/gen_api_docs.py` filename collisions**: two types sharing a short
  name within one module (the DisplayList/Raster `DisplayListBuilder` and
  `PageDisplayList` pairs) no longer overwrite each other's page — duplicates
  are suffixed with their namespace tail.

### Hardened (independent adversarial review of this release)
An independent review agent probed the release's changes and its findings were
fixed and swept library-wide:
- **Operand stack semantics**: the redaction and scrub interpreters now model
  the LAST n operands of every operator (matrix, text-state, path, line-width),
  matching viewer stack semantics, so crafted extra operands can no longer
  shift the modelled geometry away from what a viewer paints; the precise
  vector subtractor additionally refuses (whole-path drop) any construction
  operator with surplus operands rather than re-emitting them.
- **Buffered paths survive interleaved state operators**: a state operator
  between path construction and paint (illegal but rendered by viewers) no
  longer flushes the buffered path verbatim past the region check.
- **SVG fill batching** refuses to merge fills whose geometry overlaps the
  batch (opposite-winding overlaps would cancel to holes under nonzero).
- **Soft-mask streams are exempt from palette recoding** (must stay
  DeviceGray per §11.6.5.2).
- **Inline images end exactly**: the data length of unfiltered inline images
  is computed from /W /H /BPC /CS, so a byte pair that merely looks like "EI"
  inside the data cannot end the image early (scrub leak / stream corruption);
  with no terminating EI the remainder is kept verbatim instead of dropped.
- **Xref recovery ignores decoys**: scan hits inside stream…endstream extents
  are excluded and endobj-terminated definitions are preferred.
- **Shared appearance streams survive scrub-crop** (only AP objects no kept
  annotation references are omitted); **self-referential patterns/forms**
  are depth-capped instead of overflowing the stack; **downsampling excludes
  images also referenced from form XObjects**; fill-piece emission inflates
  the subtracted region past coordinate-rounding error; a sample-size guard
  overflow and a stream leak in `XfaRenderer.OpenComposed` were fixed.

### Verified (no code change needed)
- The SVG sink's `cs`/`CS`/`sc`/`scn`/`SCN` colour conversion, form-XObject
  recursion, and quote-operator composite-font routing all shipped with the
  rendering consolidation — the backlog entries were stale.
- The four COI dataset nodes without `XfaDataField.Geometry` have no
  corresponding template field; all laid-out fields carry geometry.

---
## [3.17.1] - 2026-07-09

### Fixed
- **Redaction rectangles no longer capture neighbouring lines they never
  touched.** The hit test inflated every glyph's box by 0.25 em + 1.5 pt below
  its baseline, so a rectangle drawn around one word — with its top edge in
  the blank gap under the line above — "intersected" that line's phantom
  descent and silently deleted words the user never covered (redacting a body
  word ate part of the section heading above it). Hit testing now uses tight
  per-glyph ink bounds: descender glyphs (g j p q y, low punctuation,
  brackets) keep a deep bottom, x-height-only lowercase a shorter top, and
  everything else spans baseline-guard to ascender height. Removal of
  anything genuinely hit is unchanged (still over-redacting, B15): a
  rectangle covering only a descender tail still removes those glyphs, and
  the generous box is still used for the drawn overlay. A rectangle placed
  entirely in inter-line whitespace now removes nothing.

### Tests
- 4 new tests (2,417 total): gap-box leaves the line above intact,
  baseline-crossing box still redacts it, descender-tail-only box still hits,
  pure-whitespace box removes nothing.

---
## [3.17.0] - 2026-07-08

Hybrid XFA documents "just work": a consumer opens a PDF through the ordinary
render / extract / flatten APIs and gets the finished document — no
XFA-awareness required. Driven by the Government-of-India MCA Certificate of
Incorporation class of files (hybrid XFA, signed, incrementally updated).

### Added
- **Annotation appearance rendering (PDF 32000-1:2008 §12.5.5).** All render
  sinks — raster, SVG, and the text display list — now draw each visible
  annotation's normal appearance (`/AP /N`, honouring the `/AS` state
  selector) placed onto its `/Rect` by the §12.5.5 algorithm. Form-field
  widgets, stamps, and ink annotations paint the way an interactive viewer
  paints them; hybrid XFA/AcroForm files show their filled field values.
  New public API in `Chuvadi.Pdf.Documents`: `PageAnnotationAppearances.Collect`
  and `AnnotationAppearance` — the single shared resolver consumed by
  rendering, extraction, and flattening, so an annotation paints, extracts,
  and flattens at exactly the same place.
- **Text extraction includes annotation appearance text.** `TextExtractor`
  (`ExtractText` and `ExtractFragments`) appends fragments from each visible
  appearance stream, transformed into page space, so field values are
  searchable and copyable. On the reference certificate, extraction grows from
  the bare template labels to the full document including company name, CIN,
  PAN, TAN, and signatory.
- **`XfaDataField.Geometry` populated via the template bind map.** The XFA
  template's `<field name="…"><bind ref="$record.…"/>` pairs are parsed and
  correlated with the AcroForm widget rects, so dataset nodes whose names
  differ from their widget names (COMPANY_NAME vs CompanyName[0]) still
  resolve a page index and rectangle — enabling search-hit highlighting and
  per-field redaction.
- **`XrefTable.ContainsAny`** — presence check for an entry of any kind
  (in-use, compressed, or free), used by the chain merge below.
- **`PdfString.DecodeLiteralToken` / `PdfString.DecodeHexToken`** — the single
  authoritative §7.3.4 string-token decoders (full escape set: octal `\ddd`,
  `\b`, `\f`, line continuations, unescaped end-of-line normalization).

### Fixed
- **Cross-reference chain precedence (§7.5.6).** The entry for an object in
  the most recent incremental-update section now supersedes all earlier ones
  regardless of kind: a free entry in a newer section marks a deleted object
  and shadows older definitions (no resurrection), and a compressed (Type 2)
  entry is no longer replaced by an older uncompressed one. In a hybrid
  section, `/XRefStm` entries merge before the classic table's, per §7.5.8.4.
  Previously, signed and incrementally-filled files (e.g. government
  certificates) resolved their pre-fill page tree — widgets without `/V` or
  `/AP` — so values were invisible on every path.
- **Literal-string escape decoding unified.** The content-stream parser
  passed string bytes through unescaped (octal `\050` surfaced verbatim in
  extracted text), and the object parser lacked octal/`\b`/`\f`/continuation
  handling. Both now delegate to `PdfString`, as does the content-stream
  walker.
- **Layout extraction no longer double-spaces.** A positional word gap after a
  fragment that already ends in a space (Adobe appearance generators end each
  word-run with one) inserted a second separator, breaking exact-substring
  search over extracted text.
- **Standard 14 font metrics are now exact.** `Standard14Widths` previously
  returned a single per-font *average* for every non-space character (a
  documented v2.0.0 stopgap), so unembedded Times/Helvetica text — the norm in
  LiveCycle/government documents — rendered with jumbled word spacing, lines
  overflowing the page edge, and a distorted type impression. The class now
  carries the complete Adobe Core 14 AFM tables (codegenned from the canonical
  URW base-35 AFM files, WinAnsi-indexed via the Adobe Glyph List);
  `Standard14GlyphWidths` delegates to the same data, so the raster,
  display-list, and redaction width paths can never disagree. Rendering the
  reference certificate now matches a poppler reference at 97.8% binary-ink
  agreement with every text line's extents within ±2 px at 110 dpi.
- **`AnnotationFlattener` content loss (BASELINE B16).** The flattener
  iterated the lazy object store without a preload, silently dropping every
  not-yet-resolved object — a 536 KB certificate flattened to a 6.7 KB empty
  page. The worker now force-resolves the object graph first.
- **Flatten produces a truly static PDF.** Widgets selected for flattening
  that have no usable normal appearance are dropped (industry-standard
  semantics) instead of kept live, so the AcroForm — and the `/XFA` entry
  inside it — is removed: flattening a hybrid XFA document yields
  `IsXfa == false` with all values baked into the page content.

### Tests
- 21 new tests (2,413 total): xref precedence (update supersedes, free
  shadows), appearance collection and §12.5.5 placement, geometry binding,
  extraction (values, octal escapes, spacing), SVG and raster appearance
  output, flatten content preservation and XFA removal, and an end-to-end
  suite over a sanitised MCA certificate fixture.
- New fixtures: `hybrid_xfa_widget.pdf` (minimal hybrid XFA with a widget
  appearance), `incremental_object_update.pdf` and
  `incremental_free_shadowing.pdf` (hand-built xref chains).

---
## [3.16.0] - 2026-07-07

### Added
- **XFA form scripting — FormCalc and JavaScript engines.** Completes the XFA
  renderer arc: embedded form scripts now execute during rendering, so real
  LiveCycle forms fill their computed fields instead of rendering blank. New
  public types in `Chuvadi.Pdf.Xfa` (`Scripting` namespace): `XfaScriptHost`
  (SOM reference resolution and property get/set), `XfaScriptValue` (a dynamic
  value type shared by both engines), `XfaJavaScriptEngine` (a from-scratch
  lexer, parser, and tree-walking interpreter for the JavaScript subset XFA
  forms use), `XfaFormCalcEngine` (a full FormCalc interpreter: `&` concat,
  `if/then/endif`, `for/upto/endfor`, `while/endwhile`, and the common builtins
  Concat/Left/Right/Len/Substr/Upper/Lower/Sum/Avg/Min/Max/Round/Abs/At/Replace/
  Stuff/Space), `XfaScriptRunner` (fires initialize/calculate/validate across
  the template tree, failing soft per script), `XfaScript`, `XfaScriptException`,
  and `XfaValidationResult`. Interactive events (click/change/etc.) are parsed
  and attached but dormant — a static render has no event source. Unsupported
  script constructs fail soft, leaving form state untouched, so one bad script
  never aborts a render. (#171)

### Changed
- **`XfaRenderOptions.ScriptMode` now defaults to `Full`.** Rendering an XFA
  document via `XfaRenderOptions.Default` runs its initialize, calculate, and
  validate scripts, so scripted fields fill without the caller opting in. Pass
  `new XfaRenderOptions { ScriptMode = XfaScriptMode.None }` to restore the
  previous no-script behaviour. This is a behavioural change to the default, not
  an API break — no signatures changed. (#172)
- **Rendering split into three single-namespace projects.** The former
  `Chuvadi.Pdf.Rendering.DisplayList` project housed three namespaces; it is now
  three projects, one namespace each, with acyclic layering
  Walking → DisplayList → Raster: `Chuvadi.Pdf.Rendering.Walking` (the shared
  content-stream walker, a leaf depending only on Filters/Objects/Primitives and
  exposing its internals to the two builders via `InternalsVisibleTo`),
  `Chuvadi.Pdf.Rendering.DisplayList` (the text/search display list), and
  `Chuvadi.Pdf.Rendering.Raster` (the raster display list for the scanline
  rasterizer). Behaviour is unchanged; consumers of the raster or walker types
  reference the new packages, which are pulled in transitively by the meta
  package and `Chuvadi.Pdf.Rendering`. (#174)

### Fixed
- **Compressed objects in hybrid-reference PDFs now resolve
  (`HasStructTree`/`IsTagged` work on Word/Office output).** A hybrid-reference
  file (PDF 32000-1:2008 §7.5.8.4) has a classic `xref` table whose trailer also
  carries `/XRefStm`, pointing to a cross-reference stream that lists the
  compressed (Type 2) objects the classic table cannot represent — for many
  Office writers, that includes `/StructTreeRoot` and `/MarkInfo`.
  `PdfReader.LoadXrefChain` followed `/Prev` but ignored `/XRefStm`, so those
  objects were invisible and `HasStructTree`, `IsTagged`, and `StructTreeRoot`
  wrongly reported absent/null. The classic-xref branch now also reads
  `/XRefStm`, merging its entries without overriding the classic ones (classic
  wins, per the spec). (#173)

---
## [3.14.0] - 2026-06-19

### Added
- Glyph-level text redaction: a redaction overlapping part of a Tj run now
  removes only the matched glyphs and keeps neighbours in their exact
  positions, instead of dropping the whole operator. (#118)
- In-place redaction replacement: `RedactionRect.ReplacementText` draws a
  replacement in the removed span (no box over it); a replacement wider than
  the span is rejected with `RedactionException`. (#118)
- `Standard14GlyphWidths`: accurate per-glyph Standard-14 widths promoted to a
  public type in `Chuvadi.Pdf.Fonts.Rendering`. (#118)
- Checksum-validated redaction patterns: `PatternRule.Validator` plus
  `PatternValidators` (Luhn, Verhoeff, IBAN mod-97, ABA routing, NPI), new
  `CommonPatterns` (India PAN/Aadhaar, IBAN, ABA, SWIFT, EIN, ITIN, NPI, IPv4),
  a `LabeledValue` matcher, and ready-to-use `PatternSets`
  (Financial/Medical/GeneralPii). (#119)
- Redaction R1: physical removal of in-region annotations and their link URLs,
  form-field values, and vector graphics. (#117)
- PageStamper lifecycle: scan-and-continue stamp naming (fixes silent data loss
  on re-stamp), plus `RemoveStamp` and `ReplaceStamp`. (#116)
- Rendering Phase 2: PDF function and shading evaluators (axial/radial, the `sh`
  operator) in the SVG sink, and form XObject rendering. New public
  `PdfFunction`, `PdfShading`, `ShadingOp`, `ShadingStop`. (#115)

### Fixed
- SVG/raster rendering: un-premultiply image SMask `/Matte`. (#115)

---
## [3.9.0] - 2026-06-15

### Fixed
- **Watermarking preserved document integrity.** `WatermarkStamper` (text and
  image) now force-loads the full object graph before numbering, so freshly
  opened documents no longer collide watermark object numbers with existing
  objects, and catalog-only objects (metadata, outlines, names, attachments,
  struct tree) are carried into the output instead of being dropped. The trailer
  `/Info` dictionary is also preserved, so Title/Author/Subject/Keywords and
  dates survive a watermark pass.

### Added
- **Compression safety guard.** `PdfCompressor.Compress` now skips digitally
  signed and encrypted documents by default rather than silently invalidating a
  signature or emitting decrypted content. It writes nothing and reports
  `CompressionResult.SkipReason` (`CompressionSkipReason.Signed` / `Encrypted`),
  so a batch run over a mixed corpus continues instead of throwing. Opt in per
  hazard with `CompressionOptions.AllowSignedRewrite` /
  `AllowEncryptedRewrite`.
- **Compression benchmark and ratio/quality baseline.** A new
  `Chuvadi.Benchmarks.Compression` support library provides a deterministic
  synthetic corpus, ratio measurement, and a global-SSIM quality metric for the
  lossy image path. A committed `compression-baseline.json` plus a CI gate test
  (`CompressionRatioBaselineTests`) fail the build on any ratio or quality
  regression. The BenchmarkDotNet suite gains a `CompressionBench` timing
  scenario, and the runner gains `--compression-report` and
  `--update-compression-baseline` for inspecting and regenerating the baseline.

## [3.8.0] - 2026-06-15

### Added
- **Page composition.** High-level operations for placing existing pages under
  arbitrary affine transforms, all keeping vector and text content intact and
  selectable:
  - `PageComposer` — build a new document by placing pages onto target sheets
    (standard or custom size): rotate by any angle, resize between paper sizes,
    and N-up / imposition (`AddPage`, `AddPageMatching`, `PlacePage`, `Write`).
  - `PageStamper` — overlay or underlay a source page onto one, several, or all
    pages of an existing document (`Place`, `PlaceOnAll`), preserving the rest.
  - `Placement` — convenience transforms (`ScaleToFit`, `Center`,
    `RotatedSize`, `RotateIntoBox`, `RotateAboutCenter`).
  - `PdfPage.EffectiveSize` — the page's displayed size, accounting for
    `/Rotate`.
- **Text extraction follows form XObjects.** `TextExtractor` now recurses into
  form XObjects invoked with `Do`, so text on composed or stamped pages is
  extractable — along with any other form-XObject text previously missed.
- **`SvgExportOptions.Background`** (`ColorF?`, default white). SVG export now
  emits an opaque full-page background rectangle, matching the rasteriser's
  default paper; set to null for a transparent SVG.

### Fixed
- Page import forces a full object-graph load before numbering, fixing a latent
  lazy-loading bug where newly added object numbers could collide with the
  catalog (or drop unloaded objects) on large documents.
- **TIFF output now decodes in Windows/WIC viewers.** PackBits is packed per
  scanline across multiple ~8 KB strips, with explicit Orientation and
  PlanarConfiguration tags. Whole-image single-strip packing previously shifted
  rows and left a black band in Photos, Photo Viewer, WPS, and Paint, even
  though libtiff-family decoders tolerated it.
- **SVG pages render correctly aligned in every engine.** The root width/height
  now carry an explicit `pt` unit; emitting them unitless let some viewers treat
  the point-sized values as points and rescale the canvas by 96/72 while leaving
  the content at 1 unit = 1px, pushing the page off-centre.

## [3.7.0] - 2026-06-15

### Fixed
- **Cross-reference entries are now exactly 20 bytes (ISO 32000-1 §7.5.4).**
  Each `xref` entry was written with a stray space before its CRLF
  (`nnnnnnnnnn ggggg n \r\n`), making it 21 bytes. Lenient readers (qpdf,
  pikepdf, and Chuvadi's own reader) scan past the misalignment, but Adobe
  Acrobat rejects the table, rebuilds it on open, and marks the file modified —
  so it prompts "save changes?" on close even after the file was only viewed.
  The space is removed; entries are now 20 bytes. Affects every written PDF
  (merge, extract, split, authoring, watermarking).

### Added
- **Document metadata on every written PDF.** Output now carries an `/Info`
  dictionary (`/Producer`, `/Creator`) and an XMP `/Metadata` stream on the
  catalog. Both are deterministic — a fixed producer plus identifiers derived
  from the file id, with no timestamps — so identical input still yields
  byte-identical output, and any caller-supplied `/Info` or `/Metadata` is
  preserved.

## [3.6.0] - 2026-06-14

### Added
- **`PdfDocument.IsXfa`.** True when the document is an XFA form (its
  `/AcroForm` carries an `/XFA` entry). XFA content lives outside standard page
  content, so such documents render essentially blank; consumers can use this
  flag to show a notice instead of reaching into the catalog themselves.

### Fixed
- **SVG rendering now applies image `/SMask` (soft-mask transparency).** An image
  XObject with an `/SMask` was embedded as opaque colour with the mask dropped,
  so transparent regions — whose colour bytes are conventionally black —
  rendered as a solid black box (affecting any transparent logo, watermark, or
  signature image). The renderer now decodes the soft mask and embeds the image
  as an RGBA PNG with the mask applied as alpha (honouring the mask's `/Decode`).

## [3.5.1] - 2026-06-14

### Fixed
- **Merge no longer corrupts pages when inputs reuse object numbers.**
  `PageOperations.Merge` renumbered referenced objects through a table keyed on
  the bare object number. Object numbers are per-document, so two inputs that
  reused the same number for different objects (common with shared
  letterhead/forms) collided: distinct content streams collapsed onto one, and
  pages from the second document came out blank, doubled, or failed with "stream
  ended unexpectedly". The remap is now keyed by `(source document, object
  number)`, so each input keeps its own objects while shared objects within a
  single document still de-duplicate.
- **Every written PDF now carries a trailer `/ID`.** `PdfWriter.Write` emitted
  `/ID` only on the encryption path, so Merge, ExtractPages, and Split produced
  files without one; some viewers (e.g. Adobe) synthesised an ID on open and then
  prompted to save on close even after only viewing. The writer now derives a
  stable, content-based `/ID` (ISO 32000-1 §14.4) for every file; output stays
  deterministic and any caller-supplied `/ID` is preserved.

## [3.5.0] - 2026-06-14

### Added
- **Per-run font style on extracted text (copy-with-format).** `TextRun` now
  carries `FontFamily`, `FontWeight` (CSS-style numeric), `Slant`
  (normal/italic/oblique), and `FontSize`, so callers can reconstruct formatted
  text. A new shared `FontStyleClassifier` derives these from the base font name
  combined with the FontDescriptor `/Flags`, `/ItalicAngle`, and `/StemV`; the
  resolved `FontStyle` is carried on `TextOp` and surfaced on `TextRun`.

### Changed
- **SVG text styling now descriptor-aware.** `SvgRenderer` resolves bold/italic
  through the shared `FontStyleClassifier` (`TextOp.Style`) instead of a
  name-only substring check, so fonts that signal style only through their
  descriptor are now rendered with the correct weight/slant.

## [3.4.0] - 2026-06-14

### Added
- **Glyph subsetting for embedded fonts.** `AddTrueTypeFont` now embeds only the
  glyphs actually drawn (plus their composite components), via the new
  `TrueTypeSubsetter`. Non-rendering tables (`GSUB`/`GPOS`/`GDEF`/`cmap`/`post`)
  are dropped, since a CIDFontType2 with an Identity CID-to-GID map never
  consults them and the layout tables are large in complex-script fonts. Glyph
  numbering is preserved, so the Identity mapping, per-CID widths, and ToUnicode
  are unchanged. A two-font Tamil + Devanagari page dropped from ~309 KB to
  ~7 KB (~98% smaller); CFF/OpenType-CFF fonts are embedded whole.

## [3.3.0] - 2026-06-14

### Added
- **Inline-image redaction.** The redactor now removes inline images
  (`BI … ID <binary> EI`) whose CTM-mapped unit square intersects a redaction
  rectangle, matching the existing behaviour for `Do` image/form XObjects.

### Fixed
- **Inline-image parsing in redaction.** Inline-image binary data was previously
  fed to the content tokenizer as if it were operators, which could corrupt the
  rewrite of any content stream containing an inline image. The redactor now
  consumes `BI … EI` as a single unit and resumes parsing after `EI`.

### Changed
- **Backlog reconciled against the code.** `docs/BACKLOG.md` was rewritten: items
  wrongly marked "Not started" but in fact shipped (pattern redaction, non-text
  image redaction, optional content, linearization) were moved to Shipped, and
  the duplicate N.5–N.8 numbering was replaced with a single 1–13 open-roadmap
  scheme.

## [3.2.0] - 2026-06-14

### Added
- **Custom TrueType font embedding in authoring.** `PdfDocumentBuilder.AddTrueTypeFont(name, ttfBytes)`
  registers a static TrueType (glyf) font; `PageBuilder.DrawText` then draws text
  in it. The font is embedded as a composite Type0 / CIDFontType2 font with
  Identity-H encoding, a `/W` width array and `/ToUnicode` CMap covering the
  glyphs actually used, and a FontDescriptor whose metrics are read from the
  font's sfnt tables. A font used on multiple pages is embedded once and shared;
  a registered-but-unused font is not embedded. This enables authoring of
  non-Latin scripts (e.g. Tamil, Devanagari) and any custom Latin font.

### Notes
- Text is emitted in logical order **without** complex-script shaping (no
  GSUB/GPOS or reordering), so Latin renders correctly and Indic renders
  correctly for isolated or already-ordered glyphs; conjunct/matra shaping is a
  separate future effort. Variable fonts must be instantiated to a static
  instance first (see `docs/custom-fonts.md`). Glyph subsetting (embedding only
  used glyphs) is a planned follow-up; this version embeds the whole font program.

## [3.1.0] - 2026-06-14

### Added
- **Streaming page enumeration.** `PdfPageCollection.EnumerateStreaming()` walks
  the page tree once and yields pages without retaining them, for
  constant-memory traversal of very large documents (the indexer stays lazy and
  cached for random access).
- **Optional content (layer) toggling.** `OptionalContentWriter.SetVisibility`
  writes a copy of a document with named layers shown or hidden, complementing
  the existing `OptionalContentReader`.
- **Parallel redaction.** Opt-in `RedactionOptions.MaxDegreeOfParallelism` runs
  the per-page redaction interpreter and overlay generation in parallel. Loading
  and final assembly stay sequential, so the output is byte-for-byte identical
  to the sequential path; the default (1) is unchanged single-threaded behaviour.
- **Rasterizer benchmark.** A BenchmarkDotNet rasterize hot-path scenario
  (150/300 DPI) added to `Chuvadi.Benchmarks`.

### Fixed
- Developer guide: corrected the signatures section — signature *creation*,
  timestamping, and LTV are supported (`PdfCounterSigner`,
  `PdfDocumentTimestamper`, `PdfLtvUpdater`), not read-only as previously stated.

## [3.0.0] - 2026-06-14

### Added
- **One-call render facade.** `PdfDocument` extension methods in
  `Chuvadi.Pdf.Reader` render a page to any format in a single call:
  `RenderPageToSvg`, `RenderPageToPng`, `RenderPageToJpeg`, `RenderPageToBmp`,
  `RenderPageToTiff` (each with `byte[]` and `Stream` forms and a DPI parameter),
  plus `RenderToTiff()` for an all-pages multi-page TIFF. Open a document, call
  one method, get the result — no manual pipeline assembly.

### Removed
- **BREAKING: `SvgExporter` removed.** The obsolete content-stream SVG exporter
  (and its internal-only helpers `TextDispatcher`, `ImageDispatcher`,
  `SvgGraphicsState`) has been deleted. It produced incorrect output —
  vertically flipped images and overlapping text — and was superseded by
  `SvgRenderer`, which renders the neutral `PageDisplayList` correctly. Migrate
  to `document.RenderPageToSvg(pageIndex)` (simplest) or
  `new SvgRenderer(options).RenderPage(document, pageIndex)`.

### Notes
This release was driven by feedback from the first application built on the
library: the easy-to-find call was the deprecated, broken one. The fix makes the
obvious path the correct path and removes the broken path entirely.

## [2.8.4] - 2026-06-13

### Added
- **Progressive JPEG (SOF2) decoding.** Embedded `DCTDecode` images encoded as
  progressive JPEG previously failed to decode and rendered blank (e.g. a
  scanned/exported masthead). The decoder now handles progressive scans —
  spectral selection and successive approximation (DC and AC, first and
  refinement passes) — accumulating coefficients across all scans before the
  inverse DCT.
- **4-component JPEG (CMYK and YCCK).** Handles the Adobe APP14 colour
  transform and the Adobe inverted-channel convention, converting to RGB for
  display. Grayscale (1-component) and YCbCr/RGB (3-component) baseline and
  progressive are all supported, with chroma subsampling and restart intervals.

### Fixed
- The decoder was rebuilt around a coefficient-buffer architecture shared by
  baseline and progressive paths; baseline output is unchanged (verified
  against the existing image tests).

### Notes
Verified against a reference decoder across baseline/progressive, 4:4:4/4:2:0,
grayscale, and CMYK fixtures (max per-channel difference within IDCT rounding).
Not supported: 12-bit precision, arithmetic coding, and lossless JPEG.

---

## [2.8.3] - 2026-06-13

### Fixed
- **Embedded subset fonts now render the correct glyphs.** Documents whose
  fonts are embedded and subsetted (e.g. produced by LibreOffice/eLORA) showed
  blank or garbled text. Two causes: the TrueType loader only parsed `(3,1)`
  format-4 cmaps and ignored the `(1,0)` format-0 tables these fonts use; and
  the rasterizer selected glyphs by decoded Unicode rather than by the
  content-stream character code. The raster text path now selects glyphs by
  code (symbol/Mac/Unicode cmaps as the encoding requires, with a subset
  code-as-index fallback), keeping the Unicode path for standard fonts.
- **Embedded Type1 (`FontFile`) fonts now render.** Added a Type1 program
  interpreter (eexec/charstring decryption, the Type1 charstring operators
  including seac and flex, built-in and `/Differences` encodings). Previously
  these glyphs did not render at all.
- **Simple-font advances now come from the font's `/Widths` array** (the
  authoritative source per PDF §9.2.4) when present, falling back to the font
  program's metrics — fixing letter spacing for both TrueType and Type1.

### Added
- TrueType cmap formats 0 and 6, and separate retention of Unicode, symbol,
  and Macintosh cmap subtables.
- Word spacing is applied to single-byte code 32 per §9.3.3.

### Known gap
Embedded JPEG (`DCTDecode`) image XObjects are not yet decoded for display, so
documents whose graphics are baseline-JPEG (e.g. a scanned masthead) show those
images blank. Text renders fully. A JPEG decoder is planned separately.

---

## [2.8.2] - 2026-06-13

### Fixed
- **Non-embedded Standard-14 fonts now render text.** Pages using the
  standard Helvetica/Times/Courier/Symbol/ZapfDingbats fonts without an
  embedded font program previously rasterised with no text (only vector
  graphics appeared). The rasterizer's font resolver returned nothing when a
  font had no embedded program, so glyph emission was skipped. It now falls
  back to the embedded substitute-outline bundle for the 14 standard fonts.

### Changed
- **The Standard-14 outline bundle is now built from real font data.**
  `Standard14.bin` was previously a header-only placeholder. It is now
  generated from the Liberation (SIL OFL) and URW (AGPL-with-font-exception)
  substitute fonts: all 14 fonts, ASCII plus Latin-1, ~191 glyphs each.
  Glyph outlines are normalised to a 1000-unit em on load.
- **The bundle build tool** (`tools/build_standard14_bundle.py`) now converts
  quadratic TrueType curves to cubic via fontTools' Qu2CuPen (the loader
  renders single-segment cubics; raw multi-point `qCurveTo` was unsupported),
  resolves symbol-encoded fonts that lack a Unicode cmap, and covers
  0x20–0xFF.

### Note
Glyph shapes are metric-compatible substitutes (Liberation/URW), not Adobe's
original outlines — standard practice for headless PDF rendering.

---

## [2.8.1] - 2026-06-13

### Fixed
- **Page operations no longer corrupt the source document.** `SplitPages`,
  `Merge`, `ExtractPages`, `DeletePages`, `RotatePages`, and `ReorderPages`
  shared the source document's nested resource dictionaries (e.g.
  `/Resources`) with their output and remapped references in place. This
  mutated the original document — so a second operation on the same
  `PdfDocument` (for example compressing a document that had just been
  split) saw scrambled references and dropped fonts and images — and it
  scrambled references for multi-page documents whose pages share resource
  objects. The page builder now performs a true deep copy and never mutates
  shared state.
- **References nested inside arrays are now remapped.** The array branch of
  the old reference-remapper was a no-op, so indirect references inside
  arrays (such as `/Annots` or an array `/Contents`) kept their original
  object numbers after a page operation. They are now renumbered correctly.

---

## [2.8.0] - 2026-06-12

### Added
- **JPEG encoder** (`Chuvadi.Pdf.Images.JpegEncoder`): baseline JFIF export
  with IJG quality 1–100, completing PDF→image support across PNG, BMP,
  TIFF, and JPEG. Colour encodes as YCbCr 4:4:4; grayscale frames encode
  single-component.
- **CCITTFaxDecode filter** (`Chuvadi.Pdf.Filters.CcittFaxFilter`): Group 3
  one- and two-dimensional and Group 4 fax decoding — scanned-document PDFs
  now render. Honours K, Columns, Rows, BlackIs1, EncodedByteAlign, and
  EndOfBlock; registered with the `CCF` alias.
- **Raw raster images.** The rasterizer now renders raw-sample images
  (FlateDecode RGB/Gray, CCITT bilevel, ICCBased 1/3-component), honouring
  per-filter `/DecodeParms` and `/Decode [1 0]` inversion. These were
  previously dropped silently.
- **PDF compression** (`Chuvadi.Pdf.Operations.PdfCompressor`):
  garbage-collects unreachable objects, Flate-compresses raw streams, and
  optionally re-encodes photographic images as JPEG
  (`CompressionOptions.RecompressImages`). The catalog graph (outlines,
  forms, metadata) is preserved.

### Changed
- **Real DEFLATE compression.** The Flate encoder previously emitted stored
  (uncompressed) blocks; it now performs LZ77 with fixed-Huffman coding.
  PNG exports, authored documents, and compressed streams shrink
  dramatically (typical content streams to 10–20% of raw size).

### Removed
- **`Chuvadi.Pdf.Images.Jpeg` project.** An orphaned duplicate JPEG encoder
  with no consumers or tests; superseded by
  `Chuvadi.Pdf.Images.JpegEncoder`.

---

## [2.7.1] - 2026-06-12

### Fixed
- **Octal string escapes on the raster path.** The rasterizer's content-stream
  string decoder did not handle `\nnn` octal escapes (PDF 32000-1 §7.3.4.2),
  so literal strings written with octal-escaped bytes — including the WinAnsi
  bullets and ellipses Chuvadi's own ReportBuilder emits — rendered the escape
  characters verbatim when rasterized. Octal escapes now decode correctly.
- **Multi-element dash patterns in SVG output.** The SVG display-list parser
  kept only the first dash length and treated later array entries as the
  phase, so patterns like `[3 2] 0 d` rendered with the wrong gap lengths.
  The full dash array is now honoured.

### Changed
- **DisplayList consolidation (internal).** The two content-stream
  interpreters behind the SVG/Reader display list and the raster display list
  now share one walker (tokenisation, operand parsing, operator dispatch) with
  the two builders as event sinks. No public API changed. Malformed numeric
  operands on the SVG path are now read as 0 instead of aborting the page
  build, matching the raster path's tolerance.

---

## [2.7.0] - 2026-06-12

### Added
- **Image → PDF conversion.** New `ImagePdfConverter` turns JPEG, PNG, TIFF,
  and BMP images into PDF documents in one call: page sized to the image at a
  chosen DPI or fitted to a paper size with margins and centring, multi-image
  → multi-page, multi-frame TIFF → one page per frame (optional), and document
  metadata. New `BmpDecoder` (headers 12/40–124; depths 1/4/8/16/24/32;
  BI_RGB, RLE8, RLE4, BI_BITFIELDS; top-down and bottom-up) completes the
  decoder set.
- **Image embedding rework.** `PageBuilder.DrawImage` now accepts JPEG, PNG,
  TIFF, and BMP (plus a decoded `ImageFrame` overload). Alpha channels are
  preserved via PDF soft masks (`/SMask`); grayscale sources embed as
  DeviceGray. Baseline JPEG and 8-bit truecolour PNG still embed without
  recompression; palette / grayscale / alpha / 16-bit PNG, TIFF, and BMP
  decode and re-embed as Flate-compressed samples. Fixes the previous
  RGBA-PNG embedding (4-component data declared as DeviceRGB).
- **Report layout.** New `ReportBuilder`: flowing multi-page composition with
  headings, paragraphs (alignment incl. justification, indents, spacing),
  bulleted and numbered lists (Arabic / Roman / letter numbering), span-aware
  tables (fixed / fractional / auto column widths, col- and row-spans,
  per-cell style overrides, alternating row fills, wrap / truncate / ellipsis
  overflow, images in cells, repeating headers across pages, five border
  modes), images, rules, spacers, page breaks, styled headers/footers with
  `{page}` / `{total}` / `{title}` / `{date}` tokens, and page numbers in
  Arabic, Roman, or letter form.
- **Geometric autohinter fallback.** Fonts that carry no hinting programs are
  now grid-fitted on the Y axis by a geometric autohinter (blue-zone
  anchoring with classic overshoot suppression, horizontal-stroke weight
  fitting, untouched-point interpolation). On by default; opt out with
  `RenderOptions.AutohintUnhintedFonts = false` (plumbed through
  `DisplayListBuilder` and `FontRenderer.GetHintedGlyphOutline`).

### Fixed
- **SSW interprets FUnits.** The Set Single Width instruction now converts
  its argument from font units to pixels via the current scale (matching
  WCVTF and the FreeType v35 reference); previously the raw value was stored.
- **MIRP twilight originals.** When zp1 is the twilight zone, MIRP now seeds
  the point's original (and current) position from rp0 plus the CVT distance
  along the freedom vector — the undocumented MS-rasterizer behaviour the
  conformance reference implements.
- **MIRP cut-in zone gating.** The control-value cut-in test now applies only
  when both zone pointers reference the same zone, per the reference.

### Changed
- **Engine compensation plumbed.** ROUND/NROUND and MDRP/MIRP now key engine
  compensation off the opcode's distance-type bits, applying it in both the
  rounded and unrounded (Round_None) branches. All four compensation values
  default to zero — the conformance reference's behaviour — and are settable
  via `HintingInterpreter.SetEngineCompensation`.
- **Single-width per-instruction forms.** MDRP and MIRP now use their own
  single-width snap forms (window around the single-width value for MDRP;
  CVT-distance proximity for MIRP), replacing the previous shared
  approximation.
- **MPS point-size option.** MPS keeps pushing the ppem by default (FreeType
  v35 classic behaviour); embedders can supply the spec-true point size via
  `HintingInterpreter.MeasuredPointSize`.

---

## [2.6.0] - 2026-06-10

### Added
- **Composite glyph hinting (Light and Full).** Composite glyphs - accented
  letters and other glyphs assembled from components - are now grid-fitted
  instead of falling back to the unhinted outline. Each component is hinted as
  its own glyph, translated by its component offset (rounded to the grid when
  the component sets `ROUND_XY_TO_GRID`), and merged into one point set; the
  composite's four phantom points are appended, and the composite's own
  instruction stream (when `WE_HAVE_INSTRUCTIONS` is set) is then executed
  over the assembled points. Light keeps its unfitted X and grid-fitted Y for
  composites exactly as for simple glyphs.
- **Composite hinting test coverage.** A synthetic in-memory font with a base
  glyph and a composite that references it (grid-rounded offset plus an
  instruction stream) exercises the hinted assembly end to end, including a
  regression lock on the composite-program org-from-current baseline and the
  scaled-component fallback.
### Fixed
- **SHC / SHZ no longer move the reference point a second time.** The shift
  applied by `SHC` (shift contour) and `SHZ` (shift zone) now skips the
  reference point itself, which has already moved. Previously, when the
  reference point's displacement was non-zero, it received that displacement
  twice. The defect was masked for simple glyphs (their programs begin with
  current equal to original positions) and surfaced only with composite
  instruction streams.

### Notes
- **Composite hinting scope.** Components placed by XY offset - the common case,
  including every composite in the standard text fonts tested - are fully
  hinted. Composites that use scaled components, a 2x2 transform, or
  anchor-point (point-matching) placement fall back to the unhinted outline, as
  does nesting deeper than three levels. This matches or exceeds the coverage of
  the existing unhinted composite path, so no glyph renders worse than before.

---

## [2.5.1] - 2026-06-09

### Fixed
- **Full-mode hinting now uses the hinted advance width.** In
  `HintingMode.Full`, the pen advance is taken from the hinted horizontal
  phantom points (pp2 - pp1) rather than the scaled static `hmtx` advance.
  The glyph program grid-fits the advance phantom, so the hinted advance can
  differ from the merely scaled value; using it keeps the advance consistent
  with the grid-fitted ink and removes the extra right-side gap that appeared
  after each glyph. `Light` (the default) and the unhinted path are unchanged -
  they continue to use the scaled `hmtx` advance, since `Light` does not
  grid-fit the horizontal axis

## [2.5.0] - 2026-06-09

### Added
- **TrueType bytecode hinting is now wired into the raster render path
  (Stage 7), and on by default.** The completed interpreter
  (`Chuvadi.Pdf.Fonts.Rendering.Hinting`) is now executed during
  rasterization for embedded TrueType glyphs, replacing the inert flag of
  earlier releases
  - New `RenderOptions.Hinting` mode enum `HintingMode { Off, Light, Full }`.
    **`Light` is the new default** - it grid-fits the vertical (Y) axis only,
    keeping baselines and stem heights crisp without the horizontal stem
    snapping that reads heavy under grayscale anti-aliasing
  - `Full` executes the complete interpreter on both axes (best for
    black-and-white or very low-resolution output); `Off` restores the previous
    unhinted behaviour
  - Glyph hinting runs per device-ppem with a fractional 26.6 outline that the
    painter scales back exactly, so the grid fit lands on real device pixels
  - The example renderer accepts `--hint-light` (default) and `--hint-full`

### Fixed
- **MSIRP (opcodes 0x3A/0x3B) was mislabelled as RTDG and silently dropped its
  two stack operands.** This was the decision deferred in 2.4.4 (A24): the
  working decode table mapped `0x3A` to RTDG, so every MSIRP a glyph program
  issued left two values stranded on the operand stack, corrupting all
  downstream reference points and producing visibly broken glyphs (the capital
  W in the test CV lost half its strokes). MSIRP is now implemented (move a
  point to a stack-supplied distance from rp0, setting rp1/rp2 and, for the
  [1] form, rp0), and **RTDG is moved to its correct opcode 0x3D**
- **`fpgm` now runs before `prep`** when preparing a size, so functions the
  control-value program calls are defined when it executes
- **IP (interpolate points) handles out-of-range points by shifting**, not
  proportional scaling: points whose original position lies outside the
  [rp1, rp2] reference span are shifted by the nearer reference's movement, per
  the TrueType specification, fixing points that previously collapsed inward
- **Glyph outlines are re-cubicized in fractional 26.6**, not rounded to whole
  pixels first, so small glyphs keep their shape instead of degenerating
- Implemented the FLIP opcodes FLIPPT/FLIPRGON/FLIPRGOFF (0x80-0x82) and the
  vector opcodes SPVFS/SFVFS/GPV/GFV/SFVTPV (0x0A-0x0E), which were previously
  silent no-ops that drifted the stack

### Changed
- Default render output now differs from 2.4.x for documents with embedded
  TrueType fonts: glyphs are hinted with `HintingMode.Light`. Pass
  `RenderOptions { Hinting = HintingMode.Off }` to restore the previous output

### Tests
- Corrected the RTDG rounding test to use the proper opcode (0x3D); added an
  MSIRP movement test; the full suite passes (no glyph renders worse than the
  unhinted baseline at the gate)

### Notes
- **Known limitation - `Full` mode over-tightens parallel-stem letters.** On
  letters with two vertical stems (n, u, m, h), `Full` mode pulls the stems too
  close horizontally, narrowing the counter. This is an X-axis
  minimum-distance/stem-width refinement tracked for a follow-up; **`Light`
  (the default) is unaffected** and renders these correctly
- Composite (component) glyphs still fall back to unhinted outlines
- Carries forward the documented MDRP/MIRP simplifications (distance-type
  compensation zero - correct for grayscale, single-width no-op at the default
  cut-in, MPS approximated as ppem) revisited alongside the `Full`-mode stem
  work
- Architecture and rationale: decision log A25

## [2.4.4] - 2026-06-05

### Added
- **TrueType bytecode hinting — Stages 5 and 6 (arithmetic, logic, flow
  control, DELTA, and interpolation)** in
  `Chuvadi.Pdf.Fonts.Rendering.Hinting`. Builds on Stage 4 and fills in the bulk
  of the interpreter's opcode surface; still internal and unwired, so render
  output is unchanged and `RenderOptions.Hinting` remains inert
  - Arithmetic, logical, and stack operators: ADD/SUB/DIV/MUL/ABS/NEG/FLOOR/
    CEILING/MAX/MIN/ROLL, AND/OR/NOT/EQ/NEQ/GT/GTEQ/LT/LTEQ/ODD/EVEN, and
    ROUND[ab]/NROUND[ab]; DIV and MUL operate in 26.6 fixed point
  - Storage-area access (RS/WS)
  - Flow control handled in the execution loop with depth-aware scanning:
    IF/ELSE/EIF and the jumps JMPR/JROT/JROF
  - DELTA exceptions DELTAP1/2/3 (point table bases 0/16/32) and DELTAC1/2/3
    (CVT), decoding each pair's relative-ppem nibble and magnitude selector and
    applying only when the active ppem matches `DeltaBase + tableBase + relppem`
  - Shift and interpolation family, loop-aware via the graphics-state loop
    counter: SHP/SHC/SHZ/SHPIX, IP, ISECT, and full IUP[x]/IUP[y]
    (per-contour interpolation between touched anchors, with single-anchor
    rigid shift), plus ALIGNRP/ALIGNPTS/UTP
  - GETINFO answers conservatively — scaler version and the grayscale-rasterizer
    flag only

### Tests
- 25 tests over synthetic programs (no font file required): each arithmetic,
  logical, and rounding operator; ROLL and storage; the flow-control branches
  and jumps; DELTA at matching and non-matching ppem; and the geometry family
  (SHPIX/IP/IUP interpolate-and-shift/ALIGNRP/ALIGNPTS/UTP/SHC/ISECT/GETINFO),
  with expected values hand-verified in fixed point

### Notes
- Still inert: nothing in the render path calls the interpreter and the
  `Hinting` flag has no effect
- Operand orders that the spec underspecifies are pinned by tests and flagged
  for confirmation against real fonts at Stage 7: DELTA pairs (point/CVT index
  deeper, argument on top), ISECT (b1, b0, a1, a0, point), and JROT/JROF
  (condition then offset)
- Carries forward the Stage 4 simplifications (MDRP/MIRP distance-type
  compensation zero, single-width no-op at default cut-in, MPS as ppem) and
  GETINFO's conservative answer, all revisited at Stage 7
- MSIRP is deferred: its opcode assignment collides with RTDG in the working
  decode table and is resolved alongside the Stage 7 wiring
- Architecture and rationale: decision log A24

## [2.4.3] - 2026-06-04

### Added
- **TrueType bytecode hinting — Stage 4 (points, zones, and movement)** in
  `Chuvadi.Pdf.Fonts.Rendering.Hinting`. Builds on Stage 3; the interpreter is
  internal and unwired, so render output is unchanged and `RenderOptions.Hinting`
  remains inert
  - `PrepareSize(ppem, unitsPerEm, cvt, prep)` computes the 16.16 font-unit to
    26.6 scale, scales the Control Value Table, allocates the twilight zone,
    resets the graphics state, and runs the `prep` program once per size;
    `HintGlyph(RawGlyph)` builds the glyph zone scaled to 26.6, runs the glyph
    program, and returns the fitted zone
  - New internal `Zone` type holding current and original 26.6 coordinates and
    per-axis touch flags for the twilight and glyph zones
  - `F26Dot6.MulFix` — 16.16 scale multiply used to scale coordinates and CVT
    entries
  - Opcodes: measurement (GC/SCFS/MD/MPPEM/MPS), absolute movement (MDAP/MIAP),
    relative movement (MDRP/MIRP with auto-flip, control-value cut-in, rounding,
    and minimum-distance clamp), vector-to-line (SPVTL/SFVTL/SDPVTL), CVT access
    (RCVT/WCVTP/WCVTF), and the supporting state setters
    (SRP/SZP/SLOOP/SMD/SCVTCI/SSWCI/SSW/FLIPON/FLIPOFF/SDB/SDS/SCANCTRL/SCANTYPE/INSTCTRL)

### Tests
- 14 tests over synthetic programs (no font file required): font-unit and CVT
  scaling, CVT read/write, measurement, the four move operators, vector-to-line
  setters, and persistence of `prep` control-value writes into the glyph program

### Notes
- Still inert: nothing in the render path calls the interpreter and the
  `Hinting` flag has no effect
- MDRP/MIRP distance-type compensation is treated as zero (grey rendering),
  single-width handling is a no-op at the default cut-in, and MPS approximates
  point size as ppem; these are revisited at Stage 7
- Interpolation (ISECT/SHP/SHC/SHZ/SHPIX/IP/IUP), DELTA, and the
  arithmetic/logical/storage/flow-control tail remain for Stages 5 and 6
- Architecture and staging rationale: decision log A23

## [2.4.2] - 2026-06-04

### Added
- **TrueType bytecode hinting — Stage 3 (vectors and rounding)** in
  `Chuvadi.Pdf.Fonts.Rendering.Hinting`. Builds on Stage 2; the interpreter is
  internal and unwired, so render output is unchanged and `RenderOptions.Hinting`
  remains inert
  - New internal `F26Dot6` (64 = 1px) with FromPixels/ToPixels/Floor/Ceiling/
    Round/Mul/Div, and `F2Dot14` (16384 = 1.0) with Mul/Dot/ToDouble; multiply
    and divide round half away from zero and guard divide-by-zero
  - Axis-aligned vector setters SVTCA/SPVTCA/SFVTCA, and the round-state
    operators RTG/RTHG/RTDG/RDTG/RUTG/ROFF and SROUND/S45ROUND with spec
    selector decoding
  - `round()` engine via floor-to-multiple, correct for any period including
    S45ROUND's non-power-of-two grid; consumed by MDRP/MIRP in Stage 4
  - `GraphicsState` gains the super-round period/phase/threshold fields,
    defaulted to round-to-grid in `Reset()`

### Tests
- 23 tests over synthetic bytecode: the fixed-point helpers, the round engine
  under every state, SROUND/S45ROUND selector decoding, and the vector setters

### Notes
- Still inert: `round()` and the new opcodes have no render-path consumer and
  the `Hinting` flag has no effect
- The line-based vector setters (SPVTL/SFVTL/SDPVTL) and running `prep` are
  deferred to Stage 4 with the point/CVT infrastructure they consume
- Architecture and rationale: decision log A22

---

## [2.4.1] - 2026-06-04

### Added
- **TrueType bytecode hinting — Stage 2 (VM skeleton)** in
  `Chuvadi.Pdf.Fonts.Rendering.Hinting`. Builds on the Stage 1 foundation; the
  interpreter is internal and unwired, so render output is unchanged and
  `RenderOptions.Hinting` remains inert
  - New internal `HintingInterpreter`: operand stack, storage area, function
    table, and instruction-definition table. Implements the push family
    (NPUSHB/NPUSHW/PUSHB/PUSHW), stack manipulation
    (DUP/POP/CLEAR/SWAP/DEPTH/CINDEX/MINDEX), and function/instruction
    definition and calling (FDEF/ENDF/CALL/LOOPCALL/IDEF), plus
    `RunFontProgram`. The FDEF/IDEF body scanner is instruction-length-aware,
    so inline push data is never misread as `ENDF`; a call-depth guard bounds
    recursion. Opcodes not yet implemented are length-aware no-ops
  - New internal `GraphicsState` carrying the full TrueType graphics state and
    its spec defaults (`Reset()`); most fields are consumed by later stages
  - New internal `HintingLimits` and `RoundState`
  - `TrueTypeLoader.GetHintingLimits()` reads the `maxp` version 1.0 maximums
    that size the interpreter's tables, defaulting on `maxp` version 0.5

### Tests
- 19 tests over synthetic bytecode (no font file required): push family,
  stack manipulation, FDEF/CALL/LOOPCALL/IDEF, length-aware FDEF body
  scanning, the recursion-depth guard, table sizing from limits, and the
  loader's version 0.5 default limits

### Notes
- Still inert: nothing in the render path calls the interpreter and the
  `Hinting` flag has no effect
- Stack values are raw 32-bit integers; F26Dot6/F2Dot14 fixed-point
  interpretation begins in Stage 3 with the vector and rounding operators
- Introduces the repository's first `InternalsVisibleTo` (a standalone
  attribute file) so the internal interpreter can be unit-tested
- Architecture and staging rationale: decision log A21

---

## [2.4.0] - 2026-06-04

### Added
- **TrueType bytecode hinting — Stage 1 (raw-glyph foundation)** in
  `Chuvadi.Pdf.Fonts.Rendering`. Additive groundwork for a spec-complete
  TrueType instruction interpreter; no interpreter yet, and render output is
  unchanged
  - `TrueTypeLoader.ParseOffsetTable` now captures the `cvt `, `fpgm`, and
    `prep` table offsets and lengths, with internal accessors
    `GetControlValueTable`, `GetFontProgram`, and `GetControlValueProgram`
    returning the raw (unparsed) table bytes on demand
  - New internal `BuildRawGlyph(int)` parse path producing an un-cubicized
    point set in font design units — on/off-curve flags, contour ends, the
    glyph's captured instruction bytecode, and four appended phantom points —
    alongside the existing cubic `GetGlyphOutline` path, which stays the
    rendering path. Composite glyphs return null (deferred to a later stage);
    empty glyphs return phantom points only
  - New internal `RawGlyph` model in the new
    `Chuvadi.Pdf.Fonts.Rendering.Hinting` sub-namespace
  - New `RenderOptions.Hinting` flag (default `false`), currently unconsumed;
    it gates the hinted pipeline once later stages wire it in

### Notes
- Purely additive and inert: nothing in the render path calls the new
  members, the 9-page reference CV renders identically, and `Hinting` has no
  effect yet
- No new tests this stage — the foundation is exercised once the VM skeleton
  (Stage 2) calls into it
- Stage-1 simplifications: vertical phantom points (pp3/pp4) are synthesised
  pending `vmtx`/`vhea` parsing; touched-flag arrays for IUP live on the
  interpreter working set rather than the parsed model
- Architecture and staging rationale: decision log A20

---

## [2.1.0] - 2026-05-22

### Added
- **`Chuvadi.Pdf.Reader` module** — a high-level facade over the
  library, designed for interactive PDF reader applications (Blazor
  WebAssembly, WPF, etc.) that want a small, mockable surface area
  instead of wiring the lower-level modules (Documents, Rendering,
  Svg, Text) directly
  - `IPdfReader` interface with six async methods: `OpenAsync`
    (stream + optional password), `RenderPageSvgAsync` (page-sized
    SVG with selectable text layer at native PDF coordinates),
    `RenderThumbnailAsync` (lower-precision SVG for thumbnail strips),
    `GetOutlinesAsync` (bookmark tree), `SearchAsync` (streaming
    `IAsyncEnumerable<SearchMatch>` across pages), and
    `GetTextRunsAsync` (per-page text-run geometry for selection
    layers)
  - `ChuvadiPdfReader` concrete implementation backed by the
    underlying library — `PdfDocument.OpenAsync`,
    `SvgRenderer.RenderPage`, `OutlineReader.GetOutlines`,
    `PdfDocument.SearchAsync`, `PdfDocument.GetTextRuns`. All public
    method parameters are validated with `ArgumentNullException` and
    `ArgumentOutOfRangeException`. Stateless and thread-safe; suitable
    for singleton registration in DI
  - Caches two `SvgRenderer` instances internally — one tuned for
    full-page rendering (`Precision = 4`, web-font embedding,
    selectable text), one for thumbnails (`Precision = 2`, CSS
    fallback fonts, selectable text). Visual sizing is the caller's
    responsibility per design: SVG is resolution-independent and
    browsers scale it losslessly via CSS

### Tests
- New `Chuvadi.Pdf.Reader.Tests` project with tests for every
  `IPdfReader` method covering: null-argument validation,
  out-of-range page indices, cancellation propagation, plain and
  encrypted-PDF open flows, render-output sanity (well-formed SVG),
  outline traversal on a document with no outline, search on a
  document with no text, and text-run extraction on an empty page

### Notes
- The module re-uses existing library types directly: `PdfDocument`,
  `OutlineItem` (in `Chuvadi.Pdf.Forms`), `SearchMatch`/`SearchOptions`/
  `TextRun` (in `Chuvadi.Pdf.Rendering.DisplayList`). Reader-app
  consumers add the corresponding `using` directives rather than
  going through a re-export layer — the types live where they
  logically belong, the facade just exposes them through a single
  small interface
- No breaking changes. Existing API surface is unchanged

---

## [2.0.2] - 2026-05-22

### Fixed
- **`EncryptionOptions` default permission mask** — `EncryptionOptions`
  constructed via the `Aes128` or `Aes256` factories was defaulting
  `Permissions` to `-3904` under the mistaken belief that the value
  meant "allow everything (PDF spec all-bits-on)". In fact `-3904`
  (`0xFFFFF0C0`) has **every** PDF 32000-1 §7.6.3.2 Table 22 permission
  bit CLEAR — print, modify, copy, annotate, fill-forms, accessibility
  extract, assemble, and high-quality print are all denied. The
  correct "allow everything" value is `-4` (`0xFFFFFFFC`): all eight
  permission bits set, both reserved-must-be-1 bits set, and all high
  reserved bits set. Encrypted PDFs written with default
  `EncryptionOptions` in v1.4.0 through v2.0.1 are maximally
  restricted regardless of intent
- Introduced `public const int EncryptionOptions.AllPermissionsAllowed = -4;`
  as a self-documenting reference value for the canonical "allow
  everything" mask, replacing the magic-number literal in the default
  constructor and the misleading doc comment. Callers who want to
  restrict specific permissions can continue to assign directly to
  the `Permissions` init property

### Tests
- New `EncryptionDefaultsRoundTripTests` under
  `tests/Chuvadi.Pdf.Documents.Tests` covering the AES-128 and AES-256
  write → read round-trip with default options, verifying that the
  read-back `PdfDocument.Encryption.Permissions` equals
  `EncryptionOptions.AllPermissionsAllowed` and that every `Allow*`
  decoder reports true. A guard test pins the constant to `-4` so any
  future regression to `-3904` is the loudest possible alarm
- Existing `EncryptionInfoTests.Constructor_PropertiesPropagate`
  updated to use `-4` as its sample permission value for consistency
  with the new constant; behaviour and assertions otherwise unchanged

### Notes
- The reader-side default for an absent `/P` entry in
  `EncryptionDictionary.Parse` (also `-3904`) is **intentionally not
  changed**. PDF spec requires `/P` to be present on encrypted
  documents; if a malformed document omits it, defaulting to all-bits-
  clear (deny by default) is the safer behaviour than auto-granting
  every permission
- The bug only affected the **default** permission value. Callers who
  passed an explicit `Permissions` value via the init property
  (e.g. `EncryptionOptions.Aes256("pw") with { Permissions = ... }`)
  were unaffected

---

## [2.0.1] - 2026-05-22

### Added
- **Document metadata properties** on `PdfDocument` for the date and
  trapping fields declared by PDF 32000-1 §14.3.3 — `CreationDate` and
  `ModDate` returning `DateTimeOffset?` parsed per §7.9.4 (UTC, offset,
  and date-only forms all supported; malformed inputs return null);
  `Trapped` returning `string?` and leniently accepting both `/Name`
  and `/String` forms (some producers write either); `XmpMetadata`
  returning the raw bytes of the Catalog's `/Metadata` stream
- **Encryption introspection** on `PdfDocument` via the new
  `Encryption` property returning `EncryptionInfo?` — null when the
  document is unencrypted, otherwise exposing `Algorithm`, `KeyLength`,
  `Revision`, `Version`, `Permissions`, `EncryptMetadata`, and eight
  permission decoders (`AllowPrint`, `AllowModify`, `AllowCopy`,
  `AllowAnnotate`, `AllowFillForms`, `AllowAccessibilityExtract`,
  `AllowAssemble`, `AllowPrintHighQuality`) per Table 22
- **Shape annotations — read and write** (PDF 32000-1 §12.5.6.7–9)
  - Five new public sealed classes derived from `PdfAnnotation`:
    `SquareAnnotation`, `CircleAnnotation`, `LineAnnotation`,
    `PolygonAnnotation`, `PolyLineAnnotation`
  - New shared `BorderStyle` class plus `BorderStyleType` enum
    (`Solid`/`Dashed`/`Beveled`/`Inset`/`Underline`) for the PDF
    `/BS` border-style dictionary
  - New `LineEnding` enum with all ten spec values
    (`None`/`Square`/`Circle`/`Diamond`/`OpenArrow`/`ClosedArrow`/
    `Butt`/`ROpenArrow`/`RClosedArrow`/`Slash`) for `/LE` on
    `LineAnnotation` and `PolyLineAnnotation`
  - `AnnotationType` enum extended with `Square`, `Circle`, `Line`,
    `Polygon`, `PolyLine`
  - `AnnotationReader` extended with five new subtype parsers plus
    helpers for `/BS`, `/LE`, and `/Vertices`; `ReadColor` refactored
    to take a key parameter so `/IC` (interior color) reuses the
    same code path as `/C` (border color)
  - `AnnotationWriter` extended with five new dictionary builders
    plus serializers for `BorderStyle`, line endings, line points,
    and vertices

### Tests
- 19 new tests under `tests/Chuvadi.Pdf.Documents.Tests` covering
  metadata properties (date parsing across UTC/offset/date-only/malformed
  inputs, name-vs-string Trapped variants, XMP round-trip,
  Encryption-null-when-unencrypted) and the `EncryptionInfo`
  permission decoders (per-bit theory, default deny, default allow)
- 17 new tests under `tests/Chuvadi.Pdf.Annotations.Tests` covering
  `BorderStyle` validation, shape model constructors, and full
  round-trip via `AnnotationWriter.Add` → `AnnotationReader.GetAnnotations`
  for all five shape subtypes
- Library total: 952 tests passing on the v2.0.1 commit

---

## [1.10.0] - 2026-05-20

### Added
- **Parser fuzz harness** (`tests/Chuvadi.Pdf.Fuzz/`) — hand-rolled mutation
  fuzzer with three targets: `pdf-open` (full document open), `content-stream`
  (content stream parsing), `truetype` (font loading). No NuGet dependencies.
  Mutations include splice, bit-flip, byte replace/insert/delete, boundary-value
  injection, range duplication, and random truncation. Crash inputs are saved
  to `crashes/<target>/<sha256>.bin` with full stack traces in matching `.txt`
  files for triage. See `tests/Chuvadi.Pdf.Fuzz/README.md`
- GitHub Actions workflow `.github/workflows/fuzz.yml` for scheduled fuzz runs
- `tests/Chuvadi.Pdf.Fuzz/FOLLOW-UPS.md` documenting findings deferred to
  PR 2.1 (truetype IndexOutOfRangeException bounds, PdfName.Intern
  ArgumentException tightening)

### Fixed
- `PdfPageCollection.FindPage` no longer recurses without bound on malformed
  page trees. Cyclic `/Kids` references and pathologically deep `/Pages`
  chains now throw `PdfDocumentException` with a clear message instead of
  killing the process with a `StackOverflowException`. Surfaced by the
  `pdf-open` fuzz target. Depth limit: 1024 (real PDFs use depth 1–5)
- `PdfObjectParser` no longer leaks `OverflowException` or `FormatException`
  from `int.Parse` on malformed integer tokens. All six parse sites now go
  through a guarded `ParseInt32` helper that throws `PdfReaderException` with
  the offending token's text snippet and byte offset. Surfaced by the
  `pdf-open` fuzz target after ~5.7M iterations

---

## [1.9.0] - 2026-05-19

### Added
- `benchmarks/Chuvadi.Benchmarks` project (BenchmarkDotNet harness)
  - `BrotliRatioBench`: output-size comparison vs `System.IO.Compression.BrotliStream`
    Optimal and Fastest across 6 representative scenarios (Lorem ipsum, English prose,
    repetitive, moderate, SFNT-like binary, random incompressible)
  - `BrotliThroughputBench`: encode-time comparison on the same scenarios
  - `ParserOpenBench`: `PdfDocument.Open` timing on synthetic single-page and 20-page PDFs
- `tests/Chuvadi.Pdf.Fonts.Woff2.Tests/BrotliLz77Tests.cs` — 11 regression tests covering
  the multi-command emission path

### Changed
- `BrotliCompressedEmitter` rewritten to consume the LZ77 command stream from
  `BrotliCommandStream.Encode()` and emit one Brotli command per LZ77 record. Per-block
  cap raised from 64 KiB to 16 MiB (MNIBBLES=6); inputs larger than that split across
  multiple meta-blocks
- `BrotliCompressedEmitter.TryEmit` (bool fallback) replaced by `Emit` (void, infallible
  for non-empty inputs)
- `BrotliEncoder` simplified — speculative compressed + stored, smaller wins
- Compression ratio on real data improved from 50-100% (single-command stage 3) to 2-6%
  (within ~1% of `BrotliStream` Optimal)

---

## [1.8.0] - 2026-05-19

### Fixed
- RFC 7932 §3.5 "modify rule" violation in `BrotliComplexPrefixCode.RunLengthEncode`:
  consecutive 17 (or 16) codes caused exponential count blowup per the spec's modify
  formula, producing invalid streams. Fixed by inserting a literal-length entry between
  consecutive 17s and 16s to break the run

### Changed
- `BrotliCompressedEmitter` now wires complex prefix codes for inputs with 5+ distinct
  literals (previously fell back to stored meta-blocks)
- `BrotliHuffman.BuildCanonicalCodes` uses explicit `int[] ordered` instead of `var`
  (IDE0008 conformance)
- `BrotliCodeTables.cs` switch-expression arms split one-per-line for `dotnet format`
  conformance

### Added
- `tests/Chuvadi.Pdf.Fonts.Woff2.Tests` — first test project for the WOFF2 module, with
  11 regression tests covering the modify-rule fix across 5..25 distinct literals,
  random data, and repeated text

---

## [1.7.0] - 2026-05-16

### Added
- **Phase 1.1.4** — Digital signature verification (`Chuvadi.Pdf.Signatures`)
  - PKCS#7 / CMS detached signature parsing (PDF 32000-1 §12.8.3.3)
  - Certificate chain extraction and signing-time recovery
  - Byte-range verification against the signed bytes of the document
  - Verification-only in this release; signing remains on the backlog

---

## [1.6.0] - 2026-05-15

### Added
- **Phase 1.1.6** — Linearization / Fast Web View (ISO 32000-1 Annex F)
  - Reader detects linearization and exposes the `/Linearized` parameter
    dictionary through `PdfDocument.IsLinearized` / `PdfDocument.Linearization`
  - Writer produces linearized PDFs with primary hint stream via
    `PdfWriter.WriteLinearized(...)`
  - `BitWriter` / `BitReader` and `PageHintTable` infrastructure for
    sub-byte hint encoding

### Notes
- Spec-conformant output. Real-world viewer compatibility (Acrobat,
  Foxit, browser PDF viewers) is not yet verified end-to-end and is
  tracked in the backlog

---

## [1.5.0] - 2026-05-15

### Added
- **Phase 1.1.3** — Form XObject and image redaction. `Redactor` now
  traces the `Do` operator with full CTM intersection so any Form XObject
  or image overlapping a redaction rect is dropped from the rewritten
  content stream
- **Phase 1.1.7** — Optional content (layers) reader. `OptionalContentReader`
  + `OptionalContentGroup` expose `/OCProperties`, `/OCGs`, default
  configuration name, and resolved visibility (`/ON`, `/OFF`, `BaseState`)
- **Phase 1.1.8** — CMYK render output. `PageRasterizer` and `TiffEncoder`
  support CMYK pixel buffers (TIFF photometric=5)

### Fixed
- `Redactor` was silently corrupting name operands (`Tf`, `cs`, `gs`, `Do`)
  in the rewritten content stream. Regression test added

---

## [1.4.0] - 2026-05-13

### Added
- **Encryption fully wired into the public API:**
  - `PdfDocument.Open(stream, password)` for opening encrypted documents
  - `PdfWriter.Write(..., EncryptionOptions)` for writing encrypted documents
  - `EncryptionOptions` factory methods for AES-128 and AES-256 with owner
    and user passwords
  - `EncryptionVisitor` traverses the object graph and encrypts strings and
    streams in place during write
  - `EncryptionDictionaryBuilder` implements PDF Algorithms 3/5 (R=4,
    standard security handler) and ISO 32000-2 Algorithms 8/9 (R=6,
    AES-256)
- 5 integration tests including byte-level plaintext-absence verification
  on round-tripped encrypted documents

### Changed
- AES-128 and AES-256 are now supported for both read AND write paths
  (v1.3.0 shipped read-only)

---

## [1.3.0] - 2026-05-12

### Added
- **Phase 1.1.2** — Pattern-based redaction (`Chuvadi.Pdf.Redaction`)
  - `PatternRule` and `PatternMatcher` for regex-based content matching
  - `CommonPatterns` library covering SSN, email, phone, ICD-10, NHS
    number, and other common PHI identifiers
- **Phase 1.1.9** — TIFF baseline 6.0 read and write
  (`Chuvadi.Pdf.Images.TiffDecoder` / `TiffEncoder`)
  - Uncompressed, PackBits, and LZW compression
  - Multi-page TIFFs via chained IFDs
- **Phase 1.1.5** — Standard security handler encryption (read path)
  (`Chuvadi.Pdf.Encryption`, `Chuvadi.Cryptography`)
  - RC4-40 and RC4-128 decryption
  - AES-128 decryption
  - Password-based key derivation per Algorithm 2

### Notes
- 604 tests across 19 test projects at tag time
- Write path for encryption arrived in v1.4.0

---

## [1.2.0] - 2026-05-12

### Added
- 8 runnable example projects under `examples/` (TextExtraction,
  Watermark, Redaction, Render, FormFill, Outlines, PageOps,
  Annotations)
- Getting Started guide (`docs/getting-started.md`)
- **Auto-generated Markdown API reference** (`docs/api/`, 117 pages)
  produced by `tools/gen_api_docs.py` parsing XML doc comments from
  every public type across all `src/` modules

### Changed
- CI adds a `docs-up-to-date` job that re-runs `gen_api_docs.py` and
  fails the PR if the resulting Markdown diverges from what's committed
- Style check (`tools/check_style.py`) expanded to scan `examples/`
  alongside `src/` and `tests/`

---

## [1.1.0] - 2026-05-11

### Added
- **Phase 1.1.1** — `Chuvadi.Pdf.Annotations` module (read and write
  per PDF 32000-1 §12.5)
  - `AnnotationReader` and `AnnotationWriter` covering Text, FreeText,
    Link, Stamp, Ink, Markup, and Generic annotation types
- GitHub Actions CI matrix: style check + build/test on Ubuntu,
  Windows, and macOS
- Style checker (`tools/check_style.py`) with line-by-line string
  stripping, `CONFLICT_OVERRIDES`, and `bin/` / `obj/` exclusion

### Fixed
- `Chuvadi.Pdf.Text.csproj` was missing several `ProjectReference`
  entries; added
- 10 `var`-in-`src` violations rewritten with explicit types to satisfy
  the IDE0008 rule that the new style checker now enforces

---

## [1.0.0] - 2026-05-11

### Added
- Initial public release. Closes Phase 2 (rendering, watermarking,
  redaction, forms, CLI). 17 modules, ~564 tests, 0 failures
- **Read pipeline:** PDF 1.4–2.0 ingestion (classic and stream xref,
  including hybrid); all standard non-encryption filters (Flate,
  ASCIIHex, ASCII85, RunLength, LZW); Type1 standard 14 fonts, TrueType,
  and CFF/Type1C inspection; full content stream tokenizer and parser
- **Text extraction:** operator-walking, layout-aware, and glyph-level
  fallback strategies (the glyph extractor handles non-Latin scripts via
  TrueType outline extraction)
- **Rendering:** zero-dependency scanline rasterizer producing PNG and
  BMP output; standard PDF fonts (Helvetica, Times, Courier) plus
  embedded TrueType; adaptive Bezier flattening; both fill rules;
  butt/square stroke caps
- **Document operations:** merge, split, delete pages, rotate, extract
  page ranges; text watermarks with rotation, opacity, and per-page
  targeting; image watermarks via PNG XObject embedding
- **PHI-safe redaction:** rectangle-based content-stream rewriting with
  byte-level removal (the redacted text is absent from the output PDF
  at both operator and indirect-object levels). Conservative TJ array
  drop. Tests grep the output bytes to verify removal
- **AcroForms:** read the field tree (fully-qualified names, types,
  current values, object IDs); fill values, set button `/AS`, mark
  `/NeedAppearances=true`
- **Outlines (bookmarks):** read the full tree with children and
  resolve destinations to page indices
- **CLI** (`chuvadi`): 11 user verbs (info, render, watermark, redact,
  form-fill, extract-text, outlines, merge, split, delete, rotate) plus
  6 debug verbs (tokenize, dump-objects, parse-content, decode-stream,
  inspect-xref, validate-fonts)
- Project scaffolding for all 17 Phase 1 modules, Apache 2.0 license,
  initial CI/CD pipelines

---

<!-- Template for future entries:

## [x.y.z] - YYYY-MM-DD

### Added
- New features

### Changed
- Changes to existing features

### Fixed
- Bug fixes

### Deprecated
- Features that will be removed in a future release

### Removed
- Features removed in this release

### Security
- Vulnerability fixes

-->
