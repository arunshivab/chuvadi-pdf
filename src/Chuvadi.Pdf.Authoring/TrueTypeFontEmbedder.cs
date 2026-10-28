// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7.4 — CIDFontType2 (TrueType-based CID fonts)
//        §9.7.6 — Type0 font with Identity-H encoding
//        §9.10.3 — ToUnicode CMaps

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Builds the PDF object graph that embeds a TrueType (<c>glyf</c>) font as a
/// composite Type0 font with Identity-H encoding, so authored content can draw
/// text in a custom font. The font program is subset to the glyphs used (glyph
/// numbering kept); the <c>/W</c> width array and <c>/ToUnicode</c> CMap cover
/// only those glyphs. For CFF-based OpenType fonts and faces of collections,
/// use <see cref="FontEmbedder"/>.
/// </summary>
/// <remarks>
/// The font is referenced from a page's <c>/Font</c> resource by the returned
/// <see cref="EmbeddedFontObjects.Type0FontId"/>. Text drawn with it must be
/// encoded as two-byte big-endian glyph identifiers (the
/// <see cref="ContentStreamWriter"/> handles that). This embeds glyphs in
/// logical order; it does not perform complex-script shaping (GSUB/GPOS or
/// reordering), so Latin renders correctly and Indic renders correctly only for
/// isolated or already-ordered glyphs.
/// </remarks>
public static class TrueTypeFontEmbedder
{
    /// <summary>
    /// Builds the embedded-font objects for <paramref name="ttf"/>.
    /// </summary>
    /// <param name="ttf">The complete TrueType font program (static, glyf-based).</param>
    /// <param name="loader">A loader over the same font, for cmap and widths.</param>
    /// <param name="usedCodepoints">Unicode code points actually drawn with the font.</param>
    /// <param name="baseFont">The PostScript base-font name to record.</param>
    /// <param name="allocateId">Allocates fresh object ids in the owning document.</param>
    /// <param name="extraGlyphs">Raw glyph ids from pre-shaped runs to include in the subset.</param>
    /// <returns>The Type0 font id and every object to add to the document.</returns>
    /// <exception cref="ArgumentNullException">When any reference argument is null.</exception>
    public static EmbeddedFontObjects Build(
        byte[] ttf,
        TrueTypeLoader loader,
        IReadOnlyCollection<int> usedCodepoints,
        string baseFont,
        Func<PdfObjectId> allocateId,
        IReadOnlyCollection<int>? extraGlyphs = null)
    {
        ArgumentNullException.ThrowIfNull(ttf);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(usedCodepoints);
        ArgumentNullException.ThrowIfNull(baseFont);
        ArgumentNullException.ThrowIfNull(allocateId);

        return FontEmbedder.BuildCore(
            ttf,
            isCff: false,
            loader.GetGlyphIndex,
            gid => loader.GetGlyphOutline(gid).Metrics.AdvanceWidth,
            loader.UnitsPerEm,
            usedCodepoints,
            glyphText: null,
            baseFont,
            allocateId,
            extraGlyphs);
    }

    internal static PdfArray BuildWidthArray(SortedDictionary<int, int> widthByGid)
    {
        // Emit each glyph as "gid [width]" — simple and unambiguous.
        List<PdfPrimitive> entries = new List<PdfPrimitive>();
        foreach (KeyValuePair<int, int> pair in widthByGid)
        {
            entries.Add(new PdfInteger(pair.Key));
            entries.Add(new PdfArray(new PdfPrimitive[] { new PdfInteger(pair.Value) }));
        }

        return new PdfArray(entries);
    }

    internal static string BuildToUnicodeCMap(SortedDictionary<int, string> textByGid)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");

        List<KeyValuePair<int, string>> pairs = new List<KeyValuePair<int, string>>(textByGid);
        for (int start = 0; start < pairs.Count; start += 100)
        {
            int count = Math.Min(100, pairs.Count - start);
            sb.Append(count.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");
            for (int i = start; i < start + count; i++)
            {
                sb.Append('<').Append(pairs[i].Key.ToString("X4", CultureInfo.InvariantCulture))
                  .Append("> <").Append(Utf16BeHex(pairs[i].Value)).Append(">\n");
            }

            sb.Append("endbfchar\n");
        }

        sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return sb.ToString();
    }

