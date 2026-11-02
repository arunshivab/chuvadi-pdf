// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType specification — Table Directory, cmap (formats 4 and 12), head
//        https://docs.microsoft.com/typography/opentype/spec/otff
//        https://docs.microsoft.com/typography/opentype/spec/cmap
//        https://docs.microsoft.com/typography/opentype/spec/head
// PHASE: Phase 2.1 — v2.1.5 (cmap remap), v2.1.6 (extraction)
//        Rewrites a TrueType font program's cmap table so that glyph
//        indices reachable through legacy code points (e.g. Wingdings glyph
//        at code 0xFC) are ALSO reachable at the semantic Unicode code
//        points declared by a PDF ToUnicode CMap (e.g. U+2713 -> the same
//        glyph). Without this, browsers asked to render U+2713 fall back
//        to a generic checkmark from a system font instead of using the
//        embedded Wingdings glyph.
//
//        v2.1.6: cmap construction and SFNT serialisation were extracted to
//        CmapSubtableBuilder and SfntAssembler so the CFF-to-OpenType wrapper
//        can reuse them. This file now orchestrates those two helpers; the
//        emitted bytes are unchanged from v2.1.5.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Rewrites the cmap table of an embedded TrueType font program so the
/// browser can locate the embedded glyph by its semantic Unicode code
/// point rather than the font's legacy encoding code point.
/// </summary>
/// <remarks>
/// <para>
/// PDF symbol fonts (Wingdings, Symbol, Webdings, ZapfDingbats, MT Extra,
/// and similar) carry glyphs at legacy Windows-symbol code points — for
/// example, the Wingdings checkmark glyph is reachable at character code
/// 0xFC in the original Windows encoding. PDF readers do not use the
/// font's cmap; they address glyphs directly by glyph index (CID). When
/// such a PDF is exported to SVG and the embedded font is placed in a
/// <c>@font-face</c> rule, the browser DOES use the cmap table and asks
/// for the glyph at the semantic Unicode code point (U+2713 for the
/// checkmark). Since no entry exists in the font's cmap at U+2713, the
/// browser falls back to a system font and renders the wrong glyph.
/// </para>
/// <para>
/// The fix is to add a new cmap subtable mapping each semantic Unicode
/// code point (from the ToUnicode CMap) to the corresponding glyph index.
/// This implementation replaces the cmap table entirely with a fresh one
/// containing a single Windows Unicode subtable — format 4 (BMP only) or
/// format 12 (full Unicode range). The original cmap subtables are not
/// preserved; the embedded font is used only by browser rendering of the
/// SVG, where the new mappings are sufficient.
/// </para>
/// <para>
/// This approach mirrors pdf2htmlEX's font remapping strategy. The
/// alternative of mutating the original cmap in place would require
/// parsing every cmap subtable format the source font might use; replacing
/// the table is simpler and equally effective for the SVG use case.
/// </para>
/// </remarks>
public static class TrueTypeFontPatch
{
    /// <summary>
    /// Returns a copy of <paramref name="fontBytes"/> whose cmap table has
    /// been replaced by a fresh table containing the given Unicode-to-glyph
    /// mappings. The original cmap subtables are discarded.
    /// </summary>
    /// <param name="fontBytes">Original TrueType/OpenType font program bytes.</param>
    /// <param name="unicodeToGid">
    /// Map from Unicode code point to glyph index. Code points outside
    /// the BMP (>= 0x10000) trigger a format-12 cmap subtable; if all code
    /// points are in the BMP the result uses format 4.
    /// </param>
    /// <returns>New font program bytes with the cmap table replaced.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fontBytes"/> or <paramref name="unicodeToGid"/> is null.
    /// </exception>
    /// <exception cref="FontRenderingException">
    /// Thrown when the input bytes are not a valid TrueType/OpenType font
    /// or are too short to contain an offset table.
    /// </exception>
    public static byte[] WithAugmentedCmap(
        byte[] fontBytes,
        IReadOnlyDictionary<int, int> unicodeToGid)
    {
        ArgumentNullException.ThrowIfNull(fontBytes);
        ArgumentNullException.ThrowIfNull(unicodeToGid);

        if (fontBytes.Length < 12)
        {
            throw new FontRenderingException(
                "Font data too short to contain an offset table.");
        }

        uint sfVersion = SfntAssembler.ReadUInt32BE(fontBytes, 0);
        if (sfVersion != 0x00010000 && sfVersion != 0x4F54544F
            && sfVersion != 0x74727565 && sfVersion != 0x74797031)
        {
            throw new FontRenderingException(
                $"Not a valid TrueType/OpenType font. sfVersion = 0x{sfVersion:X8}.");
        }

        int numTables = SfntAssembler.ReadUInt16BE(fontBytes, 4);
        if (12 + numTables * 16 > fontBytes.Length)
        {
            throw new FontRenderingException("Truncated table directory.");
        }

        // Collect existing tables, then replace cmap (or insert one if absent).
        List<SfntAssembler.TableEntry> tables = ReadTableDirectory(fontBytes, numTables);

        // Build the fresh cmap from the mappings (filtering and sorting happen
        // inside the builder).
        byte[] newCmap = CmapSubtableBuilder.BuildCmapTable(unicodeToGid);

        // Replace cmap entry; add it if missing.
        int cmapIdx = tables.FindIndex(t => t.Tag == 0x636D6170u);
        SfntAssembler.TableEntry cmapEntry = new(0x636D6170u, newCmap);
        if (cmapIdx >= 0)
        {
            tables[cmapIdx] = cmapEntry;
        }
        else
        {
            tables.Add(cmapEntry);
        }

        return SfntAssembler.Assemble(sfVersion, tables);
    }

