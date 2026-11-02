// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType specification — OTTO (CFF) SFNT envelope, tables
//        CFF, cmap, head, hhea, hmtx, maxp, name, OS/2, post
//        https://docs.microsoft.com/typography/opentype/spec/otff
// PHASE: Phase 2.1 — v2.1.6
//        Wraps a raw CFF font program (PDF /FontFile3, subtype Type1C or
//        CIDFontType0C) in an OpenType (OTTO) SFNT envelope so browsers will
//        accept it via @font-face. Browsers cannot consume a bare CFF program;
//        without this wrapper such fonts silently fall back to a system font.
//        Reuses CmapSubtableBuilder and SfntAssembler (extracted in v2.1.6).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Wraps a raw CFF font program in an OpenType (OTTO) SFNT envelope with the
/// synthesised tables a browser requires (<c>CFF </c>, <c>cmap</c>, <c>head</c>,
/// <c>hhea</c>, <c>hmtx</c>, <c>maxp</c>, <c>name</c>, <c>OS/2</c>, <c>post</c>),
/// so the font can be embedded in an SVG <c>@font-face</c> rule and located by
/// semantic Unicode code point.
/// </summary>
/// <remarks>
/// The raw CFF program is passed through unchanged as the <c>CFF </c> table.
/// Glyph metrics for <c>hmtx</c>, <c>head</c>, <c>hhea</c>, and <c>OS/2</c> are
/// read from <see cref="CffLoader"/>. Created/modified timestamps are fixed at
/// zero so the output is deterministic.
/// </remarks>
public static class OpenTypeFontBuilder
{
    private const uint OttoSfntVersion = 0x4F54544Fu;