    // The text as big-endian UTF-16 hex digits (surrogate pairs kept).
    internal static string Utf16BeHex(string text)
    {
        StringBuilder sb = new StringBuilder(text.Length * 4);
        foreach (char ch in text)
        {
            sb.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>Font-wide metrics read from the sfnt tables, scaled to 1000/em.</summary>
    internal readonly struct SfntMetrics
    {
        private SfntMetrics(int flags, int xMin, int yMin, int xMax, int yMax,
            double italicAngle, int ascent, int descent, int capHeight)
        {
            Flags = flags;
            BBoxXMin = xMin;
            BBoxYMin = yMin;
            BBoxXMax = xMax;
            BBoxYMax = yMax;
            ItalicAngle = italicAngle;
            Ascent = ascent;
            Descent = descent;
            CapHeight = capHeight;
        }

        public int Flags { get; }

        public int BBoxXMin { get; }

        public int BBoxYMin { get; }

        public int BBoxXMax { get; }

        public int BBoxYMax { get; }

        public double ItalicAngle { get; }

        public int Ascent { get; }

        public int Descent { get; }

        public int CapHeight { get; }

        public static SfntMetrics Read(byte[] ttf, double scale)
        {
            Dictionary<string, int> tables = ReadTableDirectory(ttf);

            int head = tables.TryGetValue("head", out int h) ? h : -1;
            int hhea = tables.TryGetValue("hhea", out int hh) ? hh : -1;
            int post = tables.TryGetValue("post", out int p) ? p : -1;
            int os2 = tables.TryGetValue("OS/2", out int o) ? o : -1;

            int macStyle = head >= 0 ? U16(ttf, head + 44) : 0;
            int xMin = head >= 0 ? Scale(S16(ttf, head + 36), scale) : 0;
            int yMin = head >= 0 ? Scale(S16(ttf, head + 38), scale) : -200;
            int xMax = head >= 0 ? Scale(S16(ttf, head + 40), scale) : 1000;
            int yMax = head >= 0 ? Scale(S16(ttf, head + 42), scale) : 800;

            int ascent = hhea >= 0 ? Scale(S16(ttf, hhea + 4), scale) : yMax;
            int descent = hhea >= 0 ? Scale(S16(ttf, hhea + 6), scale) : yMin;

            // CapHeight: OS/2 v2+ sCapHeight (offset 88) when present, else ~70% ascent.
            int capHeight = ascent * 7 / 10;
            if (os2 >= 0 && U16(ttf, os2) >= 2)
            {
                int sCap = Scale(S16(ttf, os2 + 88), scale);
                if (sCap != 0)
                {
                    capHeight = sCap;
                }
            }

            // ItalicAngle from post (Fixed 16.16 at offset 4), else 0.
            double italicAngle = post >= 0 ? S32(ttf, post + 4) / 65536.0 : 0.0;

            // Flags: Symbolic (4) for Identity-encoded composite fonts; add Italic (64).
            int flags = 4;
            if ((macStyle & 0x2) != 0 || italicAngle != 0.0)
            {
                flags |= 64;
            }

            return new SfntMetrics(flags, xMin, yMin, xMax, yMax,
                italicAngle, ascent, descent, capHeight);
        }

        private static Dictionary<string, int> ReadTableDirectory(byte[] ttf)
        {
            Dictionary<string, int> tables = new Dictionary<string, int>(StringComparer.Ordinal);
            int numTables = U16(ttf, 4);
            int record = 12;
            for (int i = 0; i < numTables; i++)
            {
                string tag = Encoding.ASCII.GetString(ttf, record, 4);
                int offset = (int)U32(ttf, record + 8);
                tables[tag] = offset;
                record += 16;
            }

            return tables;
        }

        private static int Scale(int fontUnits, double scale) => (int)Math.Round(fontUnits * scale);

        private static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];

        private static int S16(byte[] b, int o)
        {
            int v = U16(b, o);
            return v >= 0x8000 ? v - 0x10000 : v;
        }

        private static uint U32(byte[] b, int o) =>
            ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

        private static int S32(byte[] b, int o) => (int)U32(b, o);
    }
}
