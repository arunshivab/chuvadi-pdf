// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7.4 — CIDFonts (CIDFontType0 and CIDFontType2)
//        §9.7.6 — Type0 font with Identity-H encoding
//        §9.9 — Embedded font programs (FontFile2, FontFile3 /CIDFontType0C)
//        §9.10.3 — ToUnicode CMaps

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Builds the PDF objects that embed a loaded <see cref="FontFace"/> as a
/// composite Type0 font with Identity-H encoding (glyph ids are the codes).
/// TrueType outlines embed as <c>CIDFontType2</c> (<c>/FontFile2</c>); CFF
/// outlines (.otf) embed as <c>CIDFontType0</c> (<c>/FontFile3
/// /CIDFontType0C</c>). A face of a TrueType collection (.ttc/.otc) embeds as
/// that face alone. Either way the program is subset to the glyphs used, with
/// glyph numbering kept.
/// </summary>
public static class FontEmbedder
{
    /// <summary>
    /// Builds the embedded-font objects for <paramref name="face"/>.
    /// </summary>
    /// <param name="face">The loaded face (TrueType or CFF outlines).</param>
    /// <param name="usedCodepoints">
    /// Unicode code points drawn through the face's cmap; each used glyph gets
    /// a width and a ToUnicode entry for its code point.
    /// </param>
    /// <param name="baseFont">The PostScript base-font name to record.</param>
    /// <param name="allocateId">Allocates fresh object ids in the owning document.</param>
    /// <param name="extraGlyphs">Raw glyph ids from pre-shaped runs to include in the subset.</param>
    /// <returns>The Type0 font id and every object to add to the document.</returns>
    /// <exception cref="ArgumentNullException">When any reference argument is null.</exception>
    /// <exception cref="InvalidDataException">The face's CFF program cannot be embedded.</exception>
    public static EmbeddedFontObjects Build(
        FontFace face,
        IReadOnlyCollection<int> usedCodepoints,
        string baseFont,
        Func<PdfObjectId> allocateId,
        IReadOnlyCollection<int>? extraGlyphs = null)
        => Build(face, usedCodepoints, new Dictionary<int, string>(), baseFont, allocateId, extraGlyphs);

    /// <summary>
    /// Builds the embedded-font objects for <paramref name="face"/>, with
    /// ToUnicode entries for glyphs that no code point reaches through the
    /// cmap (ligatures, conjuncts, alternates from shaping).
    /// </summary>
    /// <param name="face">The loaded face (TrueType or CFF outlines).</param>
    /// <param name="usedCodepoints">Unicode code points drawn through the face's cmap.</param>
    /// <param name="glyphText">
    /// Glyph id to the text it stands for. An entry is used for glyphs that
    /// <paramref name="usedCodepoints"/> does not already map; the glyph is
    /// also included in the subset. Entries longer than 256 UTF-16 units (the
    /// ToUnicode limit) are ignored.
    /// </param>
    /// <param name="baseFont">The PostScript base-font name to record.</param>
    /// <param name="allocateId">Allocates fresh object ids in the owning document.</param>
    /// <param name="extraGlyphs">Raw glyph ids from pre-shaped runs to include in the subset.</param>
    /// <returns>The Type0 font id and every object to add to the document.</returns>
    /// <exception cref="ArgumentNullException">When any reference argument is null.</exception>
    /// <exception cref="InvalidDataException">The face's CFF program cannot be embedded.</exception>
    public static EmbeddedFontObjects Build(
        FontFace face,
        IReadOnlyCollection<int> usedCodepoints,
        IReadOnlyDictionary<int, string> glyphText,
        string baseFont,
        Func<PdfObjectId> allocateId,
        IReadOnlyCollection<int>? extraGlyphs = null)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(usedCodepoints);
        ArgumentNullException.ThrowIfNull(glyphText);
        ArgumentNullException.ThrowIfNull(baseFont);
        ArgumentNullException.ThrowIfNull(allocateId);

