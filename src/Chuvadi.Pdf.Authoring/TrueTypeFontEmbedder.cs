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
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Builds the PDF object graph that embeds a TrueType (<c>glyf</c>) font as a
/// composite Type0 font with Identity-H encoding, so authored content can draw
/// text in a custom font. The whole font program is embedded (no subsetting in
/// this version); the <c>/W</c> width array and <c>/ToUnicode</c> CMap cover
/// only the glyphs actually used.
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

        double scale = 1000.0 / loader.UnitsPerEm;

        // Gather used glyphs: gid -> scaled width, and gid -> code point (for ToUnicode).
        SortedDictionary<int, int> widthByGid = new SortedDictionary<int, int>();
        SortedDictionary<int, int> codepointByGid = new SortedDictionary<int, int>();
        foreach (int codepoint in usedCodepoints)
        {
            int gid = loader.GetGlyphIndex(codepoint);
            if (gid <= 0)
            {
                continue;
            }

            if (!widthByGid.ContainsKey(gid))
            {
                int advance = loader.GetGlyphOutline(gid).Metrics.AdvanceWidth;
                widthByGid[gid] = (int)Math.Round(advance * scale);
                codepointByGid[gid] = codepoint;
            }
        }

        // Pre-shaped glyphs supplied as raw ids (e.g. ligatures not reachable via
        // cmap). They get a width and join the subset, but no ToUnicode entry.
        if (extraGlyphs is not null)
        {
            foreach (int gid in extraGlyphs)
            {
                if (gid <= 0 || widthByGid.ContainsKey(gid))
                {
                    continue;
                }

                int advance = loader.GetGlyphOutline(gid).Metrics.AdvanceWidth;
                widthByGid[gid] = (int)Math.Round(advance * scale);
            }
        }

        SfntMetrics metrics = SfntMetrics.Read(ttf, scale);

        // Subset the font program to the used glyphs (numbering preserved, so the
        // Identity CID-to-GID map and per-CID widths below stay valid). Read
        // descriptor metrics from the original above, since the subset drops
        // OS/2 and other non-rendering tables.
        HashSet<int> usedGlyphs = new HashSet<int> { 0 };
        foreach (int gid in widthByGid.Keys)
        {
            usedGlyphs.Add(gid);
        }

        byte[] fontProgram = TrueTypeSubsetter.Subset(ttf, usedGlyphs);

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        // FontFile2 — the (subsetted) TrueType program, uncompressed.
        PdfObjectId fontFileId = allocateId();
        PdfDictionary fontFileDict = new PdfDictionary();
        fontFileDict.Set(PdfName.Intern("Length1"), fontProgram.Length);
        objects.Add(new PdfIndirectObject(fontFileId, new PdfStream(fontFileDict, fontProgram)));

        // FontDescriptor.
        PdfObjectId descriptorId = allocateId();
        PdfDictionary descriptor = new PdfDictionary();
        descriptor.Set(PdfName.Type, PdfName.Intern("FontDescriptor"));
        descriptor.Set(PdfName.Intern("FontName"), PdfName.Intern(baseFont));
        descriptor.Set(PdfName.Intern("Flags"), metrics.Flags);
        descriptor.Set(PdfName.Intern("FontBBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(metrics.BBoxXMin),
            new PdfInteger(metrics.BBoxYMin),
            new PdfInteger(metrics.BBoxXMax),
            new PdfInteger(metrics.BBoxYMax),
        }));
        descriptor.Set(PdfName.Intern("ItalicAngle"), new PdfReal(metrics.ItalicAngle));
        descriptor.Set(PdfName.Intern("Ascent"), metrics.Ascent);
        descriptor.Set(PdfName.Intern("Descent"), metrics.Descent);
        descriptor.Set(PdfName.Intern("CapHeight"), metrics.CapHeight);
        descriptor.Set(PdfName.Intern("StemV"), 80);
        descriptor.Set(PdfName.Intern("FontFile2"), new PdfReference(fontFileId));
        objects.Add(new PdfIndirectObject(descriptorId, descriptor));

        // Descendant CIDFontType2.
        PdfObjectId cidFontId = allocateId();
        PdfDictionary cidFont = new PdfDictionary();
        cidFont.Set(PdfName.Type, PdfName.Intern("Font"));
        cidFont.Set(PdfName.Intern("Subtype"), PdfName.Intern("CIDFontType2"));
        cidFont.Set(PdfName.Intern("BaseFont"), PdfName.Intern(baseFont));
        PdfDictionary cidSystemInfo = new PdfDictionary();
        cidSystemInfo.Set(PdfName.Intern("Registry"), new PdfString("Adobe"));
        cidSystemInfo.Set(PdfName.Intern("Ordering"), new PdfString("Identity"));
        cidSystemInfo.Set(PdfName.Intern("Supplement"), 0);
        cidFont.Set(PdfName.Intern("CIDSystemInfo"), cidSystemInfo);
        cidFont.Set(PdfName.Intern("FontDescriptor"), new PdfReference(descriptorId));
        cidFont.Set(PdfName.Intern("CIDToGIDMap"), PdfName.Intern("Identity"));
        cidFont.Set(PdfName.Intern("DW"), 1000);
        cidFont.Set(PdfName.Intern("W"), BuildWidthArray(widthByGid));
        objects.Add(new PdfIndirectObject(cidFontId, cidFont));

        // ToUnicode CMap.
        PdfObjectId toUnicodeId = allocateId();
        byte[] cmap = Encoding.ASCII.GetBytes(BuildToUnicodeCMap(codepointByGid));
        PdfDictionary toUnicodeDict = new PdfDictionary();
        toUnicodeDict.Set(PdfName.Length, cmap.Length);
        objects.Add(new PdfIndirectObject(toUnicodeId, new PdfStream(toUnicodeDict, cmap)));

        // Top-level Type0 font.
        PdfObjectId type0Id = allocateId();
        PdfDictionary type0 = new PdfDictionary();
        type0.Set(PdfName.Type, PdfName.Intern("Font"));
        type0.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type0"));
        type0.Set(PdfName.Intern("BaseFont"), PdfName.Intern(baseFont));
        type0.Set(PdfName.Intern("Encoding"), PdfName.Intern("Identity-H"));
        type0.Set(PdfName.Intern("DescendantFonts"),
            new PdfArray(new PdfPrimitive[] { new PdfReference(cidFontId) }));
        type0.Set(PdfName.Intern("ToUnicode"), new PdfReference(toUnicodeId));
        objects.Add(new PdfIndirectObject(type0Id, type0));

        return new EmbeddedFontObjects(type0Id, objects);
    }

    private static PdfArray BuildWidthArray(SortedDictionary<int, int> widthByGid)
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

    private static string BuildToUnicodeCMap(SortedDictionary<int, int> codepointByGid)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");

        List<KeyValuePair<int, int>> pairs = new List<KeyValuePair<int, int>>(codepointByGid);
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

    private static string Utf16BeHex(int codepoint)
    {
        if (codepoint <= 0xFFFF)
        {
            return codepoint.ToString("X4", CultureInfo.InvariantCulture);
        }

        int v = codepoint - 0x10000;
        int high = 0xD800 + (v >> 10);
        int low = 0xDC00 + (v & 0x3FF);
        return high.ToString("X4", CultureInfo.InvariantCulture) +
            low.ToString("X4", CultureInfo.InvariantCulture);
    }

    /// <summary>Font-wide metrics read from the sfnt tables, scaled to 1000/em.</summary>
    private readonly struct SfntMetrics
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
