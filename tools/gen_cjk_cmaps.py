#!/usr/bin/env python3
# Copyright 2025 Chuvadi Contributors
# SPDX-License-Identifier: Apache-2.0
#
# 3.35.0 (E1, D-352): builds src/Chuvadi.Pdf.Fonts/Resources/cjk-cmaps.bin,
# the predefined CJK CMaps (PDF 32000-1:2008 §9.7.5.2, Table 118), from one
# pinned release of Adobe's cmap-resources (BSD-3-Clause; notice in
# THIRD-PARTY-NOTICES.md). Run it only to move to another release:
#
#   git clone --depth 1 --branch 20231115 \
#       https://github.com/adobe-type-tools/cmap-resources <dir>
#   python3 tools/gen_cjk_cmaps.py <dir>
#
# The UTF-8 and UTF-32 forms of a Unicode CMap that also has a UTF-16 form
# are left out: the reader decodes their codes to a Unicode scalar and looks
# it up in the UTF-16 table (they map the same characters).
#
# Output, deflated (zlib stream). "v" is an unsigned LEB128 varint, "z" a
# zigzag varint:
#   "CVCM" u8 version=1 v count, then per CMap:
#     v len + ASCII name; v len + ASCII usecmap name (len 0: none);
#     u8 wmode; v n x (u8 bytes, v lo, v hi) codespace ranges;
#     v n x (u8 bytes, z lo - previous lo, v hi - lo, z cid - previous cid)
#     cid ranges (a cidchar is a range of one). Ranges keep file order;
#     later entries win.

import os
import re
import struct
import sys
import zlib

PIN = "20231115"
COLLECTIONS = ["Adobe-CNS1-7", "Adobe-GB1-6", "Adobe-Japan1-7", "Adobe-Korea1-2"]
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Chuvadi.Pdf.Fonts", "Resources", "cjk-cmaps.bin")

TOKEN = re.compile(rb"<([0-9A-Fa-f]*)>|/([^\s/<>\[\]()]+)|(-?\d+)|([A-Za-z][A-Za-z0-9]*)")


def parse(path):
    data = open(path, "rb").read()
    # Drop comments.
    data = re.sub(rb"%[^\r\n]*", b"", data)
    tokens = []
    for m in TOKEN.finditer(data):
        if m.group(1) is not None:
            tokens.append(("hex", m.group(1).decode()))
        elif m.group(2) is not None:
            tokens.append(("name", m.group(2).decode()))
        elif m.group(3) is not None:
            tokens.append(("int", int(m.group(3))))
        else:
            tokens.append(("op", m.group(4).decode()))

    usecmap = ""
    wmode = 0
    codespace = []
    ranges = []
    i = 0
    n = len(tokens)
    while i < n:
        kind, value = tokens[i]
        if kind == "op" and value == "usecmap" and i > 0 and tokens[i - 1][0] == "name":
            usecmap = tokens[i - 1][1]
        elif kind == "name" and value == "WMode" and i + 1 < n and tokens[i + 1][0] == "int":
            wmode = tokens[i + 1][1]
        elif kind == "op" and value == "begincodespacerange":
            i += 1
            while tokens[i] != ("op", "endcodespacerange"):
                lo, hi = tokens[i][1], tokens[i + 1][1]
                codespace.append((len(lo) // 2, int(lo, 16), int(hi, 16)))
                i += 2
        elif kind == "op" and value == "begincidrange":
            i += 1
            while tokens[i] != ("op", "endcidrange"):
                lo, hi, cid = tokens[i][1], tokens[i + 1][1], tokens[i + 2][1]
                ranges.append((len(lo) // 2, int(lo, 16), int(hi, 16), cid))
                i += 3
        elif kind == "op" and value == "begincidchar":
            i += 1
            while tokens[i] != ("op", "endcidchar"):
                code, cid = tokens[i][1], tokens[i + 1][1]
                ranges.append((len(code) // 2, int(code, 16), int(code, 16), cid))
                i += 2
        i += 1
    return usecmap, wmode, codespace, ranges


def varint(v):
    out = bytearray()
    while True:
        b = v & 0x7F
        v >>= 7
        if v:
            out.append(b | 0x80)
        else:
            out.append(b)
            return bytes(out)


def zigzag(v):
    return varint((v << 1) if v >= 0 else ((-v << 1) - 1))


def main():
    if len(sys.argv) != 2:
        print(__doc__ or "usage: gen_cjk_cmaps.py <cmap-resources dir>")
        sys.exit(2)
    root = sys.argv[1]
    entries = []
    for collection in COLLECTIONS:
        folder = os.path.join(root, collection, "CMap")
        for name in sorted(os.listdir(folder)):
            if name.startswith("Adobe-"):
                continue  # CID-to-CID files, not encodings
            derived = re.sub(r"-UTF(8|32)-", "-UTF16-", name)
            if derived != name and os.path.exists(os.path.join(folder, derived)):
                continue  # read through the UTF-16 table
            entries.append((name,) + parse(os.path.join(folder, name)))

    out = bytearray(b"CVCM")
    out += struct.pack("<B", 1) + varint(len(entries))
    for name, usecmap, wmode, codespace, ranges in entries:
        out += varint(len(name)) + name.encode("ascii")
        out += varint(len(usecmap)) + usecmap.encode("ascii")
        out += struct.pack("<B", wmode)
        out += varint(len(codespace))
        for b, lo, hi in codespace:
            out += struct.pack("<B", b) + varint(lo) + varint(hi)
        out += varint(len(ranges))
        prev_lo = 0
        prev_cid = 0
        for b, lo, hi, cid in ranges:
            out += struct.pack("<B", b) + zigzag(lo - prev_lo) + varint(hi - lo) + zigzag(cid - prev_cid)
            prev_lo = lo
            prev_cid = cid

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "wb") as f:
        f.write(zlib.compress(bytes(out), 9))
    print("%d CMaps, %d bytes raw, %d deflated (cmap-resources %s)" % (len(entries), len(out), os.path.getsize(OUT), PIN))


if __name__ == "__main__":
    main()