    /// <summary>
    /// Builds an OpenType (OTTO) font program wrapping <paramref name="cffProgram"/>.
    /// </summary>
    /// <param name="cffProgram">The raw CFF program bytes (the PDF /FontFile3 stream).</param>
    /// <param name="loader">A loader over the same CFF program, used for glyph metrics.</param>
    /// <param name="unicodeToGid">Map from Unicode code point to glyph index for the cmap.</param>
    /// <param name="postScriptName">The font's PostScript name (subset prefix already stripped).</param>
    /// <param name="isBold">Whether the font should be flagged bold.</param>
    /// <param name="isItalic">Whether the font should be flagged italic.</param>
    /// <returns>The assembled OpenType font program bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is null.</exception>
    public static byte[] Build(
        byte[] cffProgram,
        CffLoader loader,
        IReadOnlyDictionary<int, int> unicodeToGid,
        string postScriptName,
        bool isBold,
        bool isItalic)
    {
        ArgumentNullException.ThrowIfNull(cffProgram);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(unicodeToGid);
        ArgumentNullException.ThrowIfNull(postScriptName);

        int unitsPerEm = loader.UnitsPerEm > 0 ? loader.UnitsPerEm : 1000;
        int numGlyphs = loader.NumGlyphs > 0 ? loader.NumGlyphs : 1;

        int[] advances = new int[numGlyphs];
        int[] lsbs = new int[numGlyphs];
        bool anyBounds = false;
        double xMin = double.MaxValue;
        double yMin = double.MaxValue;
        double xMax = double.MinValue;
        double yMax = double.MinValue;
        int advanceWidthMax = 0;

        for (int gid = 0; gid < numGlyphs; gid++)
        {
            int advance = 0;
            RectangleF bounds = RectangleF.Zero;
            try
            {
                GlyphMetrics metrics = loader.GetGlyphMetrics(gid);
                advance = metrics.AdvanceWidth;
                bounds = metrics.Bounds;
            }
            catch (FontRenderingException)
            {
                // A single uninterpretable glyph should not fail the whole font.
                advance = 0;
                bounds = RectangleF.Zero;
            }

            if (advance < 0) { advance = 0; }
            if (advance > 0xFFFF) { advance = 0xFFFF; }
            advances[gid] = advance;
            if (advance > advanceWidthMax) { advanceWidthMax = advance; }

            if (!bounds.IsEmpty)
            {
                anyBounds = true;
                if (bounds.X < xMin) { xMin = bounds.X; }
                if (bounds.Y < yMin) { yMin = bounds.Y; }
                if (bounds.Right > xMax) { xMax = bounds.Right; }
                if (bounds.Top > yMax) { yMax = bounds.Top; }
                lsbs[gid] = ClampInt16(bounds.X);
            }
            else
            {
                lsbs[gid] = 0;
            }
        }

        if (!anyBounds)
        {
            xMin = 0;
            yMin = -(unitsPerEm / 5.0);
            xMax = unitsPerEm;
            yMax = unitsPerEm * 0.8;
        }

        int bboxXMin = ClampInt16(xMin);
        int bboxYMin = ClampInt16(yMin);
        int bboxXMax = ClampInt16(xMax);
        int bboxYMax = ClampInt16(yMax);

        int firstChar = 0xFFFF;
        int lastChar = 0;
        bool anyCodePoint = false;
        foreach (KeyValuePair<int, int> kv in unicodeToGid)
        {
            int cp = kv.Key;
            if (cp < 0 || cp > 0xFFFF) { continue; }
            anyCodePoint = true;
            if (cp < firstChar) { firstChar = cp; }
            if (cp > lastChar) { lastChar = cp; }
        }

        if (!anyCodePoint)
        {
            firstChar = 0;
            lastChar = 0;
        }

        int advanceSum = 0;
        int advanceCount = 0;
        for (int i = 0; i < numGlyphs; i++)
        {
            if (advances[i] > 0)
            {
                advanceSum += advances[i];
                advanceCount++;
            }
        }

        int avgAdvance = advanceCount > 0 ? advanceSum / advanceCount : 0;

        List<SfntAssembler.TableEntry> tables = new(9);
        tables.Add(new SfntAssembler.TableEntry(0x43464620u, cffProgram));
        tables.Add(new SfntAssembler.TableEntry(0x636D6170u, CmapSubtableBuilder.BuildCmapTable(unicodeToGid)));
        tables.Add(new SfntAssembler.TableEntry(0x68656164u, BuildHead(unitsPerEm, bboxXMin, bboxYMin, bboxXMax, bboxYMax, isBold, isItalic)));
        tables.Add(new SfntAssembler.TableEntry(0x68686561u, BuildHhea(bboxYMax, bboxYMin, advanceWidthMax, MinValue(lsbs), bboxXMax, numGlyphs)));
        tables.Add(new SfntAssembler.TableEntry(0x686D7478u, BuildHmtx(advances, lsbs)));
        tables.Add(new SfntAssembler.TableEntry(0x6D617870u, BuildMaxp(numGlyphs)));
        tables.Add(new SfntAssembler.TableEntry(0x6E616D65u, BuildName(postScriptName, isBold, isItalic)));
        tables.Add(new SfntAssembler.TableEntry(0x4F532F32u, BuildOs2(unitsPerEm, bboxYMin, bboxYMax, firstChar, lastChar, isBold, isItalic, avgAdvance)));
        tables.Add(new SfntAssembler.TableEntry(0x706F7374u, BuildPost()));

        return SfntAssembler.Assemble(OttoSfntVersion, tables);
    }

    private static byte[] BuildHead(int unitsPerEm, int xMin, int yMin, int xMax, int yMax, bool isBold, bool isItalic)
    {
        using MemoryStream ms = new();
        WriteU16(ms, 1);                 // majorVersion
        WriteU16(ms, 0);                 // minorVersion
        WriteU32(ms, 0x00010000u);       // fontRevision 1.0
        WriteU32(ms, 0u);                // checkSumAdjustment (filled by SfntAssembler)
        WriteU32(ms, 0x5F0F3CF5u);       // magicNumber
        WriteU16(ms, 0x0003);            // flags: baseline at y=0, lsb at x=0
        WriteU16(ms, unitsPerEm);
        for (int i = 0; i < 8; i++) { ms.WriteByte(0); }  // created
        for (int i = 0; i < 8; i++) { ms.WriteByte(0); }  // modified
        WriteI16(ms, xMin);
        WriteI16(ms, yMin);
        WriteI16(ms, xMax);
        WriteI16(ms, yMax);
        int macStyle = (isBold ? 1 : 0) | (isItalic ? 2 : 0);
        WriteU16(ms, macStyle);
        WriteU16(ms, 8);                 // lowestRecPPEM
        WriteI16(ms, 2);                 // fontDirectionHint
        WriteI16(ms, 0);                 // indexToLocFormat
        WriteI16(ms, 0);                 // glyphDataFormat
        return ms.ToArray();
    }

