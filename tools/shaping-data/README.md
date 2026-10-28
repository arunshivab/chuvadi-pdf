# Shaping data and HarfBuzz conformance

`Chuvadi.Pdf.Text.Shaping` is a port of HarfBuzz's OpenType shaper (see
`THIRD-PARTY-NOTICES.md`). This folder holds what keeps it in step with
HarfBuzz.

## Unicode and Indic tables

`src/Chuvadi.Pdf.Text.Shaping/Unicode/UnicodeTables.g.cs` is generated from
HarfBuzz's own Unicode data, so the shaper uses the same Unicode version as
HarfBuzz:

```sh
git clone https://github.com/harfbuzz/harfbuzz <hb>      # the version to match
g++ -O1 -std=c++17 -w -I <hb>/src -DHB_NO_MT -o hb_ucd_dump tools/shaping-data/hb_ucd_dump.cc -lm
./hb_ucd_dump                                           # writes ucd.tsv
python tools/shaping-data/gen_shaping_tables.py ucd.tsv
```

## Conformance corpora

`hb_conformance.py` (needs `fontTools` and `uharfbuzz`) builds the two
corpora — 19,641 Indic syllables and 9,852 words, stress strings and Latin
lines — and shapes every line with HarfBuzz:

```sh
python tools/shaping-data/hb_conformance.py full corpus.tsv
CHUVADI_HB_CORPUS=corpus.tsv dotnet test tests/Chuvadi.Pdf.Text.Shaping.Tests
```

`hb_conformance.py sample` regenerates the fixture the normal test run uses
(`tests/Chuvadi.Pdf.Text.Shaping.Tests/Fixtures/HarfBuzzConformance.tsv`).
3.26.0 matches HarfBuzz 15.0.0 on every line of both corpora.
