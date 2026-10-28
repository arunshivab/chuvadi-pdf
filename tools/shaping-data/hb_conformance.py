#!/usr/bin/env python3
"""Builds the HarfBuzz conformance corpora for TextShaper.

Two corpora, generated deterministically:
  1. syllables: 19,641 single syllables over the nine Indic LiPi Sans faces
     (consonant, consonant+virama, consonant+matra, conjuncts, reph, signs);
  2. words: multi-syllable words with ZWJ/ZWNJ, broken clusters and random
     character strings per script, plus Latin text on LiPi Latin, Carlito,
     Liberation Serif and the CFF test fixture (no GPOS: fallback marks).

Each line is shaped with HarfBuzz (uharfbuzz) and written as
    script <TAB> code points (hex) <TAB> font file <TAB> glyph:cluster:x_advance:x_offset:y_offset ...

Usage (needs fontTools and uharfbuzz):
    python tools/shaping-data/hb_conformance.py full <out.tsv>
    python tools/shaping-data/hb_conformance.py sample <out.tsv>   # the test fixture

Run the full file through the tests with:
    CHUVADI_HB_CORPUS=<out.tsv> dotnet test tests/Chuvadi.Pdf.Text.Shaping.Tests
"""

import io
import os
import random
import sys
import unicodedata

import uharfbuzz as hb
from fontTools.ttLib import TTFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
LIPI = os.path.join(ROOT, 'assets', 'fonts', 'lipi-sans', 'fonts')
FONT_PATHS = {
    'Carlito-Regular.ttf': os.path.join(ROOT, 'tests', 'Chuvadi.Pdf.Text.Shaping.Tests', 'Fixtures', 'Carlito-Regular.ttf'),
    'LiberationSerif-Regular.ttf': os.path.join(ROOT, 'tools', 'fonts', 'LiberationSerif-Regular.ttf'),
    'ChuvadiTest-Cff.otf': os.path.join(ROOT, 'tests', 'Fixtures', 'Fonts', 'ChuvadiTest-Cff.otf'),
}
BLOCKS = {'Devanagari': 0x900, 'Bengali': 0x980, 'Gurmukhi': 0xA00, 'Gujarati': 0xA80, 'Odia': 0xB00,
          'Tamil': 0xB80, 'Telugu': 0xC00, 'Kannada': 0xC80, 'Malayalam': 0xD00}
ISO = {'Latin': 'Latn', 'Devanagari': 'Deva', 'Bengali': 'Beng', 'Gurmukhi': 'Guru', 'Gujarati': 'Gujr',
       'Odia': 'Orya', 'Tamil': 'Taml', 'Telugu': 'Telu', 'Kannada': 'Knda', 'Malayalam': 'Mlym'}


def font_path(name):
    return FONT_PATHS.get(name) or os.path.join(LIPI, name)


def syllables():
    random.seed(7)
    rows = []
    for name, b in BLOCKS.items():
        cmap = TTFont(font_path('LiPi-Sans-%s.woff2' % name)).getBestCmap()

        def ok(c):
            return c in cmap and unicodedata.name(chr(c), '') != ''

        cons = [c for c in range(b + 0x15, b + 0x3A) if ok(c)]
        vowels = [c for c in range(b + 0x3E, b + 0x4D) if ok(c) and 'VOWEL SIGN' in unicodedata.name(chr(c))]
        h = b + 0x4D
        marks = [c for c in (b + 1, b + 2, b + 3) if ok(c)]
        ra = b + 0x30
        words = set()
        for c in cons:
            words.add(chr(c))
            words.add(chr(c) + chr(h))
            for v in vowels:
                words.add(chr(c) + chr(v))
        for _ in range(400):
            c1, c2 = random.choice(cons), random.choice(cons)
            v = random.choice(vowels)
            words.add(chr(c1) + chr(h) + chr(c2))
            words.add(chr(c1) + chr(h) + chr(c2) + chr(v))
            words.add(chr(ra) + chr(h) + chr(c2) + chr(v))
            if marks:
                words.add(chr(c1) + chr(v) + chr(random.choice(marks)))
            c3 = random.choice(cons)
            words.add(chr(c1) + chr(h) + chr(c2) + chr(h) + chr(c3) + chr(v))
        for w in sorted(words):
            rows.append((name, w, 'LiPi-Sans-%s.woff2' % name))
    return rows