        return BuildCore(
            face.FontProgram,
            face.IsCff,
            face.GetGlyphIndex,
            face.GetAdvanceWidth,
            face.UnitsPerEm,
            usedCodepoints,
            glyphText,
            baseFont,
            allocateId,
            extraGlyphs);
    }

    internal static EmbeddedFontObjects BuildCore(
        byte[] program,
        bool isCff,
        Func<int, int> glyphOf,
        Func<int, int> advanceOf,
        int unitsPerEm,
        IReadOnlyCollection<int> usedCodepoints,
        IReadOnlyDictionary<int, string>? glyphText,
        string baseFont,
        Func<PdfObjectId> allocateId,
        IReadOnlyCollection<int>? extraGlyphs)
    {
        double scale = 1000.0 / unitsPerEm;
        int glyphCount = GlyphCount(program);

        // Used glyphs: gid -> scaled width, and gid -> text (for ToUnicode).
        SortedDictionary<int, int> widthByGid = new SortedDictionary<int, int>();
        SortedDictionary<int, string> textByGid = new SortedDictionary<int, string>();
        foreach (int codepoint in usedCodepoints)
        {
            int gid = glyphOf(codepoint);
            if (gid <= 0 || widthByGid.ContainsKey(gid))
            {
                continue;
            }

            widthByGid[gid] = (int)Math.Round(advanceOf(gid) * scale);
            textByGid[gid] = char.ConvertFromUtf32(codepoint);
        }

        if (glyphText is not null)
        {
            foreach (KeyValuePair<int, string> pair in glyphText)
            {
                // A ToUnicode destination holds at most 256 UTF-16 units.
                if (pair.Key <= 0 || pair.Key >= glyphCount || pair.Value is null
                    || pair.Value.Length == 0 || pair.Value.Length > 256 || textByGid.ContainsKey(pair.Key))
                {
                    continue;
                }

                widthByGid.TryAdd(pair.Key, (int)Math.Round(advanceOf(pair.Key) * scale));
                textByGid[pair.Key] = pair.Value;
            }
        }

        // Pre-shaped glyphs supplied as raw ids get a width and join the
        // subset, but no ToUnicode entry of their own.
        if (extraGlyphs is not null)
        {
            foreach (int gid in extraGlyphs)
            {
                if (gid <= 0 || gid >= glyphCount || widthByGid.ContainsKey(gid))
                {
                    continue;
                }

                widthByGid[gid] = (int)Math.Round(advanceOf(gid) * scale);
            }
        }

        TrueTypeFontEmbedder.SfntMetrics metrics = TrueTypeFontEmbedder.SfntMetrics.Read(program, scale);

        HashSet<int> usedGlyphs = new HashSet<int> { 0 };
        foreach (int gid in widthByGid.Keys)
        {
            usedGlyphs.Add(gid);
        }

        List<PdfIndirectObject> objects = new List<PdfIndirectObject>();

        // The font program: subset TrueType uncompressed (as before), or the
        // subset CID-keyed CFF compressed.
        PdfObjectId fontFileId = allocateId();
        PdfDictionary fontFileDict = new PdfDictionary();
        byte[] fontFile;
        if (isCff)
        {
            byte[] cff = CffSubsetter.FindCffTable(program)
                ?? throw new InvalidDataException("The font has no CFF table.");
            fontFile = Deflate(CffSubsetter.Subset(cff, usedGlyphs));
            fontFileDict.Set(PdfName.Intern("Subtype"), PdfName.Intern("CIDFontType0C"));
            fontFileDict.Set(PdfName.Intern("Filter"), PdfName.Intern("FlateDecode"));
        }
        else
        {
            fontFile = TrueTypeSubsetter.Subset(program, usedGlyphs);
            fontFileDict.Set(PdfName.Intern("Length1"), fontFile.Length);
        }

        objects.Add(new PdfIndirectObject(fontFileId, new PdfStream(fontFileDict, fontFile)));

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
        descriptor.Set(PdfName.Intern(isCff ? "FontFile3" : "FontFile2"), new PdfReference(fontFileId));
        objects.Add(new PdfIndirectObject(descriptorId, descriptor));

        // Descendant CIDFont. A CIDFontType0 has no CIDToGIDMap: the subset
        // CFF's identity charset makes CID n glyph n.
        PdfObjectId cidFontId = allocateId();
        PdfDictionary cidFont = new PdfDictionary();
        cidFont.Set(PdfName.Type, PdfName.Intern("Font"));
        cidFont.Set(PdfName.Intern("Subtype"), PdfName.Intern(isCff ? "CIDFontType0" : "CIDFontType2"));
        cidFont.Set(PdfName.Intern("BaseFont"), PdfName.Intern(baseFont));
        PdfDictionary cidSystemInfo = new PdfDictionary();
        cidSystemInfo.Set(PdfName.Intern("Registry"), new PdfString("Adobe"));
        cidSystemInfo.Set(PdfName.Intern("Ordering"), new PdfString("Identity"));
        cidSystemInfo.Set(PdfName.Intern("Supplement"), 0);
        cidFont.Set(PdfName.Intern("CIDSystemInfo"), cidSystemInfo);
        cidFont.Set(PdfName.Intern("FontDescriptor"), new PdfReference(descriptorId));
        if (!isCff)
        {
            cidFont.Set(PdfName.Intern("CIDToGIDMap"), PdfName.Intern("Identity"));
        }

        cidFont.Set(PdfName.Intern("DW"), 1000);
        cidFont.Set(PdfName.Intern("W"), TrueTypeFontEmbedder.BuildWidthArray(widthByGid));
        objects.Add(new PdfIndirectObject(cidFontId, cidFont));

        // ToUnicode CMap.
        PdfObjectId toUnicodeId = allocateId();
        byte[] cmap = Encoding.ASCII.GetBytes(TrueTypeFontEmbedder.BuildToUnicodeCMap(textByGid));
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

    // maxp numGlyphs; glyph ids at or above it are not in the font.
    private static int GlyphCount(byte[] sfnt)
    {
        int numTables = (sfnt[4] << 8) | sfnt[5];
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + (i * 16);
            if (sfnt[record] == (byte)'m' && sfnt[record + 1] == (byte)'a' && sfnt[record + 2] == (byte)'x' && sfnt[record + 3] == (byte)'p')
            {
                int offset = (sfnt[record + 8] << 24) | (sfnt[record + 9] << 16) | (sfnt[record + 10] << 8) | sfnt[record + 11];
                return (sfnt[offset + 4] << 8) | sfnt[offset + 5];
            }
        }

        return 0x10000;
    }

    private static byte[] Deflate(byte[] data)
    {
        using MemoryStream output = new MemoryStream();
        using (ZLibStream z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }
}