    /// <summary>
    /// Returns a copy of <paramref name="fontBytes"/> with minimal
    /// <c>OS/2</c>, <c>name</c> and <c>post</c> tables added where the font
    /// has none (since 3.30). PDF font subsets often carry only the tables a
    /// PDF reader needs; web browsers (their OpenType sanitiser) reject a
    /// font without these three and draw the text in a fallback font
    /// instead. Tables the font already has are kept unchanged.
    /// </summary>
    /// <param name="fontBytes">A TrueType or OpenType font program.</param>
    /// <param name="postScriptName">The name recorded in the added <c>name</c> table.</param>
    /// <param name="isBold">Whether the added tables describe a bold face.</param>
    /// <param name="isItalic">Whether the added tables describe an italic face.</param>
    /// <returns>
    /// The font with the three tables present, re-assembled with every table
    /// on a 4-byte boundary (browsers also reject misaligned tables).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="fontBytes"/> or <paramref name="postScriptName"/> is null.
    /// </exception>
    /// <exception cref="FontRenderingException">
    /// The bytes are not a valid TrueType or OpenType font.
    /// </exception>
    public static byte[] WithRequiredTables(byte[] fontBytes, string postScriptName, bool isBold, bool isItalic)
    {
        ArgumentNullException.ThrowIfNull(fontBytes);
        ArgumentNullException.ThrowIfNull(postScriptName);
        if (fontBytes.Length < 12)
        {
            throw new FontRenderingException("Font data too short to contain an offset table.");
        }

        uint sfVersion = SfntAssembler.ReadUInt32BE(fontBytes, 0);
        if (sfVersion != 0x00010000 && sfVersion != 0x4F54544F && sfVersion != 0x74727565)
        {
            throw new FontRenderingException($"Not a valid TrueType/OpenType font. sfVersion = 0x{sfVersion:X8}.");
        }

        int numTables = SfntAssembler.ReadUInt16BE(fontBytes, 4);
        if (12 + (numTables * 16) > fontBytes.Length)
        {
            throw new FontRenderingException("Truncated table directory.");
        }

        List<SfntAssembler.TableEntry> tables = ReadTableDirectory(fontBytes, numTables);
        bool hasOs2 = tables.Exists(t => t.Tag == Os2Tag);
        bool hasName = tables.Exists(t => t.Tag == NameTag);
        bool hasPost = tables.Exists(t => t.Tag == PostTag);

        // Metrics for OS/2 from head (units per em, glyph box) and hhea/hmtx
        // (average advance); sensible defaults when they are missing.
        SfntAssembler.TableEntry? head = tables.Find(t => t.Tag == HeadTag);
        int unitsPerEm = 1000;
        int yMin = -200;
        int yMax = 800;
        if (head is not null && head.Data.Length >= 54)
        {
            unitsPerEm = Math.Max(16, SfntAssembler.ReadUInt16BE(head.Data, 18));
            yMin = (short)SfntAssembler.ReadUInt16BE(head.Data, 38);
            yMax = (short)SfntAssembler.ReadUInt16BE(head.Data, 42);
        }

        if (!hasOs2)
        {
            tables.Add(new SfntAssembler.TableEntry(
                Os2Tag,
                OpenTypeFontBuilder.BuildOs2(unitsPerEm, yMin, yMax, 0x20, 0xFFFF, isBold, isItalic, AverageAdvance(tables, unitsPerEm))));
        }

        if (!hasName)
        {
            tables.Add(new SfntAssembler.TableEntry(NameTag, OpenTypeFontBuilder.BuildName(postScriptName, isBold, isItalic)));
        }

        if (!hasPost)
        {
            tables.Add(new SfntAssembler.TableEntry(PostTag, OpenTypeFontBuilder.BuildPost()));
        }

        return SfntAssembler.Assemble(sfVersion, tables);
    }

