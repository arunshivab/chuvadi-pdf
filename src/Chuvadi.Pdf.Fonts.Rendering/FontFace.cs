// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType 1.9 — head, hhea, hmtx, maxp, OS/2, post, name, CFF tables;
//        TrueType/OpenType Collections
// PHASE: 3.25 — font-face measuring API

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// A glyph or font bounding box in font design units.
/// </summary>
/// <param name="XMin">The left edge.</param>
/// <param name="YMin">The bottom edge.</param>
/// <param name="XMax">The right edge.</param>
/// <param name="YMax">The top edge.</param>
public readonly record struct FontBox(int XMin, int YMin, int XMax, int YMax)
{
    /// <summary>Gets the width (<see cref="XMax"/> − <see cref="XMin"/>).</summary>
    public int Width => XMax - XMin;

    /// <summary>Gets the height (<see cref="YMax"/> − <see cref="YMin"/>).</summary>
    public int Height => YMax - YMin;
}

/// <summary>
/// One loaded font face — a TrueType (<c>glyf</c>) or CFF-based OpenType
/// font, or one face of a collection (<c>.ttc</c>/<c>.otc</c>) — with its
/// font-wide metrics and per-glyph measurements, for layout. Load it once and
/// reuse it: every member reads tables parsed at load (glyph bounds are
/// computed on first use and cached), and the object is safe to share across
/// threads.
/// </summary>
/// <remarks>
/// All metrics are in font design units; divide by <see cref="UnitsPerEm"/>
/// (or use <see cref="Scale"/>) to get ems or points. Signs follow the font
/// tables: ascenders are positive, descenders negative — except
/// <see cref="WinDescent"/>, which the OS/2 table stores as a positive
/// distance below the baseline.
/// </remarks>
public sealed class FontFace
{
    private readonly TrueTypeLoader _loader;
    private readonly CffLoader? _cff;
    private readonly double _cffToDesign;
    private readonly int _hmtx;
    private readonly int _numberOfHMetrics;
    private readonly ConcurrentDictionary<int, FontBox> _bounds = new ConcurrentDictionary<int, FontBox>();

    private FontFace(byte[] program, int faceIndex)
    {
        FontProgram = program;
        FaceIndex = faceIndex;
        Dictionary<string, (int Offset, int Length)> tables = SfntFile.Tables(program);
        (int head, int headLength) = Required(tables, "head", 54);
        (int hhea, int _) = Required(tables, "hhea", 36);
        (int maxp, int _) = Required(tables, "maxp", 6);
        (int hmtx, int _) = Required(tables, "hmtx", 0);

        _loader = new TrueTypeLoader(program);
        UnitsPerEm = SfntFile.U16(program, head + 18);
        if (UnitsPerEm <= 0)
        {
            throw new FontRenderingException("The font's head table has no units per em.");
        }

        FontBounds = new FontBox(
            SfntFile.S16(program, head + 36), SfntFile.S16(program, head + 38),
            SfntFile.S16(program, head + 40), SfntFile.S16(program, head + 42));
        int macStyle = headLength >= 46 ? SfntFile.U16(program, head + 44) : 0;
        GlyphCount = SfntFile.U16(program, maxp + 4);
        HheaAscender = SfntFile.S16(program, hhea + 4);
        HheaDescender = SfntFile.S16(program, hhea + 6);
        HheaLineGap = SfntFile.S16(program, hhea + 8);
        _numberOfHMetrics = Math.Max(1, SfntFile.U16(program, hhea + 34));
        _hmtx = hmtx;

        if (tables.TryGetValue("OS/2", out (int Offset, int Length) os2) && os2.Length >= 78)
        {
            int o = os2.Offset;
            HasOs2Table = true;
            int version = SfntFile.U16(program, o);
            WeightClass = SfntFile.U16(program, o + 4);
            StrikeoutSize = SfntFile.S16(program, o + 26);
            StrikeoutPosition = SfntFile.S16(program, o + 28);
            int fsSelection = SfntFile.U16(program, o + 62);
            IsItalic = (fsSelection & 0x01) != 0;
            IsBold = (fsSelection & 0x20) != 0;
            UseTypoMetrics = (fsSelection & 0x80) != 0;
            TypoAscender = SfntFile.S16(program, o + 68);
            TypoDescender = SfntFile.S16(program, o + 70);
            TypoLineGap = SfntFile.S16(program, o + 72);
            WinAscent = SfntFile.U16(program, o + 74);
            WinDescent = SfntFile.U16(program, o + 76);
            if (version >= 2 && os2.Length >= 90)
            {
                XHeight = SfntFile.S16(program, o + 86);
                CapHeight = SfntFile.S16(program, o + 88);
            }
        }
        else
        {
            IsItalic = (macStyle & 0x02) != 0;
            IsBold = (macStyle & 0x01) != 0;
            WeightClass = IsBold ? 700 : 400;
        }

        if (tables.TryGetValue("post", out (int Offset, int Length) post) && post.Length >= 16)
        {
            ItalicAngle = (int)SfntFile.U32(program, post.Offset + 4) / 65536.0;
            UnderlinePosition = SfntFile.S16(program, post.Offset + 8);
            UnderlineThickness = SfntFile.S16(program, post.Offset + 10);
            IsFixedPitch = SfntFile.U32(program, post.Offset + 12) != 0;
        }

        if (tables.TryGetValue("CFF ", out (int Offset, int Length) cff) && cff.Length > 4)
        {
            byte[] cffBytes = program.AsSpan(cff.Offset, cff.Length).ToArray();
            _cff = new CffLoader(cffBytes);
            _cffToDesign = (double)UnitsPerEm / Math.Max(1, _cff.UnitsPerEm);
        }
        else if (!tables.ContainsKey("glyf"))
        {
            throw new FontRenderingException("The font has neither glyf nor CFF outlines (CFF2 variable fonts are not supported).");
        }

        if (tables.TryGetValue("name", out (int Offset, int Length) name))
        {
            FamilyName = ReadName(program, name.Offset, name.Length, 16) ?? ReadName(program, name.Offset, name.Length, 1);
            SubfamilyName = ReadName(program, name.Offset, name.Length, 17) ?? ReadName(program, name.Offset, name.Length, 2);
            FullName = ReadName(program, name.Offset, name.Length, 4);
            PostScriptName = ReadName(program, name.Offset, name.Length, 6);
        }

        // OS/2 before version 2 has no cap or x-height: measure H and x.
        if (CapHeight == 0)
        {
            CapHeight = GetGlyphBounds(GetGlyphIndex('H')).YMax;
        }

        if (XHeight == 0)
        {
            XHeight = GetGlyphBounds(GetGlyphIndex('x')).YMax;
        }
    }