    private static byte[] BuildHhea(int ascender, int descender, int advanceWidthMax, int minLsb, int xMaxExtent, int numberOfHMetrics)
    {
        using MemoryStream ms = new();
        WriteU32(ms, 0x00010000u);       // version 1.0
        WriteI16(ms, ascender);
        WriteI16(ms, descender);
        WriteI16(ms, 0);                 // lineGap
        WriteU16(ms, advanceWidthMax);
        WriteI16(ms, minLsb);            // minLeftSideBearing
        WriteI16(ms, 0);                 // minRightSideBearing
        WriteI16(ms, xMaxExtent);
        WriteI16(ms, 1);                 // caretSlopeRise
        WriteI16(ms, 0);                 // caretSlopeRun
        WriteI16(ms, 0);                 // caretOffset
        WriteI16(ms, 0);
        WriteI16(ms, 0);
        WriteI16(ms, 0);
        WriteI16(ms, 0);                 // 4x reserved
        WriteI16(ms, 0);                 // metricDataFormat
        WriteU16(ms, numberOfHMetrics);
        return ms.ToArray();
    }

    private static byte[] BuildHmtx(int[] advances, int[] lsbs)
    {
        using MemoryStream ms = new();
        for (int i = 0; i < advances.Length; i++)
        {
            WriteU16(ms, advances[i]);
            WriteI16(ms, lsbs[i]);
        }
        return ms.ToArray();
    }

    private static byte[] BuildMaxp(int numGlyphs)
    {
        using MemoryStream ms = new();
        WriteU32(ms, 0x00005000u);       // version 0.5 (CFF outlines)
        WriteU16(ms, numGlyphs);
        return ms.ToArray();
    }

    internal static byte[] BuildName(string postScriptName, bool isBold, bool isItalic)
    {
        string psName = SanitizePostScriptName(postScriptName);
        string subfamily = (isBold && isItalic) ? "Bold Italic" : isBold ? "Bold" : isItalic ? "Italic" : "Regular";
        string family = psName;
        string full = family + " " + subfamily;

        (int Id, string Value)[] records =
        {
            (1, family),
            (2, subfamily),
            (3, psName),
            (4, full),
            (5, "Version 1.0"),
            (6, psName),
        };

        using MemoryStream storage = new();
        using MemoryStream recordTable = new();
        int offset = 0;
        foreach ((int id, string value) in records)
        {
            byte[] encoded = Encoding.BigEndianUnicode.GetBytes(value);
            WriteU16(recordTable, 3);        // platformID = Windows
            WriteU16(recordTable, 1);        // encodingID = Unicode BMP
            WriteU16(recordTable, 0x0409);   // languageID = en-US
            WriteU16(recordTable, id);
            WriteU16(recordTable, encoded.Length);
            WriteU16(recordTable, offset);
            storage.Write(encoded, 0, encoded.Length);
            offset += encoded.Length;
        }

        int stringOffset = 6 + records.Length * 12;
        using MemoryStream ms = new();
        WriteU16(ms, 0);                     // format 0
        WriteU16(ms, records.Length);        // count
        WriteU16(ms, stringOffset);
        byte[] recordBytes = recordTable.ToArray();
        byte[] storageBytes = storage.ToArray();
        ms.Write(recordBytes, 0, recordBytes.Length);
        ms.Write(storageBytes, 0, storageBytes.Length);
        return ms.ToArray();
    }

