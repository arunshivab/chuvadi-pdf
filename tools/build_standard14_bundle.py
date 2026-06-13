#!/usr/bin/env python3
"""
Builds the Standard 14 glyph outline bundle.

Reads Liberation or URW TTF files from tools/fonts/ and emits a binary
bundle to src/Chuvadi.Pdf.Fonts.Rendering/Resources/Standard14.bin that the
runtime loads to provide outlines for the PDF Standard 14 fonts even when
the host system lacks them (Blazor WebAssembly, headless servers, etc.).

Required input files (drop in tools/fonts/):
  - LiberationSans-Regular.ttf      (mapped to Helvetica)
  - LiberationSans-Bold.ttf         (mapped to Helvetica-Bold)
  - LiberationSans-Italic.ttf       (Helvetica-Oblique)
  - LiberationSans-BoldItalic.ttf   (Helvetica-BoldOblique)
  - LiberationSerif-Regular.ttf     (Times-Roman)
  - LiberationSerif-Bold.ttf        (Times-Bold)
  - LiberationSerif-Italic.ttf      (Times-Italic)
  - LiberationSerif-BoldItalic.ttf  (Times-BoldItalic)
  - LiberationMono-Regular.ttf      (Courier)
  - LiberationMono-Bold.ttf         (Courier-Bold)
  - LiberationMono-Italic.ttf       (Courier-Oblique)
  - LiberationMono-BoldItalic.ttf   (Courier-BoldOblique)
  - StandardSymbolsPS.ttf           (Symbol)
  - D050000L.ttf                    (ZapfDingbats)

Liberation Sans/Serif/Mono are SIL OFL licensed; StandardSymbolsPS and
D050000L from URW++ are AGPL-with-font-exception. Both are commercially
redistributable under Apache 2.0.

Output binary format (little-endian throughout):
  Header (16 bytes):
    [0..3]   'CV14' signature
    [4..7]   version (1)
    [8..11]  number of fonts (14)
    [12..15] reserved
  For each font:
    [0..31]  font name as null-padded ASCII
    [32..35] units per em (int32)
    [36..39] number of glyph entries (int32)
    [40..]   glyph entries, each:
       char_code (uint16), num_points (uint16), each point: (int16 x, int16 y, byte flags)
"""

import struct, sys, os, pathlib

# Lazy-import fontTools; emit a clear error if missing.
try:
    from fontTools.ttLib import TTFont
    from fontTools.pens.recordingPen import RecordingPen
    from fontTools.pens.qu2cuPen import Qu2CuPen
except ImportError:
    print("ERROR: pip install fonttools", file=sys.stderr)
    sys.exit(2)

FONT_MAP = {
    "Helvetica":             "LiberationSans-Regular.ttf",
    "Helvetica-Bold":        "LiberationSans-Bold.ttf",
    "Helvetica-Oblique":     "LiberationSans-Italic.ttf",
    "Helvetica-BoldOblique": "LiberationSans-BoldItalic.ttf",
    "Times-Roman":           "LiberationSerif-Regular.ttf",
    "Times-Bold":            "LiberationSerif-Bold.ttf",
    "Times-Italic":          "LiberationSerif-Italic.ttf",
    "Times-BoldItalic":      "LiberationSerif-BoldItalic.ttf",
    "Courier":               "LiberationMono-Regular.ttf",
    "Courier-Bold":          "LiberationMono-Bold.ttf",
    "Courier-Oblique":       "LiberationMono-Italic.ttf",
    "Courier-BoldOblique":   "LiberationMono-BoldItalic.ttf",
    "Symbol":                "StandardSymbolsPS.ttf",
    "ZapfDingbats":          "D050000L.ttf",
}

CMD_MOVE = 0
CMD_LINE = 1
CMD_CUBIC = 2
CMD_QUAD = 3
CMD_CLOSE = 4

def build_code_map(font):
    """Map PDF character codes (0x20..0xFF) to glyph names.

    Text fonts expose a Unicode cmap (getBestCmap). Symbol-encoded fonts
    (Symbol, ZapfDingbats) have no Unicode cmap; their (3, 0) symbol subtable
    maps code 0xF000+cc, so a PDF byte code cc resolves through 0xF000+cc.
    """
    unicode_cmap = font.getBestCmap()
    if unicode_cmap is not None:
        return {cc: unicode_cmap[cc] for cc in range(0x20, 0x100) if cc in unicode_cmap}

    # Symbol font: find a (3, 0) symbol subtable and resolve via 0xF000+cc.
    symbol = None
    for table in font["cmap"].tables:
        if table.platformID == 3 and table.platEncID == 0:
            symbol = table.cmap
            break
    if symbol is None:
        return {}

    code_map = {}
    for cc in range(0x20, 0x100):
        name = symbol.get(0xF000 + cc) or symbol.get(cc)
        if name is not None:
            code_map[cc] = name
    return code_map