    /// <summary>
    /// Gets the face program as a standalone font file (for a face of a
    /// collection, the face's tables in a file of their own). Do not modify it.
    /// </summary>
    public byte[] FontProgram { get; }

    /// <summary>Gets the face's index in its collection (0 for a single font file).</summary>
    public int FaceIndex { get; }

    /// <summary>Gets a value indicating whether the outlines are CFF (PostScript) rather than TrueType.</summary>
    public bool IsCff => _cff is not null;

    /// <summary>Gets the typographic family name (name IDs 16 or 1), or null.</summary>
    public string? FamilyName { get; }

    /// <summary>Gets the typographic subfamily (style) name (name IDs 17 or 2), or null.</summary>
    public string? SubfamilyName { get; }

    /// <summary>Gets the full font name (name ID 4), or null.</summary>
    public string? FullName { get; }

    /// <summary>Gets the PostScript name (name ID 6), or null.</summary>
    public string? PostScriptName { get; }

    /// <summary>Gets the OS/2 weight class (400 regular, 700 bold).</summary>
    public int WeightClass { get; }

    /// <summary>Gets a value indicating whether the face is bold (OS/2 fsSelection, else head macStyle).</summary>
    public bool IsBold { get; }

    /// <summary>Gets a value indicating whether the face is italic (OS/2 fsSelection, else head macStyle).</summary>
    public bool IsItalic { get; }

    /// <summary>Gets a value indicating whether every glyph has the same advance (post isFixedPitch).</summary>
    public bool IsFixedPitch { get; }

    /// <summary>Gets the font design units per em (head).</summary>
    public int UnitsPerEm { get; }

    /// <summary>Gets the number of glyphs (maxp).</summary>
    public int GlyphCount { get; }

    /// <summary>Gets the bounding box of all glyphs (head).</summary>
    public FontBox FontBounds { get; }

    /// <summary>Gets the hhea ascender.</summary>
    public int HheaAscender { get; }

    /// <summary>Gets the hhea descender (negative below the baseline).</summary>
    public int HheaDescender { get; }

    /// <summary>Gets the hhea line gap.</summary>
    public int HheaLineGap { get; }

    /// <summary>Gets a value indicating whether the font has an OS/2 table (the OS/2 values are 0 otherwise).</summary>
    public bool HasOs2Table { get; }

    /// <summary>Gets the OS/2 typographic ascender.</summary>
    public int TypoAscender { get; }

    /// <summary>Gets the OS/2 typographic descender (negative below the baseline).</summary>
    public int TypoDescender { get; }

    /// <summary>Gets the OS/2 typographic line gap.</summary>
    public int TypoLineGap { get; }

    /// <summary>Gets the OS/2 Windows ascent.</summary>
    public int WinAscent { get; }

