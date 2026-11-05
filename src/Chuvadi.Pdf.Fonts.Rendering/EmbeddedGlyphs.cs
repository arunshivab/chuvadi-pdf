// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 — the code-to-glyph rules for embedded fonts, shared by the
//        raster builder and the structured display list (Rule 5 item 28), so
//        a printer and the raster renderer pick the same glyph for a code.
// SPEC:  PDF 32000-1:2008 §9.6.6.4 (TrueType encodings), §9.7.4.2 (CIDToGIDMap).

namespace Chuvadi.Pdf.Fonts.Rendering;

internal static class EmbeddedGlyphs
{
    // Resolves a content-stream byte code to a glyph index in an embedded
    // TrueType/OpenType/CFF program. Non-symbolic fonts with a Unicode cmap go
    // code -> Unicode (via the font's encoding) -> Unicode cmap, preserving
    // standard-encoding behaviour; otherwise the code selects the glyph
    // directly through the symbol/Mac cmap or as a subset glyph index.
    //
    // 3.31 (D-175): for TrueType outlines the character comes from the font's
    // encoding, not its ToUnicode CMap, as PDF 32000-1:2008 §9.6.6.4 says
    // (ToUnicode is for extraction and search). CFF programs keep the 3.30
    // order.
    internal static int ForSimpleCode(FontRenderer renderer, PdfFont? pdfFont, int code, bool symbolic)
    {
        if (!symbolic && pdfFont is not null)
        {
            string decoded = Character(renderer, pdfFont, code);
            if (decoded.Length > 0)
            {
                int gid = renderer.GetGlyphIndexUnicode(FirstCodePoint(decoded));
                if (gid != 0)
                {
                    return gid;
                }
            }
        }

        int byCode = renderer.GetGlyphIndexForCode(code, symbolic);
        if (byCode != 0)
        {
            return byCode;
        }

        // Final attempt: a symbolic font that nonetheless has a usable
        // Unicode mapping via its encoding.
        if (pdfFont is not null)
        {
            string decoded = Character(renderer, pdfFont, code);
            if (decoded.Length > 0)
            {
                int gid = renderer.GetGlyphIndexUnicode(FirstCodePoint(decoded));
                if (gid != 0)
                {
                    return gid;
                }
            }
        }

        return 0;
    }

    // The character that selects the glyph: the encoding's for TrueType
    // outlines (D-175), the decoded text (ToUnicode first) for CFF.
    private static string Character(FontRenderer renderer, PdfFont pdfFont, int code)
    {
        if (renderer.IsCff)
        {
            return pdfFont.DecodeCode(code);
        }

        char c = pdfFont.GetEncodingCharacter(code);
        return c == '\0' ? string.Empty : c.ToString();
    }

    // 3.31 (D-169): the whole first code point, so a surrogate pair maps
    // through the full-repertoire cmap instead of the lone high surrogate.
    private static int FirstCodePoint(string decoded) =>
        decoded.Length > 1 && char.IsSurrogatePair(decoded[0], decoded[1])
            ? char.ConvertToUtf32(decoded[0], decoded[1])
            : decoded[0];

    // A CID's glyph index: through the /CIDToGIDMap stream when there is one,
    // else the CFF charset for a CID-keyed CFF program, else the CID itself.
    internal static int ForCid(FontRenderer? renderer, int[]? cidToGid, int cid) =>
        cidToGid is not null
            ? (cid < cidToGid.Length ? cidToGid[cid] : 0)
            : renderer?.GetGlyphIndexForCid(cid) ?? cid;
}