    internal static byte[] BuildOs2(int unitsPerEm, int yMin, int yMax, int firstChar, int lastChar, bool isBold, bool isItalic, int avgAdvance)
    {
        using MemoryStream ms = new();
        WriteU16(ms, 4);                                 // version
        WriteI16(ms, avgAdvance);                        // xAvgCharWidth
        WriteU16(ms, isBold ? 700 : 400);                // usWeightClass
        WriteU16(ms, 5);                                 // usWidthClass = medium
        WriteU16(ms, 0);                                 // fsType = installable
        WriteI16(ms, EmRel(unitsPerEm, 0.65));           // ySubscriptXSize
        WriteI16(ms, EmRel(unitsPerEm, 0.70));           // ySubscriptYSize
        WriteI16(ms, 0);                                 // ySubscriptXOffset
        WriteI16(ms, EmRel(unitsPerEm, 0.14));           // ySubscriptYOffset
        WriteI16(ms, EmRel(unitsPerEm, 0.65));           // ySuperscriptXSize
        WriteI16(ms, EmRel(unitsPerEm, 0.70));           // ySuperscriptYSize
        WriteI16(ms, 0);                                 // ySuperscriptXOffset
        WriteI16(ms, EmRel(unitsPerEm, 0.48));           // ySuperscriptYOffset
        WriteI16(ms, EmRel(unitsPerEm, 0.05));           // yStrikeoutSize
        WriteI16(ms, EmRel(unitsPerEm, 0.26));           // yStrikeoutPosition
        WriteI16(ms, 0);                                 // sFamilyClass
        for (int i = 0; i < 10; i++) { ms.WriteByte(0); } // panose
        WriteU32(ms, 1u);                                // ulUnicodeRange1 (bit0 Basic Latin)
        WriteU32(ms, 0u);
        WriteU32(ms, 0u);
        WriteU32(ms, 0u);
        ms.WriteByte((byte)'C');                         // achVendID
        ms.WriteByte((byte)'H');
        ms.WriteByte((byte)'V');
        ms.WriteByte((byte)'D');
        int fsSelection = 0;
        if (isItalic) { fsSelection |= 0x01; }
        if (isBold) { fsSelection |= 0x20; }
        if (!isItalic && !isBold) { fsSelection |= 0x40; }
        WriteU16(ms, fsSelection);
        WriteU16(ms, firstChar);
        WriteU16(ms, lastChar);
        WriteI16(ms, yMax);                              // sTypoAscender
        WriteI16(ms, yMin);                              // sTypoDescender
        WriteI16(ms, 0);                                 // sTypoLineGap
        WriteU16(ms, yMax > 0 ? yMax : 0);               // usWinAscent
        WriteU16(ms, yMin < 0 ? -yMin : 0);              // usWinDescent
        WriteU32(ms, 1u);                                // ulCodePageRange1 (Latin 1)
        WriteU32(ms, 0u);                                // ulCodePageRange2
        WriteI16(ms, 0);                                 // sxHeight
        WriteI16(ms, 0);                                 // sCapHeight
        WriteU16(ms, 0);                                 // usDefaultChar
        WriteU16(ms, 0x20);                              // usBreakChar
        WriteU16(ms, 0);                                 // usMaxContext
        return ms.ToArray();
    }

    internal static byte[] BuildPost()
    {
        using MemoryStream ms = new();
        WriteU32(ms, 0x00030000u);       // version 3.0 (no glyph names)
        WriteU32(ms, 0u);                // italicAngle
        WriteI16(ms, -100);              // underlinePosition
        WriteI16(ms, 50);                // underlineThickness
        WriteU32(ms, 0u);                // isFixedPitch
        WriteU32(ms, 0u);                // minMemType42
        WriteU32(ms, 0u);                // maxMemType42
        WriteU32(ms, 0u);                // minMemType1
        WriteU32(ms, 0u);                // maxMemType1
        return ms.ToArray();
    }

    private static string SanitizePostScriptName(string name)
    {
        StringBuilder sb = new(name.Length);
        foreach (char ch in name)
        {
            if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-')
            {
                sb.Append(ch);
            }
        }

        if (sb.Length == 0)
        {
            return "Font";
        }

        if (sb.Length > 63)
        {
            sb.Length = 63;
        }

        return sb.ToString();
    }

    private static int EmRel(int unitsPerEm, double fraction)
    {
        return ClampInt16(unitsPerEm * fraction);
    }

    private static int ClampInt16(double value)
    {
        long rounded = (long)Math.Round(value);
        if (rounded < short.MinValue) { return short.MinValue; }
        if (rounded > short.MaxValue) { return short.MaxValue; }
        return (int)rounded;
    }

    private static int MinValue(int[] values)
    {
        int min = 0;
        bool first = true;
        foreach (int v in values)
        {
            if (first || v < min)
            {
                min = v;
                first = false;
            }
        }
        return min;
    }

    private static void WriteU16(MemoryStream ms, int value)
    {
        ms.WriteByte((byte)((value >> 8) & 0xFF));
        ms.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteI16(MemoryStream ms, int value)
    {
        WriteU16(ms, value & 0xFFFF);
    }

    private static void WriteU32(MemoryStream ms, uint value)
    {
        ms.WriteByte((byte)((value >> 24) & 0xFF));
        ms.WriteByte((byte)((value >> 16) & 0xFF));
        ms.WriteByte((byte)((value >> 8) & 0xFF));
        ms.WriteByte((byte)(value & 0xFF));
    }
}
