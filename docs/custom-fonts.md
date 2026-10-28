# Embedding custom fonts in authored PDFs

`PdfDocumentBuilder` can embed a custom font so authored content draws text in
it — including non-Latin scripts such as Tamil and Devanagari.

```csharp
byte[] ttf = File.ReadAllBytes("LiPi-Sans-Tamil.ttf");

PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
    .AddTrueTypeFont("LiPiSansTamil", ttf);

PageBuilder page = builder.AddPage(PageSize.A4);
page.DrawText("கமலனவ", 60, 100, "LiPiSansTamil", 48, Colors.Black);

File.WriteAllBytes("out.pdf", builder.ToByteArray());
```

The font is embedded as a composite Type0 font with Identity-H encoding. Only
the glyphs actually drawn get width and `ToUnicode` entries, so extracted text
round-trips back to Unicode. A font used on several pages is embedded once and
shared.

## Font formats (3.25)

| Font | Embedded as | Program |
|---|---|---|
| TrueType (`.ttf`, `glyf` outlines) | `CIDFontType2` | `/FontFile2`, subset |
| OpenType with CFF outlines (`.otf`) | `CIDFontType0` | `/FontFile3 /CIDFontType0C`, subset, Flate-compressed |
| A face of a collection (`.ttc`, `.otc`) | as its outlines | that face only |

```csharp
builder.AddFont("Inter", File.ReadAllBytes("Inter-Regular.otf"));
builder.AddFont("NotoSerifTC", File.ReadAllBytes("NotoSerifCJK-Bold.ttc"), faceIndex: 3);
```

`AddTrueTypeFont` accepts all of these too (a collection's first face).
`FontFace.GetFaceCount(bytes)` tells how many faces a collection has.

A CFF font is rewritten as a CID-keyed CFF with registry-ordering-supplement
Adobe-Identity-0: only the drawn glyphs are written, the CFF charset gives each
its original glyph id as its CID, and subroutines no drawn glyph calls are
emptied (their numbering stays). A page of CJK text from a 16 MB collection
embeds in about 100 KB.

Not supported: variable fonts (instantiate a static instance first; CFF2 is
refused), and WOFF/WOFF2 input (`Chuvadi.Pdf.Fonts.Woff2.Woff2Unpacker` turns a
WOFF2 into an sfnt first).

## Measuring: FontFace

`FontFace.Load(bytes, faceIndex)` parses a face once; keep it and reuse it.

```csharp
FontFace face = FontFace.Load(File.ReadAllBytes("Inter-Regular.otf"));
double ascent = face.Scale(face.Ascender, fontSize);   // design units -> points
int advance = face.MeasureText("Hello");               // design units, no shaping
FontBox box = face.GetGlyphBounds(face.GetGlyphIndex('H'));
```

It reports `UnitsPerEm`; the hhea, OS/2 typo and OS/2 win vertical metrics and
the resolved `Ascender`/`Descender`/`LineGap` (typo metrics when the font sets
USE_TYPO_METRICS, hhea otherwise); `CapHeight` and `XHeight` (measured from
"H" and "x" when OS/2 is older than version 2); underline and strikeout
position and thickness; `ItalicAngle`; names and style; per-glyph advance,
left side bearing and bounds; and the `CharacterMap`. A face is safe to share
between threads.

## Shaping: TextShaper with a FontFace

`TextShaper.Shape(face, text, script)` gives the same glyphs as
`TextShaper.Shape(bytes, text, script)` but parses nothing per call: the
face's GSUB/GPOS feature lookups are cached with it. Use it inside a
line-fitting loop. Benchmark (`benchmarks/Chuvadi.Benchmarks`,
`--filter *Shaping*`): a Latin line 418 µs from bytes, 173 µs from a face;
Tamil 35 µs and 7.5 µs.

Known gap: Indic shaping does not yet match HarfBuzz for many conjuncts and
reph/matra positions (see BACKLOG). Latin shaping does.

## Drawing shaped text that stays searchable

Pass the source text and each glyph's cluster start so the run copies,
searches and extracts as written:

```csharp
IReadOnlyList<ShapedGlyph> glyphs = TextShaper.Shape(face, text, LipiScript.Latin);
int[] clusterStarts = ...; // for each glyph, the UTF-16 index in text where its cluster starts
page.DrawShapedRun(glyphs, text, clusterStarts, x, y, "Inter", 12, Colors.Black);
```

A cluster drawn by one glyph gets a `ToUnicode` entry (a ligature glyph maps
to "fi"); a cluster drawn by several glyphs, or by a glyph that already stands
for other text, is wrapped in `/Span <</ActualText …>> BDC … EMC`. Chuvadi's
own `TextExtractor` honours `/ActualText`. `GlyphRunStyle` adds horizontal
scaling (advances scale with it), a slant for synthetic italic, and a stroke
width for synthetic bold (render mode 2).

`DrawText` with the automatic LiPi font ("Lipi") shapes Indic runs and wraps
each in `/ActualText` with its source text.

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

Bundle the resulting static fonts with your application and load them with
`AddFont` or `AddTrueTypeFont`.