    private const uint Os2Tag = 0x4F532F32u;
    private const uint NameTag = 0x6E616D65u;
    private const uint PostTag = 0x706F7374u;
    private const uint HeadTag = 0x68656164u;
    private const uint HheaTag = 0x68686561u;
    private const uint HmtxTag = 0x686D7478u;

    // The mean of the non-zero advances in hmtx, or half an em.
    private static int AverageAdvance(List<SfntAssembler.TableEntry> tables, int unitsPerEm)
    {
        SfntAssembler.TableEntry? hhea = tables.Find(t => t.Tag == HheaTag);
        SfntAssembler.TableEntry? hmtx = tables.Find(t => t.Tag == HmtxTag);
        if (hhea is null || hmtx is null || hhea.Data.Length < 36)
        {
            return unitsPerEm / 2;
        }

        int count = Math.Min(SfntAssembler.ReadUInt16BE(hhea.Data, 34), hmtx.Data.Length / 4);
        long sum = 0;
        int used = 0;
        for (int i = 0; i < count; i++)
        {
            int advance = SfntAssembler.ReadUInt16BE(hmtx.Data, i * 4);
            if (advance > 0)
            {
                sum += advance;
                used++;
            }
        }

        return used > 0 ? (int)Math.Min(short.MaxValue, sum / used) : unitsPerEm / 2;
    }

    // ── Table directory ──────────────────────────────────────────────────

    private static List<SfntAssembler.TableEntry> ReadTableDirectory(byte[] data, int numTables)
    {
        List<SfntAssembler.TableEntry> tables = new(numTables);
        for (int i = 0; i < numTables; i++)
        {
            int entryOffset = 12 + i * 16;
            uint tag = SfntAssembler.ReadUInt32BE(data, entryOffset);
            uint offset = SfntAssembler.ReadUInt32BE(data, entryOffset + 8);
            uint length = SfntAssembler.ReadUInt32BE(data, entryOffset + 12);

            if (offset > data.Length || offset + length > data.Length)
            {
                throw new FontRenderingException(
                    $"Table {TagToString(tag)} extends past end of font.");
            }

            byte[] tableData = new byte[length];
            Array.Copy(data, (int)offset, tableData, 0, (int)length);
            tables.Add(new SfntAssembler.TableEntry(tag, tableData));
        }

        return tables;
    }

    private static string TagToString(uint tag)
    {
        return new string(new[]
        {
            (char)((tag >> 24) & 0xFF),
            (char)((tag >> 16) & 0xFF),
            (char)((tag >> 8) & 0xFF),
            (char)(tag & 0xFF),
        });
    }
}