    /// <summary>Gets the OS/2 Windows descent, as a positive distance below the baseline.</summary>
    public int WinDescent { get; }

    /// <summary>
    /// Gets a value indicating whether the font asks for its typographic
    /// metrics to be used for line spacing (OS/2 fsSelection bit 7, USE_TYPO_METRICS).
    /// </summary>
    public bool UseTypoMetrics { get; }

    /// <summary>
    /// Gets the ascender the font asks layout to use: the typographic
    /// ascender when <see cref="UseTypoMetrics"/> is set, else the hhea one.
    /// Applications that match a particular program's line spacing should pick
    /// from the hhea, typographic and Windows values themselves.
    /// </summary>
    public int Ascender => UseTypoMetrics ? TypoAscender : HheaAscender;

    /// <summary>Gets the descender chosen like <see cref="Ascender"/> (negative below the baseline).</summary>
    public int Descender => UseTypoMetrics ? TypoDescender : HheaDescender;

    /// <summary>Gets the line gap chosen like <see cref="Ascender"/>.</summary>
    public int LineGap => UseTypoMetrics ? TypoLineGap : HheaLineGap;

    /// <summary>Gets the cap height (OS/2 version 2 or later; otherwise the top of the glyph for "H").</summary>
    public int CapHeight { get; }

    /// <summary>Gets the x-height (OS/2 version 2 or later; otherwise the top of the glyph for "x").</summary>
    public int XHeight { get; }

    /// <summary>Gets the underline position (post; negative below the baseline).</summary>
    public int UnderlinePosition { get; }

    /// <summary>Gets the underline thickness (post).</summary>
    public int UnderlineThickness { get; }

    /// <summary>Gets the strikeout position above the baseline (OS/2).</summary>
    public int StrikeoutPosition { get; }

    /// <summary>Gets the strikeout thickness (OS/2).</summary>
    public int StrikeoutSize { get; }

    /// <summary>Gets the italic angle in degrees counter-clockwise from vertical (post).</summary>
    public double ItalicAngle { get; }

