# Embedding custom fonts in authored PDFs

`PdfDocumentBuilder` can embed a custom TrueType font so authored content draws
text in it — including non-Latin scripts such as Tamil and Devanagari.

```csharp
byte[] ttf = File.ReadAllBytes("LiPi-Sans-Tamil.ttf");

PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
    .AddTrueTypeFont("LiPiSansTamil", ttf);

PageBuilder page = builder.AddPage(PageSize.A4);
page.DrawText("கமலனவ", 60, 100, "LiPiSansTamil", 48, Colors.Black);

File.WriteAllBytes("out.pdf", builder.ToByteArray());
```

The font is embedded as a composite Type0 / CIDFontType2 font with Identity-H
encoding. Only the glyphs actually drawn get width and `ToUnicode` entries, so
extracted text round-trips back to Unicode. A font used on several pages is
embedded once and shared.

## Requirements and limitations

**Static TrueType only.** The font must be a static TrueType (`glyf`) program.
Variable fonts must be instantiated to a static instance first, and web fonts
(WOFF/WOFF2) must be decoded to TTF first — Chuvadi does not read those formats
on input. See the conversion recipe below.

**No complex-script shaping (yet).** Text is emitted in logical order without
OpenType shaping (no GSUB/GPOS, no reordering). Latin renders correctly, and
Indic renders correctly for isolated or already-ordered glyphs. Scripts that
need conjunct formation or matra reordering (for example "கி" or "क्ष") are not
yet shaped — that is a separate, larger effort.

**Glyph subsetting (automatic).** Only the glyphs actually drawn are embedded,
and non-rendering tables (`GSUB`/`GPOS`/`GDEF`/`cmap`/`post`) are dropped — the
viewer never consults them for a CIDFontType2 with an Identity CID-to-GID map,
and the layout tables are large in complex-script fonts. This typically shrinks
the embedded `FontFile2` by an order of magnitude or more (e.g. a Tamil page
went from ~82 KB to ~1.6 KB). Glyph numbering is preserved, so the embedding
otherwise behaves identically. CFF/OpenType-CFF fonts are embedded whole (the
subsetter operates on `glyf`-based fonts).

## Converting a variable WOFF2 to a static TTF

Many modern fonts (including LiPi Sans) ship as variable WOFF2. Convert one to a
static TTF once, offline, with [fontTools](https://github.com/fonttools/fonttools):

```bash
pip install fonttools brotli

python - <<'PY'
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

font = TTFont("LiPi-Sans-Tamil.woff2")          # reads WOFF2
instantiateVariableFont(font, {"wght": 400, "wdth": 100})  # pick a static instance
font.flavor = None                               # write plain TTF, not WOFF2
font.save("LiPi-Sans-Tamil.ttf")
PY
```

Bundle the resulting static TTFs with your application and load them with
`AddTrueTypeFont`.
