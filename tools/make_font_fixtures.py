# Copyright 2025 Chuvadi Contributors
# SPDX-License-Identifier: Apache-2.0
"""Generates the synthetic font fixtures used by the font tests.

Every glyph is a few rectangles, so the fonts carry no third-party design.
Run from the repository root (needs fontTools):

    python tools/make_font_fixtures.py

Writes to tests/Fixtures/Fonts/:
  ChuvadiTest-Cff.otf  name-keyed CFF: local and global subroutines, a
                       shortint (byte 28) operand, an 'fi' ligature (GSUB)
  ChuvadiTest-Cid.otf  CID-keyed CFF: two font dictionaries whose local
                       subroutines and nominalWidthX differ
  ChuvadiTest-Glyf.ttf TrueType outlines, same glyphs and metrics
  ChuvadiTest.ttc      collection: face 0 the TrueType font, face 1 the CFF font
"""

import io
import os

from fontTools.cffLib import FDArrayIndex, FDSelect, FontDict, PrivateDict, SubrsIndex
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
from fontTools.fontBuilder import FontBuilder
from fontTools.misc.psCharStrings import T2CharString
from fontTools.misc.timeTools import timestampFromString
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTCollection, TTFont

OUT = os.path.join("tests", "Fixtures", "Fonts")
UPM = 1000
FIXED_TIME = timestampFromString("Wed Oct 28 00:00:00 2026")  # reproducible output

# name -> (advance, unicode or None, rectangles (x0, y0, x1, y1))
GLYPHS = {
    ".notdef": (500, None, [(50, 0, 450, 700)]),
    "space": (250, 0x20, []),
    "A": (600, 0x41, [(50, 0, 150, 700), (450, 0, 550, 700), (50, 600, 550, 700), (150, 300, 450, 380)]),
    "B": (580, 0x42, [(60, 0, 160, 700), (160, 0, 500, 80), (160, 310, 480, 390), (160, 620, 500, 700)]),
    "H": (640, 0x48, [(60, 0, 160, 700), (480, 0, 580, 700), (160, 310, 480, 390)]),
    "x": (500, 0x78, [(40, 0, 140, 500), (360, 0, 460, 500), (140, 210, 360, 290)]),
    "f": (320, 0x66, [(60, 0, 150, 700), (150, 400, 300, 470), (150, 630, 280, 700)]),
    "i": (240, 0x69, [(70, 0, 160, 500), (70, 600, 160, 690)]),
    # Wide enough that a coordinate needs the shortint (28) operand encoding.
    "W": (1300, 0x57, [(40, 0, 140, 700), (1160, 0, 1260, 700), (40, 0, 1260, 90)]),
    "f_i": (540, None, [(60, 0, 150, 700), (150, 400, 450, 470), (380, 0, 470, 500), (150, 630, 470, 700)]),
}
ORDER = list(GLYPHS)

OS2 = dict(
    sTypoAscender=800, sTypoDescender=-200, sTypoLineGap=90,
    usWinAscent=900, usWinDescent=250, sxHeight=500, sCapHeight=700,
    yStrikeoutPosition=260, yStrikeoutSize=50, usWeightClass=700,
    fsSelection=0x20 | 0x80, version=4,
)
HHEA = dict(ascent=850, descent=-250, lineGap=30)
POST = dict(underlinePosition=-120, underlineThickness=60)
NAMES = dict(familyName="Chuvadi Test", styleName="Bold", psName="ChuvadiTest-Bold")
FEATURES = "languagesystem DFLT dflt; languagesystem latn dflt; feature liga { sub f i by f_i; } liga;"


def rect_program(x0, y0, x1, y1, cx, cy):
    """Type 2 operators drawing one closed rectangle, from pen (cx, cy)."""
    return [x0 - cx, y0 - cy, "rmoveto", x1 - x0, y1 - y0, -(x1 - x0), "hlineto"]


def cff_program(name, nominal):
    """Charstring for a glyph: width, rectangles, endchar. The first
    rectangle of every glyph after .notdef is drawn by a subroutine call."""
    adv, _, rects = GLYPHS[name]
    prog = [adv - nominal]
    cx = cy = 0
    for (x0, y0, x1, y1) in rects:
        prog += rect_program(x0, y0, x1, y1, cx, cy)
        cx, cy = x0, y1  # hlineto ends back at the left edge, top
    prog.append("endchar")
    return prog


def base_builder(cff, names):
    fb = FontBuilder(UPM, isTTF=not cff)
    fb.setupGlyphOrder([names[n] for n in ORDER])
    fb.setupCharacterMap({u: names[n] for n, (_, u, _) in GLYPHS.items() if u is not None})
    fb.setupHorizontalMetrics({names[n]: (adv, min([r[0] for r in rects] or [0])) for n, (adv, _, rects) in GLYPHS.items()})
    fb.font.recalcTimestamp = False
    fb.updateHead(macStyle=1, created=FIXED_TIME, modified=FIXED_TIME)
    fb.setupHorizontalHeader(**HHEA)
    fb.setupNameTable(NAMES)
    fb.setupOS2(**OS2)
    fb.setupPost(**POST)
    return fb


def features(names):
    return FEATURES.replace("f_i", names["f_i"]).replace(" f i ", f" {names['f']} {names['i']} ")