def words():
    random.seed(11)
    rows = []
    for sc, b in BLOCKS.items():
        block = [chr(u) for u in range(b, b + 0x80) if unicodedata.category(chr(u)) != 'Cn']
        cons = [c for c in block if unicodedata.category(c) == 'Lo' and 'LETTER' in unicodedata.name(c, '') and
                not any(v in unicodedata.name(c) for v in (' A', ' AA', ' I', ' II', ' U', ' UU', ' E', ' AI', ' O', ' AU', ' VOCALIC'))]
        marks = [c for c in block if unicodedata.category(c) in ('Mn', 'Mc')]
        h = chr(b + 0x4D)
        font = 'LiPi-Sans-%s.woff2' % sc
        for _ in range(700):
            w = ''
            for _ in range(random.randint(1, 4)):
                k = random.random()
                if k < 0.3:
                    w += random.choice(cons) + random.choice(marks)
                elif k < 0.5:
                    w += random.choice(cons) + h + random.choice(cons) + random.choice(marks + [''])
                elif k < 0.6:
                    w += random.choice(cons) + h + '‍' + random.choice(cons)
                elif k < 0.67:
                    w += random.choice(cons) + h + '‌' + random.choice(cons)
                elif k < 0.75:
                    w += random.choice(cons) + h + random.choice(cons) + h + random.choice(cons) + random.choice(marks)
                elif k < 0.85:
                    w += random.choice(block)
                else:
                    w += random.choice(cons)
            if random.random() < 0.1:
                w = random.choice(marks) + w
            if random.random() < 0.1:
                w += ' ' + random.choice(block)
            rows.append((sc, w, font))
        for _ in range(300):
            rows.append((sc, ''.join(random.choice(block + ['‌', '‍', ' ', '◌'])
                                     for _ in range(random.randint(1, 6))), font))
    latin = ['office affine fluffy', 'The quick brown fox jumps over the lazy dog.', 'Ünïcödé é ä ñ',
             '1⁄2 and 3⁄4', 'A B C D', 'soft­hyphen', 'zero‍width‌join',
             'AVATAR Wave To Yo', 'ﬁﬂ ffi ffl fj', 'x̣́ q̣̇', '“quotes” — dash – ‘s’', 'ΑΒΓ абв',
             '(1) [2] {3} 4/5 6%']
    for f in ['LiPi-Sans-Latin.woff2', 'Carlito-Regular.ttf', 'LiberationSerif-Regular.ttf', 'ChuvadiTest-Cff.otf']:
        for t in latin:
            rows.append(('Latin', t, f))
        for _ in range(200):
            rows.append(('Latin', ''.join(random.choice('abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZfilft.,;:!? ́̈éü')
                                          for _ in range(random.randint(1, 12))), f))
    return rows


def main():
    mode, out_path = sys.argv[1], sys.argv[2]
    rows1, rows2 = syllables(), words()
    if mode == 'sample':
        rows = [r for i, r in enumerate(rows1) if i % 20 == 0] + [r for i, r in enumerate(rows2) if i % 8 == 0]
        rows = [r for r in rows if not r[2].startswith(('Liberation', 'ChuvadiTest'))]
    else:
        rows = rows1 + rows2
    fonts = {}
    with open(out_path, 'w', encoding='utf-8', newline='\n') as out:
        out.write('# HarfBuzz %s shaping results (tools/shaping-data/hb_conformance.py %s).\n' % (hb.version_string(), mode))
        out.write('# Columns: script, code points (hex), font, then glyph:cluster:x_advance:x_offset:y_offset\n')
        out.write('# per glyph in font units.\n')
        for script, text, font in rows:
            if font not in fonts:
                f = TTFont(font_path(font))
                f.flavor = None
                b = io.BytesIO()
                f.save(b)
                fonts[font] = hb.Font(hb.Face(b.getvalue()))
            buf = hb.Buffer()
            buf.add_str(text)
            buf.guess_segment_properties()
            buf.script = ISO[script]
            hb.shape(fonts[font], buf)
            glyphs = ' '.join('%d:%d:%d:%d:%d' % (i.codepoint, i.cluster, p.x_advance, p.x_offset, p.y_offset)
                              for i, p in zip(buf.glyph_infos, buf.glyph_positions))
            out.write('%s\t%s\t%s\t%s\n' % (script, ' '.join('%04X' % ord(c) for c in text), font, glyphs))


if __name__ == '__main__':
    main()