def extract_glyph(glyph_set, glyph_name, units_per_em):
    """Return [(cmd, [(x,y),...]), ...] for a named glyph.

    Quadratic TrueType curves are converted to cubic Beziers via Qu2CuPen so
    every curve is a single 3-point segment the C# loader renders directly.
    A raw qCurveTo may bundle many off-curve points in one call (TrueType
    implied on-curve points), which the loader does not decompose.
    """
    if glyph_name not in glyph_set:
        return None
    glyph = glyph_set[glyph_name]
    pen = RecordingPen()
    converter = Qu2CuPen(pen, max_err=max(0.5, units_per_em / 1000.0), all_cubic=True)
    glyph.draw(converter)
    out = []
    for verb, args in pen.value:
        if verb == "moveTo":
            out.append((CMD_MOVE, list(args)))
        elif verb == "lineTo":
            out.append((CMD_LINE, list(args)))
        elif verb == "qCurveTo":
            # Fallback only; all_cubic=True should prevent this.
            out.append((CMD_QUAD, list(args)))
        elif verb == "curveTo":
            out.append((CMD_CUBIC, list(args)))
        elif verb == "closePath":
            out.append((CMD_CLOSE, []))
    return out

def pack_glyph(commands):
    """Serialize a glyph's outline. Returns bytes."""
    parts = bytearray()
    parts.extend(struct.pack("<H", len(commands)))
    for cmd, pts in commands:
        parts.append(cmd)
        parts.append(len(pts))
        for (x, y) in pts:
            parts.extend(struct.pack("<hh", int(round(x)), int(round(y))))
    return bytes(parts)

def build_bundle(input_dir, output_path):
    fonts_dir = pathlib.Path(input_dir)
    out = bytearray()
    # Header
    out.extend(b"CV14")
    out.extend(struct.pack("<I", 1))   # version
    out.extend(struct.pack("<I", len(FONT_MAP)))
    out.extend(struct.pack("<I", 0))   # reserved

    missing = []
    included = 0
    for pdf_name, ttf_file in FONT_MAP.items():
        ttf_path = fonts_dir / ttf_file
        if not ttf_path.exists():
            missing.append(ttf_file)
            # Write a placeholder font entry: name, 1000 unitsPerEm, 0 glyphs
            name_bytes = pdf_name.encode("ascii").ljust(32, b"\x00")
            out.extend(name_bytes)
            out.extend(struct.pack("<i", 1000))
            out.extend(struct.pack("<i", 0))
            continue
        font = TTFont(str(ttf_path))
        units_per_em = font["head"].unitsPerEm
        name_bytes = pdf_name.encode("ascii").ljust(32, b"\x00")
        out.extend(name_bytes)
        out.extend(struct.pack("<i", units_per_em))

        glyph_set = font.getGlyphSet()
        code_map = build_code_map(font)
        entries = bytearray()
        glyph_count = 0
        for char_code in range(0x20, 0x100):
            glyph_name = code_map.get(char_code)
            if glyph_name is None:
                continue
            commands = extract_glyph(glyph_set, glyph_name, units_per_em)
            if commands is None:
                continue
            glyph_data = pack_glyph(commands)
            entries.extend(struct.pack("<H", char_code))
            entries.extend(struct.pack("<I", len(glyph_data)))
            entries.extend(glyph_data)
            glyph_count += 1
        out.extend(struct.pack("<i", glyph_count))
        out.extend(entries)
        included += 1
        print(f"  {pdf_name}: {glyph_count} glyphs (from {ttf_file})")

    if missing:
        print(f"\nWARNING: {len(missing)} font(s) missing - will fall back to width-only mode:")
        for m in missing:
            print(f"  - {m}")
        print(f"Drop these into {input_dir}/ for full outline support.")

    output_path = pathlib.Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_bytes(bytes(out))
    print(f"\nWrote {output_path} ({len(out)} bytes, {included}/{len(FONT_MAP)} fonts complete)")

if __name__ == "__main__":
    repo_root = pathlib.Path(__file__).parent.parent
    build_bundle(
        repo_root / "tools" / "fonts",
        repo_root / "src" / "Chuvadi.Pdf.Fonts.Rendering" / "Resources" / "Standard14.bin",
    )
