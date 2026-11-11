// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.3 (simple TrueType fonts), §9.6.6.4 (encoding)
// PHASE: Phase 3 — PDF/A font embedding
//
// Builds an embeddable sfnt for a simple TrueType font: subsets a source TTF to
// the glyphs reachable from a code-to-Unicode encoding (dropping variation
// tables as a side effect, yielding the font's default instance) and injects a
// (3,1) Unicode cmap so a PDF viewer can resolve character codes to glyphs.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Fonts.Rendering;

namespace Chuvadi.Pdf.Authoring;

/// <summary>The result of building an embeddable simple-font program.</summary>
/// <param name="Sfnt">The subsetted sfnt with an injected Unicode cmap.</param>
/// <param name="GidByCode">Glyph id for each character code (0 = no glyph).</param>
/// <param name="WidthByCode">Advance width per character code, scaled to 1000 units/em.</param>
/// <param name="UnitsPerEm">The font's units-per-em, for scaling descriptor metrics.</param>
internal sealed record EmbeddableFont(byte[] Sfnt, int[] GidByCode, int[] WidthByCode, int UnitsPerEm);

internal static class SimpleFontProgram
{
    /// <summary>
    /// Builds an embeddable simple-font program from <paramref name="sourceTtf"/>,
    /// covering the glyphs reachable from <paramref name="unicodeByCode"/>.
    /// </summary>
    /// <param name="sourceTtf">The source TrueType font (already a static sfnt).</param>
    /// <param name="unicodeByCode">Code-to-Unicode map (index = character code, 0 = unmapped).</param>
    /// <returns>The subsetted program plus the per-code glyph map and units-per-em.</returns>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    internal static EmbeddableFont Build(byte[] sourceTtf, IReadOnlyList<int> unicodeByCode)
        => Build(sourceTtf, unicodeByCode, symbolic: false);

    /// <summary>
    /// Builds an embeddable simple-font program (3.33.0, ticket 46). With
    /// <paramref name="symbolic"/> false the program gets a (3,1) Unicode cmap
    /// (a non-symbolic font, glyphs reached through the encoding's names);
    /// with true it gets one (3,0) Symbol cmap mapping each character code to
    /// its glyph (a symbolic font with no /Encoding, as PDF/A-1 requires for
    /// any encoding other than WinAnsi or MacRoman).
    /// </summary>
    /// <param name="sourceTtf">The source TrueType font (already a static sfnt).</param>
    /// <param name="unicodeByCode">Code-to-Unicode map (index = character code, 0 = unmapped).</param>
    /// <param name="symbolic">Whether to build the symbolic form.</param>
    /// <returns>The subsetted program plus the per-code glyph map and units-per-em.</returns>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    internal static EmbeddableFont Build(byte[] sourceTtf, IReadOnlyList<int> unicodeByCode, bool symbolic)
    {
        ArgumentNullException.ThrowIfNull(sourceTtf);
        ArgumentNullException.ThrowIfNull(unicodeByCode);

        TrueTypeLoader loader = new TrueTypeLoader(sourceTtf);
        int unitsPerEm = loader.UnitsPerEm;
        int[] gidByCode = new int[unicodeByCode.Count];
        int[] widthByCode = new int[unicodeByCode.Count];
        SortedSet<int> usedGlyphs = new SortedSet<int> { 0 };
        Dictionary<int, int> unicodeToGid = new Dictionary<int, int>();

        for (int code = 0; code < unicodeByCode.Count; code++)
        {
            int unicode = unicodeByCode[code];
            if (unicode <= 0)
            {
                gidByCode[code] = 0;
                continue;
            }

            int gid = loader.GetGlyphIndexUnicode(unicode);
            gidByCode[code] = gid;
            if (gid > 0)
            {
                usedGlyphs.Add(gid);
                unicodeToGid[unicode] = gid;
                int advance = loader.GetGlyphMetrics(gid).AdvanceWidth;
                widthByCode[code] = (int)Math.Round(advance * 1000.0 / unitsPerEm);
            }
        }

        byte[] subset = TrueTypeSubsetter.Subset(sourceTtf, usedGlyphs);
        List<SfntAssembler.TableEntry> tables = ParseTables(subset);
        Dictionary<int, int> codeToGid = new Dictionary<int, int>();
        for (int code = 0; code < gidByCode.Length; code++)
        {
            if (gidByCode[code] > 0)
            {
                codeToGid[code] = gidByCode[code];
            }
        }

        byte[] cmap = symbolic
            ? CmapSubtableBuilder.BuildSymbolCmapTable(codeToGid)
            : CmapSubtableBuilder.BuildCmapTable(unicodeToGid);
        tables.Add(new SfntAssembler.TableEntry(TagOf("cmap"), cmap));
        byte[] sfnt = SfntAssembler.Assemble(0x00010000u, tables);

        return new EmbeddableFont(sfnt, gidByCode, widthByCode, unitsPerEm);
    }

    private static List<SfntAssembler.TableEntry> ParseTables(byte[] sfnt)
    {
        List<SfntAssembler.TableEntry> tables = new List<SfntAssembler.TableEntry>();
        int numTables = ReadU16(sfnt, 4);
        for (int i = 0; i < numTables; i++)
        {
            int dir = 12 + i * 16;
            uint tag = ReadU32(sfnt, dir);
            int offset = (int)ReadU32(sfnt, dir + 8);
            int length = (int)ReadU32(sfnt, dir + 12);
            byte[] data = new byte[length];
            Array.Copy(sfnt, offset, data, 0, length);
            tables.Add(new SfntAssembler.TableEntry(tag, data));
        }

        return tables;
    }

    private static uint TagOf(string s)
        => ((uint)s[0] << 24) | ((uint)s[1] << 16) | ((uint)s[2] << 8) | s[3];

    private static int ReadU16(byte[] d, int p) => (d[p] << 8) | d[p + 1];

    private static uint ReadU32(byte[] d, int p)
        => ((uint)d[p] << 24) | ((uint)d[p + 1] << 16) | ((uint)d[p + 2] << 8) | d[p + 3];
}