    /// <summary>
    /// Returns the number of faces in a font file: the count of a collection
    /// (<c>.ttc</c>/<c>.otc</c>), or 1 for a single font.
    /// </summary>
    /// <param name="fontData">The font file.</param>
    /// <returns>The number of faces.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fontData"/> is null.</exception>
    /// <exception cref="FontRenderingException">The data is not a TrueType/OpenType font or collection.</exception>
    public static int GetFaceCount(byte[] fontData)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        return SfntFile.FaceCount(fontData);
    }

    /// <summary>Loads a face from a font file.</summary>
    /// <param name="fontData">A TrueType or OpenType font, or a collection.</param>
    /// <param name="faceIndex">The face to load from a collection (0 for a single font).</param>
    /// <returns>The loaded face.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fontData"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="faceIndex"/> is not a face of the file.</exception>
    /// <exception cref="FontRenderingException">The font data is invalid or unsupported.</exception>
    public static FontFace Load(byte[] fontData, int faceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        byte[] program = SfntFile.ExtractFace(fontData, faceIndex);
        try
        {
            return new FontFace(program, faceIndex);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException)
        {
            throw new FontRenderingException("The font tables are truncated or inconsistent: " + ex.Message, ex);
        }
    }

    /// <summary>Converts a value in design units to points at a font size.</summary>
    /// <param name="designUnits">The value in font design units.</param>
    /// <param name="fontSize">The font size in points.</param>
    /// <returns>The value in points.</returns>
    public double Scale(double designUnits, double fontSize) => designUnits * fontSize / UnitsPerEm;

    /// <summary>Returns the glyph for a Unicode code point, or 0 (.notdef) when the face has none.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns>The glyph id.</returns>
    public int GetGlyphIndex(int codePoint) => _loader.GetGlyphIndex(codePoint);

    /// <summary>
    /// Gets the face's Unicode character map: every code point the face maps,
    /// to its glyph id.
    /// </summary>
    public IReadOnlyDictionary<int, int> CharacterMap => _loader.CharacterMap;

    /// <summary>Returns whether the face maps the code point to a glyph.</summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns>True when a glyph other than .notdef is mapped.</returns>
    public bool HasGlyph(int codePoint) => GetGlyphIndex(codePoint) > 0;

    /// <summary>Returns a glyph's advance width (hmtx).</summary>
    /// <param name="glyphId">The glyph id.</param>
    /// <returns>The advance in design units; 0 for an id outside the font.</returns>
    public int GetAdvanceWidth(int glyphId)
    {
        if (glyphId < 0 || glyphId >= GlyphCount)
        {
            return 0;
        }

        int index = Math.Min(glyphId, _numberOfHMetrics - 1);
        int offset = _hmtx + (index * 4);
        return offset + 2 <= FontProgram.Length ? SfntFile.U16(FontProgram, offset) : 0;
    }

    /// <summary>Returns a glyph's left side bearing (hmtx).</summary>
    /// <param name="glyphId">The glyph id.</param>
    /// <returns>The bearing in design units; 0 for an id outside the font.</returns>
    public int GetLeftSideBearing(int glyphId)
    {
        if (glyphId < 0 || glyphId >= GlyphCount)
        {
            return 0;
        }

        int offset = glyphId < _numberOfHMetrics
            ? _hmtx + (glyphId * 4) + 2
            : _hmtx + (_numberOfHMetrics * 4) + ((glyphId - _numberOfHMetrics) * 2);
        return offset + 2 <= FontProgram.Length ? SfntFile.S16(FontProgram, offset) : 0;
    }

    /// <summary>
    /// Returns a glyph's bounding box: the TrueType glyph header, or the box
    /// of a CFF glyph's outline points. Empty glyphs (a space) give an all-zero box.
    /// </summary>
    /// <param name="glyphId">The glyph id.</param>
    /// <returns>The box in design units.</returns>
    public FontBox GetGlyphBounds(int glyphId)
    {
        if (glyphId < 0 || glyphId >= GlyphCount)
        {
            return default;
        }

        return _bounds.GetOrAdd(glyphId, ComputeBounds);
    }

    /// <summary>
    /// Returns the advance of a string with one glyph per code point, without
    /// shaping or kerning (use <c>TextShaper.Shape</c> with this face for
    /// shaped, kerned runs).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The total advance in design units.</returns>
    public int MeasureText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int total = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int codePoint = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            total += GetAdvanceWidth(GetGlyphIndex(codePoint));
        }

        return total;
    }

    private FontBox ComputeBounds(int glyphId)
    {
        try
        {
            RectangleOrEmpty box = _cff is not null
                ? RectangleOrEmpty.From(_cff.GetGlyphMetrics(glyphId).Bounds, _cffToDesign)
                : RectangleOrEmpty.From(_loader.GetGlyphMetrics(glyphId).Bounds, 1.0);
            return box.Box;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidOperationException or FontRenderingException)
        {
            // A damaged glyph has no measurable outline.
            return default;
        }
    }

    private static (int Offset, int Length) Required(Dictionary<string, (int Offset, int Length)> tables, string tag, int minLength)
    {
        if (!tables.TryGetValue(tag, out (int Offset, int Length) table) || table.Length < minLength)
        {
            throw new FontRenderingException($"The font has no usable '{tag}' table.");
        }

        return table;
    }

    // The best record for a name ID: Windows Unicode English, then any
    // Windows or Unicode record (UTF-16BE), then a Macintosh Roman record.
    private static string? ReadName(byte[] data, int table, int length, int nameId)
    {
        if (length < 6)
        {
            return null;
        }

        int count = SfntFile.U16(data, table + 2);
        int strings = table + SfntFile.U16(data, table + 4);
        string? best = null;
        int bestRank = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            int rec = table + 6 + (i * 12);
            if (rec + 12 > table + length)
            {
                break;
            }

            if (SfntFile.U16(data, rec + 6) != nameId)
            {
                continue;
            }

            int platform = SfntFile.U16(data, rec), encoding = SfntFile.U16(data, rec + 2), language = SfntFile.U16(data, rec + 4);
            int len = SfntFile.U16(data, rec + 8), off = strings + SfntFile.U16(data, rec + 10);
            if (off + len > data.Length)
            {
                continue;
            }

            int rank = platform == 3 && encoding == 1 && language == 0x409 ? 0
                : platform == 3 || platform == 0 ? 1
                : platform == 1 && encoding == 0 ? 2
                : int.MaxValue;
            if (rank < bestRank)
            {
                bestRank = rank;
                best = rank == 2 ? Encoding.Latin1.GetString(data, off, len) : Encoding.BigEndianUnicode.GetString(data, off, len);
            }
        }

        return best;
    }

    // Converts a float rectangle (y-up font units) to a rounded box.
    private readonly struct RectangleOrEmpty
    {
        private RectangleOrEmpty(FontBox box)
        {
            Box = box;
        }

        internal FontBox Box { get; }

        internal static RectangleOrEmpty From(Chuvadi.Pdf.Graphics.RectangleF r, double scale)
        {
            if (r.Width <= 0 && r.Height <= 0)
            {
                return default;
            }

            return new RectangleOrEmpty(new FontBox(
                (int)Math.Floor(r.X * scale), (int)Math.Floor(r.Y * scale),
                (int)Math.Ceiling((r.X + r.Width) * scale), (int)Math.Ceiling((r.Y + r.Height) * scale)));
        }
    }
}
