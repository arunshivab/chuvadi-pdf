// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.3 (TrueType fonts), §9.8 (font descriptors)
// PHASE: Phase 3 — PDF/A font embedding
//
// Builds the PDF objects for an embedded simple (single-byte) TrueType font:
// the font dictionary (/Subtype /TrueType with /Widths and /Encoding), the font
// descriptor (with a nonsymbolic flag set), and the FontFile2 stream carrying
// the subsetted, cmap-bearing sfnt produced by SimpleFontProgram.

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.PdfA;

internal static class SimpleFontEmbedder
{
    private const int FirstChar = 32;
    private const int LastChar = 255;

    /// <summary>The objects produced for one embedded simple TrueType font.</summary>
    /// <param name="FontId">The object id of the font dictionary.</param>
    /// <param name="Objects">The font, descriptor, and FontFile2 indirect objects.</param>
    internal sealed record FontObjects(PdfObjectId FontId, IReadOnlyList<PdfIndirectObject> Objects);

    /// <summary>
    /// Builds an embedded simple TrueType font from a source TTF and a prepared
    /// program, with WinAnsiEncoding.
    /// </summary>
    /// <param name="sourceTtf">The source TrueType font (for descriptor metrics).</param>
    /// <param name="program">The subsetted program (sfnt + per-code widths).</param>
    /// <param name="faceName">The base face name, e.g. "LiberationSans".</param>
    /// <param name="serif">Whether the face is serif (sets the Serif descriptor flag).</param>
    /// <param name="allocate">Allocates a fresh object id.</param>
    /// <returns>The font id and the indirect objects to register.</returns>
    internal static FontObjects BuildSimpleTrueTypeFont(
        byte[] sourceTtf,
        EmbeddableFont program,
        string faceName,
        bool serif,
        Func<PdfObjectId> allocate)
    {
        ArgumentNullException.ThrowIfNull(sourceTtf);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(faceName);
        ArgumentNullException.ThrowIfNull(allocate);

        string baseFont = SubsetTag(program.GidByCode) + "+" + faceName;
        DescriptorMetrics metrics = DescriptorMetrics.Read(sourceTtf, program.UnitsPerEm, serif);

        PdfObjectId fontFileId = allocate();
        PdfDictionary fontFileDict = new PdfDictionary();
        fontFileDict.Set(PdfName.Intern("Length1"), program.Sfnt.Length);
        PdfIndirectObject fontFile = new PdfIndirectObject(fontFileId, new PdfStream(fontFileDict, program.Sfnt));

        PdfObjectId descriptorId = allocate();
        PdfDictionary descriptor = new PdfDictionary();
        descriptor.Set(PdfName.Type, PdfName.Intern("FontDescriptor"));
        descriptor.Set(PdfName.Intern("FontName"), PdfName.Intern(baseFont));
        descriptor.Set(PdfName.Intern("Flags"), metrics.Flags);
        descriptor.Set(PdfName.Intern("FontBBox"), new PdfArray(new PdfPrimitive[]
        {
            new PdfInteger(metrics.XMin),
            new PdfInteger(metrics.YMin),
            new PdfInteger(metrics.XMax),
            new PdfInteger(metrics.YMax),
        }));
        descriptor.Set(PdfName.Intern("ItalicAngle"), new PdfReal(metrics.ItalicAngle));
        descriptor.Set(PdfName.Intern("Ascent"), metrics.Ascent);
        descriptor.Set(PdfName.Intern("Descent"), metrics.Descent);
        descriptor.Set(PdfName.Intern("CapHeight"), metrics.CapHeight);
        descriptor.Set(PdfName.Intern("StemV"), 80);
        descriptor.Set(PdfName.Intern("MissingWidth"), 0);
        descriptor.Set(PdfName.Intern("FontFile2"), new PdfReference(fontFileId));
        PdfIndirectObject descriptorObj = new PdfIndirectObject(descriptorId, descriptor);

        List<PdfPrimitive> widths = new List<PdfPrimitive>(LastChar - FirstChar + 1);
        for (int code = FirstChar; code <= LastChar; code++)
        {
            widths.Add(new PdfInteger(program.WidthByCode[code]));
        }

        PdfObjectId fontId = allocate();
        PdfDictionary font = new PdfDictionary();
        font.Set(PdfName.Type, PdfName.Intern("Font"));
        font.Set(PdfName.Intern("Subtype"), PdfName.Intern("TrueType"));
        font.Set(PdfName.Intern("BaseFont"), PdfName.Intern(baseFont));
        font.Set(PdfName.Intern("FirstChar"), FirstChar);
        font.Set(PdfName.Intern("LastChar"), LastChar);
        font.Set(PdfName.Intern("Widths"), new PdfArray(widths));
        font.Set(PdfName.Intern("Encoding"), PdfName.Intern("WinAnsiEncoding"));
        font.Set(PdfName.Intern("FontDescriptor"), new PdfReference(descriptorId));
        PdfIndirectObject fontObj = new PdfIndirectObject(fontId, font);

        return new FontObjects(fontId, new List<PdfIndirectObject> { fontFile, descriptorObj, fontObj });
    }

    // A deterministic 6-uppercase-letter subset tag derived from the glyph set.
    private static string SubsetTag(int[] gidByCode)
    {
        uint hash = 2166136261u;
        foreach (int gid in gidByCode)
        {
            hash = (hash ^ (uint)gid) * 16777619u;
        }

        char[] tag = new char[6];
        for (int i = 0; i < 6; i++)
        {
            tag[i] = (char)('A' + (int)(hash % 26u));
            hash /= 26u;
        }

        return new string(tag);
    }

    private sealed record DescriptorMetrics(
        int Flags, int XMin, int YMin, int XMax, int YMax,
        double ItalicAngle, int Ascent, int Descent, int CapHeight)
    {
        internal static DescriptorMetrics Read(byte[] ttf, int unitsPerEm, bool serif)
        {
            Dictionary<string, int> tables = ReadTableDirectory(ttf);
            double scale = unitsPerEm > 0 ? 1000.0 / unitsPerEm : 1.0;

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

            int capHeight = ascent * 7 / 10;
            if (os2 >= 0 && U16(ttf, os2) >= 2)
            {
                int sCap = Scale(S16(ttf, os2 + 88), scale);
                if (sCap != 0)
                {
                    capHeight = sCap;
                }
            }

            double italicAngle = post >= 0 ? S32(ttf, post + 4) / 65536.0 : 0.0;

            // Nonsymbolic (32); add Serif (2) and Italic (64) as applicable.
            int flags = 32;
            if (serif)
            {
                flags |= 2;
            }

            if ((macStyle & 0x2) != 0 || italicAngle != 0.0)
            {
                flags |= 64;
            }

            return new DescriptorMetrics(flags, xMin, yMin, xMax, yMax, italicAngle, ascent, descent, capHeight);
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

        private static int Scale(int value, double scale) => (int)Math.Round(value * scale);

        private static int U16(byte[] d, int p) => (d[p] << 8) | d[p + 1];

        private static int S16(byte[] d, int p) => (short)((d[p] << 8) | d[p + 1]);

        private static uint U32(byte[] d, int p)
            => ((uint)d[p] << 24) | ((uint)d[p + 1] << 16) | ((uint)d[p + 2] << 8) | d[p + 3];

        private static int S32(byte[] d, int p) => (int)U32(d, p);
    }
}
