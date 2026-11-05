// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: 3.31 (D-169) — the cmap above U+FFFF: formats 12 and 13 read and
//        preferred in the OpenType order, format 14 variation sequences, the
//        sfnt cmap of a CFF OpenType font, and the oracle's test vector.
//        3.31.1 (D-201): only the format 12 or 13 subtable that is chosen is
//        read; the tests below pin which one is chosen in every order.

// Fonts.Bundled is deprecated (3.31.0) but still ships; the vector names its WOFF2 faces.
#pragma warning disable CS0618

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Chuvadi.Fonts;
using Chuvadi.Pdf.Fonts.Bundled;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Text.Shaping;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Rendering.Tests;

public sealed class CmapAboveBmp331Tests
{
    private static readonly string VectorPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Vectors", "fontface-cmap-above-bmp.tsv");

    private static readonly string CffFixture =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "ChuvadiTest-Cff.otf");

    // The rows of the vector: font file, code point, expected glyph.
    private static List<(string Font, int CodePoint, int Glyph)> VectorRows()
    {
        List<(string, int, int)> rows = new List<(string, int, int)>();
        foreach (string line in File.ReadAllLines(VectorPath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] cols = line.Split('\t');
            int cp = int.Parse(cols[2].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            rows.Add((cols[0], cp, int.Parse(cols[3], CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    [Fact]
    public void TheOracleVector_EveryCodePointAboveTheBmp_GetsItsFormat12Glyph()
    {
        List<(string Font, int CodePoint, int Glyph)> rows = VectorRows();
        rows.Should().HaveCount(45, "the vector's '# total 45' line");
        foreach (IGrouping<string, (string Font, int CodePoint, int Glyph)> group in rows.GroupBy(r => r.Font))
        {
            byte[] program = BundledFonts.GetFontProgram(Path.GetFileNameWithoutExtension(group.Key));
            FontFace face = FontFace.Load(program);
            FontRenderer renderer = new FontRenderer(program);
            foreach ((string _, int cp, int glyph) in group)
            {
                string what = group.Key + " U+" + cp.ToString("X4", CultureInfo.InvariantCulture);
                face.GetGlyphIndex(cp).Should().Be(glyph, what);
                face.HasGlyph(cp).Should().BeTrue(what);
                face.CharacterMap[cp].Should().Be(glyph, what);
                renderer.GetGlyphIndex(cp).Should().Be(glyph, what);
                renderer.GetGlyphIndexUnicode(cp).Should().Be(glyph, what);
            }
        }
    }

    [Fact]
    public void TheVector_HoldsForTheStaticRegularFacesOfChuvadiFonts()
    {
        foreach ((string font, int cp, int glyph) in VectorRows())
        {
            string ttf = Path.GetFileNameWithoutExtension(font) + "-Regular.ttf";
            FontFace face = FontFace.Load(FontPack.ReadAllBytes(ttf));
            face.GetGlyphIndex(cp).Should().Be(glyph, ttf + " U+" + cp.ToString("X4", CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void ABmpCodePoint_KeepsItsGlyph_WhenFormat12Wins()
    {
        // The format 4 and format 12 subtables of LiPi Sans agree on the BMP,
        // so preferring format 12 changes no BMP glyph.
        byte[] program = BundledFonts.GetFontProgram("LiPi-Sans-Tamil");
        FontFace face = FontFace.Load(program);
        Dictionary<int, int> f4 = ReadFormat4(program);
        f4.Should().NotBeEmpty();
        foreach (KeyValuePair<int, int> kv in f4)
        {
            face.GetGlyphIndex(kv.Key).Should().Be(kv.Value);
        }
    }

    [Fact]
    public void WhenFormat4AndFormat12Disagree_Format12Wins_WhicheverComesFirst()
    {
        byte[] lipi = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        (int a, int b, int c) = Glyphs(lipi, 'A', 'B', 'C');
        byte[] f4 = Format4(new Dictionary<int, int> { ['A'] = a });
        byte[] f12 = Format12(('A', 'A', b), (0x1F600, 0x1F600, c));

        foreach (byte[] cmap in new[]
        {
            Cmap((3, 1, f4), (3, 10, f12)),
            Cmap((3, 10, f12), (3, 1, f4)),
            Cmap((0, 3, f4), (0, 4, f12)),
            Cmap((3, 1, f4), (0, 4, f12)),
        })
        {
            byte[] font = WithCmap(lipi, cmap);
            FontFace face = FontFace.Load(font);
            face.GetGlyphIndex('A').Should().Be(b);
            face.GetGlyphIndex(0x1F600).Should().Be(c);
            new FontRenderer(font).GetGlyphIndexUnicode(0x1F600).Should().Be(c);
        }

        // With format 4 alone the old behaviour is kept.
        FontFace.Load(WithCmap(lipi, Cmap((3, 1, f4)))).GetGlyphIndex('A').Should().Be(a);
    }

    [Fact]
    public void Format13_MapsAWholeRangeToOneGlyph()
    {
        byte[] lipi = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        (int a, int b, int _) = Glyphs(lipi, 'A', 'B', 'C');
        byte[] font = WithCmap(lipi, Cmap((3, 1, Format4(new Dictionary<int, int> { ['A'] = a })), (3, 10, Format13((0x1F600, 0x1F602, b)))));
        FontFace face = FontFace.Load(font);
        face.GetGlyphIndex(0x1F600).Should().Be(b);
        face.GetGlyphIndex(0x1F601).Should().Be(b);
        face.GetGlyphIndex(0x1F602).Should().Be(b);
        face.GetGlyphIndex(0x1F603).Should().Be(0);
    }

    [Fact]
    public void ADamagedFormat12Subtable_IsSkipped_AndFormat4IsUsed()
    {
        byte[] lipi = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        (int a, int _, int _) = Glyphs(lipi, 'A', 'B', 'C');
        byte[] bad = Format12(('A', 'A', a + 1));
        WriteU32(bad, 12, 0x7FFFFFFF); // numGroups far past the data
        FontFace face = FontFace.Load(WithCmap(lipi, Cmap((3, 1, Format4(new Dictionary<int, int> { ['A'] = a })), (3, 10, bad))));
        face.GetGlyphIndex('A').Should().Be(a);
    }

    [Fact]
    public void AmongFullRepertoireSubtables_WindowsWins_ThenTheFirstInTheDirectory()
    {
        // 3.31.1 reads only the subtable that is chosen; the choice is the 3.31.0 one.
        byte[] lipi = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        (int a, int b, int c) = Glyphs(lipi, 'A', 'B', 'C');
        byte[] f4 = Format4(new Dictionary<int, int> { ['A'] = a });
        byte[] withB = Format12(('A', 'A', b));
        byte[] withC = Format12(('A', 'A', c));

        Pick(Cmap((0, 4, withB), (3, 10, withC))).Should().Be(c, "Windows (3,10) comes before Unicode (0,4)");
        Pick(Cmap((3, 10, withC), (0, 4, withB))).Should().Be(c, "Windows (3,10) comes before Unicode (0,4)");
        Pick(Cmap((3, 1, f4), (0, 6, withB), (0, 4, withC))).Should().Be(b, "among Unicode equals the first wins");
        Pick(Cmap((3, 10, withB), (3, 10, withC))).Should().Be(b, "among Windows equals the first wins");
        Pick(Cmap((0, 4, withB), (3, 10, Format13(('A', 'A', c))))).Should().Be(c, "format 13 ranks with format 12");

        int Pick(byte[] cmap) => FontFace.Load(WithCmap(lipi, cmap)).GetGlyphIndex('A');
    }

    [Fact]
    public void WhenTheBestFullRepertoireSubtableIsDamagedOrEmpty_TheNextOneIsUsed()
    {
        byte[] lipi = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        (int a, int b, int c) = Glyphs(lipi, 'A', 'B', 'C');
        byte[] f4 = Format4(new Dictionary<int, int> { ['A'] = a });
        byte[] damaged = Format12(('A', 'A', c));
        WriteU32(damaged, 12, 0x7FFFFFFF); // numGroups far past the data
        byte[] empty = Format12();
        byte[] withB = Format12(('A', 'A', b));

        Pick(Cmap((3, 1, f4), (3, 10, damaged), (0, 4, withB))).Should().Be(b, "a damaged (3,10) falls back to (0,4)");
        Pick(Cmap((3, 1, f4), (3, 10, empty), (0, 4, withB))).Should().Be(b, "an empty (3,10) falls back to (0,4)");
        Pick(Cmap((3, 1, f4), (3, 10, damaged), (0, 4, empty))).Should().Be(a, "with no usable full-repertoire subtable, format 4 is used");
        Pick(Cmap((3, 10, damaged))).Should().Be(0, "with no usable subtable, nothing is mapped");

        int Pick(byte[] cmap) => FontFace.Load(WithCmap(lipi, cmap)).GetGlyphIndex('A');
    }

    [Fact]
    public void Format14_GivesTheVariantGlyph_AndTheShaperUsesIt()
    {
        byte[] lipi = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        (int a, int b, int c) = Glyphs(lipi, 'A', 'B', 'C');
        byte[] f4 = Format4(new Dictionary<int, int> { ['A'] = a, ['B'] = b, ['C'] = c });
        byte[] f14 = Format14(
            (0xFE00, Default: Array.Empty<(int, int)>(), NonDefault: new[] { ((int)'A', b) }),
            (0xE0100, Default: new[] { ((int)'C', 0) }, NonDefault: Array.Empty<(int, int)>()));
        FontFace face = FontFace.Load(WithCmap(lipi, Cmap((0, 3, f4), (0, 5, f14), (3, 1, f4))));

        face.GetVariantGlyphIndex('A', 0xFE00).Should().Be(b, "a non-default sequence names its glyph");
        face.GetVariantGlyphIndex('C', 0xE0100).Should().Be(c, "a default sequence uses the base glyph");
        face.GetVariantGlyphIndex('A', 0xFE01).Should().Be(-1);
        face.GetVariantGlyphIndex('B', 0xFE00).Should().Be(-1);

        IReadOnlyList<ShapedGlyph> variant = TextShaper.Shape(face, "A\uFE00", LipiScript.Latin);
        variant.Should().ContainSingle().Which.GlyphId.Should().Be(b);
        variant[0].Cluster.Should().Be(0);

        IReadOnlyList<ShapedGlyph> byDefault = TextShaper.Shape(face, "C\uDB40\uDD00", LipiScript.Latin);
        byDefault.Should().ContainSingle().Which.GlyphId.Should().Be(c);

        // A sequence the font does not list keeps the base glyph first.
        TextShaper.Shape(face, "A\uFE01", LipiScript.Latin)[0].GlyphId.Should().Be(a);
    }

    [Fact]
    public void AFontWithoutFormat14_HasNoVariants()
    {
        FontFace face = FontFace.Load(FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf"));
        face.GetVariantGlyphIndex('A', 0xFE00).Should().Be(-1);
    }

    [Fact]
    public void TheShaper_GivesTheLiPiGlyphForASupplementaryCodePoint()
    {
        FontFace face = FontFace.Load(FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf"));
        IReadOnlyList<ShapedGlyph> g = TextShaper.Shape(face, char.ConvertFromUtf32(0x1F130), LipiScript.Latin);
        g.Should().ContainSingle().Which.GlyphId.Should().Be(1964);
    }

    [Fact]
    public void ACffOpenTypeFont_UsesItsSfntCmapFirst()
    {
        byte[] otf = File.ReadAllBytes(CffFixture);
        FontRenderer before = new FontRenderer(otf);
        before.GetGlyphIndexUnicode(0x1F600).Should().Be(0);
        int glyph = before.NumGlyphs - 1;
        glyph.Should().BeGreaterThan(0);

        byte[] patched = WithCmap(otf, Cmap((3, 10, Format12((0x1F600, 0x1F600, glyph)))));
        FontRenderer after = new FontRenderer(patched);
        after.GetGlyphIndexUnicode(0x1F600).Should().Be(glyph);
        after.GetGlyphIndex(0x1F600).Should().Be(glyph);
        string pair = char.ConvertFromUtf32(0x1F600);
        after.LayoutText(pair, 10).Should().ContainSingle("a surrogate pair is one glyph");
        after.MeasureText(pair, 10).Should().Be(after.GetGlyphOutline(glyph).Metrics.AdvanceWidthAt(10));
    }

    // ── font helpers ──────────────────────────────────────────────────────

    private static (int, int, int) Glyphs(byte[] font, char x, char y, char z)
    {
        FontFace face = FontFace.Load(font);
        return (face.GetGlyphIndex(x), face.GetGlyphIndex(y), face.GetGlyphIndex(z));
    }

    // The font's first format 4 subtable, read independently of the loader.
    private static Dictionary<int, int> ReadFormat4(byte[] font)
    {
        (int cmapOffset, int _) = FindTable(font, "cmap");
        int n = U16(font, cmapOffset + 2);
        for (int i = 0; i < n; i++)
        {
            int sub = cmapOffset + (int)U32(font, cmapOffset + 4 + (i * 8) + 4);
            if (U16(font, sub) != 4)
            {
                continue;
            }

            Dictionary<int, int> map = new Dictionary<int, int>();
            int segX2 = U16(font, sub + 6);
            int ends = sub + 14;
            int starts = ends + segX2 + 2;
            int deltas = starts + segX2;
            int ranges = deltas + segX2;
            for (int s = 0; s < segX2 / 2; s++)
            {
                int end = U16(font, ends + (s * 2));
                int start = U16(font, starts + (s * 2));
                int delta = U16(font, deltas + (s * 2));
                int ro = U16(font, ranges + (s * 2));
                for (int cp = start; cp <= end && cp != 0xFFFF; cp++)
                {
                    int g = ro == 0 ? (cp + delta) & 0xFFFF : U16(font, ranges + (s * 2) + ro + ((cp - start) * 2));
                    if (ro != 0 && g != 0)
                    {
                        g = (g + delta) & 0xFFFF;
                    }

                    if (g != 0)
                    {
                        map.TryAdd(cp, g);
                    }
                }
            }

            return map;
        }

        return new Dictionary<int, int>();
    }

    private static byte[] Cmap(params (int Platform, int Encoding, byte[] Subtable)[] subtables)
    {
        using MemoryStream ms = new MemoryStream();
        W16(ms, 0);
        W16(ms, subtables.Length);
        int offset = 4 + (8 * subtables.Length);
        foreach ((int p, int e, byte[] s) in subtables)
        {
            W16(ms, p);
            W16(ms, e);
            W32(ms, (uint)offset);
            offset += s.Length;
        }

        foreach ((int _, int _, byte[] s) in subtables)
        {
            ms.Write(s);
        }

        return ms.ToArray();
    }

    // One segment per code point, plus the closing 0xFFFF segment.
    private static byte[] Format4(Dictionary<int, int> map)
    {
        List<int> cps = map.Keys.OrderBy(k => k).ToList();
        int segCount = cps.Count + 1;
        using MemoryStream ms = new MemoryStream();
        W16(ms, 4);
        W16(ms, 16 + (segCount * 8));
        W16(ms, 0);
        W16(ms, segCount * 2);
        W16(ms, 0);
        W16(ms, 0);
        W16(ms, 0);
        foreach (int cp in cps)
        {
            W16(ms, cp);
        }

        W16(ms, 0xFFFF);
        W16(ms, 0);
        foreach (int cp in cps)
        {
            W16(ms, cp);
        }

        W16(ms, 0xFFFF);
        foreach (int cp in cps)
        {
            W16(ms, (map[cp] - cp) & 0xFFFF);
        }

        W16(ms, 1);
        for (int i = 0; i < segCount; i++)
        {
            W16(ms, 0);
        }

        return ms.ToArray();
    }

    private static byte[] Format12(params (int Start, int End, int Glyph)[] groups) => Groups(12, groups);

    private static byte[] Format13(params (int Start, int End, int Glyph)[] groups) => Groups(13, groups);

    private static byte[] Groups(int format, (int Start, int End, int Glyph)[] groups)
    {
        using MemoryStream ms = new MemoryStream();
        W16(ms, format);
        W16(ms, 0);
        W32(ms, (uint)(16 + (groups.Length * 12)));
        W32(ms, 0);
        W32(ms, (uint)groups.Length);
        foreach ((int s, int e, int g) in groups)
        {
            W32(ms, (uint)s);
            W32(ms, (uint)e);
            W32(ms, (uint)g);
        }

        return ms.ToArray();
    }

    private static byte[] Format14(params (int Selector, (int Start, int Count)[] Default, (int CodePoint, int Glyph)[] NonDefault)[] records)
    {
        using MemoryStream body = new MemoryStream();
        int headerLength = 10 + (records.Length * 11);
        List<(int, uint, uint)> offsets = new List<(int, uint, uint)>();
        foreach ((int sel, (int Start, int Count)[] def, (int CodePoint, int Glyph)[] nd) in records)
        {
            uint defOffset = 0;
            uint ndOffset = 0;
            if (def.Length > 0)
            {
                defOffset = (uint)(headerLength + body.Length);
                W32(body, (uint)def.Length);
                foreach ((int start, int count) in def)
                {
                    W24(body, start);
                    body.WriteByte((byte)count);
                }
            }

            if (nd.Length > 0)
            {
                ndOffset = (uint)(headerLength + body.Length);
                W32(body, (uint)nd.Length);
                foreach ((int cp, int g) in nd)
                {
                    W24(body, cp);
                    W16(body, g);
                }
            }

            offsets.Add((sel, defOffset, ndOffset));
        }

        using MemoryStream ms = new MemoryStream();
        W16(ms, 14);
        W32(ms, (uint)(headerLength + body.Length));
        W32(ms, (uint)records.Length);
        foreach ((int sel, uint d, uint n) in offsets)
        {
            W24(ms, sel);
            W32(ms, d);
            W32(ms, n);
        }

        ms.Write(body.ToArray());
        return ms.ToArray();
    }

    // The font with its cmap table replaced (table checksums are not checked by the loader).
    private static byte[] WithCmap(byte[] font, byte[] cmap)
    {
        int numTables = U16(font, 4);
        List<(uint Tag, uint Sum, byte[] Data)> tables = new List<(uint, uint, byte[])>();
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + (i * 16);
            uint tag = U32(font, rec);
            int off = (int)U32(font, rec + 8);
            int len = (int)U32(font, rec + 12);
            tables.Add((tag, U32(font, rec + 4), tag == 0x636D6170 ? cmap : font.AsSpan(off, len).ToArray()));
        }

        using MemoryStream ms = new MemoryStream();
        ms.Write(font, 0, 12);
        int dataOffset = 12 + (numTables * 16);
        foreach ((uint tag, uint sum, byte[] data) in tables)
        {
            W32(ms, tag);
            W32(ms, sum);
            W32(ms, (uint)dataOffset);
            W32(ms, (uint)data.Length);
            dataOffset += (data.Length + 3) & ~3;
        }

        foreach ((uint _, uint _, byte[] data) in tables)
        {
            ms.Write(data);
            for (int pad = data.Length; (pad & 3) != 0; pad++)
            {
                ms.WriteByte(0);
            }
        }

        return ms.ToArray();
    }

    private static (int Offset, int Length) FindTable(byte[] font, string tag)
    {
        int numTables = U16(font, 4);
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + (i * 16);
            if (System.Text.Encoding.ASCII.GetString(font, rec, 4) == tag)
            {
                return ((int)U32(font, rec + 8), (int)U32(font, rec + 12));
            }
        }

        throw new InvalidOperationException("no " + tag);
    }

    private static int U16(byte[] d, int o) => (d[o] << 8) | d[o + 1];

    private static uint U32(byte[] d, int o) => ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

    private static void WriteU32(byte[] d, int o, uint v)
    {
        d[o] = (byte)(v >> 24);
        d[o + 1] = (byte)(v >> 16);
        d[o + 2] = (byte)(v >> 8);
        d[o + 3] = (byte)v;
    }

    private static void W16(Stream s, int v)
    {
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static void W24(Stream s, int v)
    {
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    private static void W32(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }
}