def build_cff(names=None):
    names = names or {n: n for n in ORDER}
    fb = base_builder(True, names)
    nominal = 400
    charstrings = {names[n]: T2CharString(program=cff_program(n, nominal)) for n in ORDER}
    fb.setupCFF(NAMES["psName"], {"FullName": "Chuvadi Test Bold"}, charstrings,
                {"nominalWidthX": nominal, "defaultWidthX": 500})
    addOpenTypeFeaturesFromString(fb.font, features(names))
    font = roundtrip(fb.font)

    # Move each glyph's first rectangle into a subroutine: 'A' and 'H' call
    # local subrs, 'B' a global one; the rest stay inline.
    cff_table = font["CFF "].cff
    top = cff_table.topDictIndex[0]
    priv = top.Private
    gsubrs = cff_table.GlobalSubrs
    local = SubrsIndex()
    priv.Subrs = local
    for name, target, index in (("A", local, 0), ("H", local, 1), ("x", local, 2), ("B", gsubrs, 0)):
        cs = top.CharStrings[names[name]]
        cs.decompile()
        prog = cs.program
        # program: [width, dx, dy, rmoveto, w, h, -w, hlineto, ...]; the
        # width is always present (no advance equals nominalWidthX).
        start = 1
        sub = prog[start:start + 8] + ["return"]
        target.append(T2CharString(program=sub, private=priv, globalSubrs=gsubrs))
        bias = 107
        call = "callsubr" if target is local else "callgsubr"
        cs.program = prog[:start] + [index - bias, call] + prog[start + 8:]
    # An unused local subroutine, so pruning has something to remove.
    local.append(T2CharString(program=[0, 0, "rmoveto", "return"], private=priv, globalSubrs=gsubrs))
    for name in ORDER:
        top.CharStrings[names[name]].private = priv
    return roundtrip(font)


def build_cid():
    """Builds the font with CID glyph names and converts it to CID-keyed with two FDs."""
    names = {n: f"cid{i:05d}" if i else ".notdef" for i, n in enumerate(ORDER)}
    font = build_cff(names)
    cff_table = font["CFF "].cff
    top = cff_table.topDictIndex[0]
    old_priv = top.Private
    gsubrs = cff_table.GlobalSubrs

    def private(nominal, subrs):
        p = PrivateDict(strings=cff_table.strings)
        p._isCFF2 = False  # a new dict has no file to tell CFF from CFF2
        p.BlueValues = [-10, 0, 700, 710]
        p.StdHW = 80
        p.StdVW = 90
        p.nominalWidthX = nominal
        p.defaultWidthX = 500
        p.Subrs = subrs
        return p

    # FD 0 keeps the original local subrs; FD 1 has them in reverse order
    # (so the same subr number means a different subroutine per FD) and a
    # different nominalWidthX.
    fd_of = {n: (1 if n in ("H", "x", "W", "f_i") else 0) for n in ORDER}
    subrs0 = old_priv.Subrs
    subrs1 = SubrsIndex()
    for i in reversed(range(len(subrs0))):
        cs = subrs0[i]
        cs.decompile()
        subrs1.append(T2CharString(program=list(cs.program)))
    priv0 = private(400, subrs0)
    priv1 = private(150, subrs1)
    n_local = len(subrs0)

    for name in ORDER:
        cs = top.CharStrings[names[name]]
        cs.decompile()
        prog = list(cs.program)
        if fd_of[name] == 1:
            prog[0] = prog[0] + 400 - 150  # re-base the width on FD 1's nominalWidthX
            for k in range(len(prog) - 1):
                if prog[k + 1] == "callsubr":
                    prog[k] = (n_local - 1 - (prog[k] + 107)) - 107
        cs.program = prog
        cs.private = priv1 if fd_of[name] == 1 else priv0

    fdarray = FDArrayIndex()
    for i, p in enumerate((priv0, priv1)):
        fd = FontDict()
        fd.setCFF2(False)
        fd.Private = p
        fd.FontName = f"ChuvadiTest-Bold-FD{i}"
        fdarray.append(fd)
    del top.rawDict["Private"]
    del top.Private
    top.ROS = ("Adobe", "Identity", 0)
    top.CIDCount = len(ORDER)
    top.FDArray = fdarray
    top.FDSelect = FDSelect()
    top.FDSelect.format = 3
    top.FDSelect.gidArray = [fd_of[n] for n in ORDER]
    return roundtrip(font)


def build_glyf():
    names = {n: n for n in ORDER}
    fb = base_builder(False, names)
    glyphs = {}
    for name in ORDER:
        pen = TTGlyphPen(None)
        for (x0, y0, x1, y1) in GLYPHS[name][2]:
            pen.moveTo((x0, y0))
            pen.lineTo((x0, y1))
            pen.lineTo((x1, y1))
            pen.lineTo((x1, y0))
            pen.closePath()
        glyphs[name] = pen.glyph()
    fb.setupGlyf(glyphs)
    fb.setupMaxp()
    addOpenTypeFeaturesFromString(fb.font, features(names))
    return roundtrip(fb.font)


def roundtrip(font):
    buf = io.BytesIO()
    font.save(buf)
    buf.seek(0)
    return TTFont(buf, recalcTimestamp=False)


def save(font, name):
    path = os.path.join(OUT, name)
    font.save(path, reorderTables=True)
    print(path, os.path.getsize(path), "bytes")


def main():
    os.makedirs(OUT, exist_ok=True)
    cff = build_cff()
    save(cff, "ChuvadiTest-Cff.otf")
    save(build_cid(), "ChuvadiTest-Cid.otf")
    glyf = build_glyf()
    save(glyf, "ChuvadiTest-Glyf.ttf")
    collection = TTCollection()
    collection.fonts = [
        TTFont(os.path.join(OUT, "ChuvadiTest-Glyf.ttf"), recalcTimestamp=False),
        TTFont(os.path.join(OUT, "ChuvadiTest-Cff.otf"), recalcTimestamp=False),
    ]
    collection.save(os.path.join(OUT, "ChuvadiTest.ttc"))
    print(os.path.join(OUT, "ChuvadiTest.ttc"))


if __name__ == "__main__":
    main()
