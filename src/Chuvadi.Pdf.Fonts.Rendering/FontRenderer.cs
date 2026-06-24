// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType specification — character mapping and glyph metrics
//        Adobe Technical Note #5176 — CFF File Format
// PHASE: Phase 2 — Chuvadi.Pdf.Fonts.Rendering
// Public API: font bytes + text → glyph outlines ready for rasterization.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// High-level API for extracting glyph outlines from a TrueType, OpenType, or
/// CFF (Type 1C) font.
/// </summary>
/// <remarks>
/// <see cref="FontRenderer"/> wraps either a <see cref="TrueTypeLoader"/> (for
/// <c>glyf</c>-based TrueType programs) or a <see cref="CffLoader"/> (for bare
/// CFF / Type 1C programs and OpenType fonts whose outlines live in a
/// <c>CFF </c> table) and provides convenient methods for text rendering
/// pipelines:
/// <list type="bullet">
///   <item>Map a character to its glyph index via the font's cmap (TrueType)
///   or a charset-derived Unicode map (CFF).</item>
///   <item>Get the scaled glyph outline for a given point size.</item>
///   <item>Enumerate glyphs for a string with advance-width positioning.</item>
/// </list>
///
/// The embedded program format is detected from the raw bytes: a bare CFF
/// program (<c>/FontFile3</c> subtype <c>Type1C</c>) and an OpenType font whose
/// only outline table is <c>CFF </c> are both routed to <see cref="CffLoader"/>;
/// everything else is parsed as TrueType. CFF programs have no hinting bytecode,
/// so <see cref="GetHintedGlyphOutline"/> returns <c>null</c> for them and
/// callers fall back to the scaled unhinted outline.
///
/// Glyph outlines are cached after first access to avoid repeated parsing.
/// The cache is per-<see cref="FontRenderer"/> instance and is not thread-safe.
/// </remarks>
public sealed class FontRenderer
{
    private readonly TrueTypeLoader? _loader;
    private readonly CffLoader? _cff;
    private readonly Dictionary<int, int> _cffUnicodeToGid;
    private readonly Dictionary<int, GlyphOutline> _cache;
    private readonly Dictionary<long, GlyphOutline?> _hintedCache;

    /// <summary>
    /// Initialises a <see cref="FontRenderer"/> from raw font bytes.
    /// </summary>
    /// <param name="fontData">The raw TrueType, OpenType, or CFF program bytes.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fontData"/> is null.
    /// </exception>
    /// <exception cref="FontRenderingException">
    /// Thrown when the font data is invalid or missing required tables.
    /// </exception>
    public FontRenderer(byte[] fontData)
    {
        ArgumentNullException.ThrowIfNull(fontData);

        _cache = new Dictionary<int, GlyphOutline>();
        _hintedCache = new Dictionary<long, GlyphOutline?>();
        _cffUnicodeToGid = new Dictionary<int, int>();

        if (TryGetCffProgram(fontData, out byte[] cffBytes))
        {
            _cff = new CffLoader(cffBytes);
            BuildCffUnicodeToGid(_cff, _cffUnicodeToGid);
        }
        else
        {
            _loader = new TrueTypeLoader(fontData);
        }
    }

    // The TrueType loader, required when the program is not CFF.
    private TrueTypeLoader Loader =>
        _loader ?? throw new InvalidOperationException("Font program is CFF; no TrueType loader is available.");

    /// <summary>Gets the number of font design units per em square.</summary>
    public int UnitsPerEm => _cff?.UnitsPerEm ?? Loader.UnitsPerEm;

    /// <summary>Gets the total number of glyphs in the font.</summary>
    public int NumGlyphs => _cff?.NumGlyphs ?? Loader.NumGlyphs;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a Unicode code point to its glyph index.
    /// Returns 0 (.notdef) when the character is not present in the font.
    /// </summary>
    public int GetGlyphIndex(int codePoint)
    {
        if (_cff is not null)
        {
            return _cffUnicodeToGid.GetValueOrDefault(codePoint, 0);
        }

        return Loader.GetGlyphIndex(codePoint);
    }

    /// <summary>
    /// Resolves a raw character code (from a content-stream string) to a glyph
    /// index, honouring symbol and Macintosh cmaps and a direct code-as-index
    /// fallback. Use for simple fonts where the code, not a Unicode value,
    /// selects the glyph. Returns 0 (.notdef) when nothing matches.
    /// </summary>
    /// <remarks>
    /// CFF programs carry no cmap; their glyphs are selected by name (and hence
    /// by Unicode via the charset), so this method returns 0 for CFF fonts and
    /// callers resolve the glyph through <see cref="GetGlyphIndexUnicode"/>.
    /// </remarks>
    public int GetGlyphIndexForCode(int code, bool symbolic)
    {
        if (_cff is not null)
        {
            return 0;
        }

        return Loader.GetGlyphIndexForCode(code, symbolic);
    }

    /// <summary>Maps a Unicode code point to a glyph index via the Unicode cmap (TrueType) or charset map (CFF).</summary>
    public int GetGlyphIndexUnicode(int codePoint)
    {
        if (_cff is not null)
        {
            return _cffUnicodeToGid.GetValueOrDefault(codePoint, 0);
        }

        return Loader.GetGlyphIndexUnicode(codePoint);
    }

    /// <summary>
    /// Gets the glyph outline for a glyph index, in font design units (unscaled).
    /// Results are cached after first access.
    /// </summary>
    public GlyphOutline GetGlyphOutline(int glyphId)
    {
        if (_cache.TryGetValue(glyphId, out GlyphOutline? cached))
        {
            return cached;
        }

        GlyphOutline outline = _cff is not null
            ? _cff.GetGlyphOutline(glyphId)
            : Loader.GetGlyphOutline(glyphId);
        _cache[glyphId] = outline;
        return outline;
    }

    /// <summary>
    /// Gets the glyph outline grid-fitted (hinted) at the given pixels-per-em,
    /// in device space, or <c>null</c> when the glyph cannot be hinted (for
    /// example a composite glyph, a CFF program, or a font without instructions).
    /// Callers should fall back to <see cref="GetGlyphOutline(int)"/> scaled to
    /// the same size. Results are cached per (glyph, ppem).
    /// </summary>
    /// <param name="glyphId">Zero-based glyph index.</param>
    /// <param name="ppem">Target size in pixels per em; must be positive.</param>
    /// <param name="light">When true, grid-fit the Y axis only.</param>
    /// <param name="autohintFallback">
    /// When true (the default), glyphs of fonts with no hinting programs are
    /// grid-fitted by the geometric autohinter instead of returning null.
    /// </param>
    public GlyphOutline? GetHintedGlyphOutline(
        int glyphId, int ppem, bool light, bool autohintFallback = true)
    {
        if (ppem <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ppem), "Pixels-per-em must be positive.");
        }

        // CFF (Type 2 charstring) programs have no TrueType hinting bytecode.
        if (_cff is not null)
        {
            return null;
        }

        long key = ((long)ppem << 34)
            | ((long)(autohintFallback ? 1 : 0) << 33)
            | ((long)(light ? 1 : 0) << 32)
            | (uint)glyphId;

        if (_hintedCache.TryGetValue(key, out GlyphOutline? cached))
        {
            return cached;
        }

        GlyphOutline? outline = Loader.GetHintedGlyphOutline(glyphId, ppem, light, autohintFallback);
        _hintedCache[key] = outline;
        return outline;
    }

    /// <summary>
    /// Gets the glyph outline for a Unicode code point, in font design units.
    /// Returns the .notdef glyph when the character is not present.
    /// </summary>
    public GlyphOutline GetGlyphOutlineForChar(char c)
    {
        int glyphId = GetGlyphIndex(c);
        return GetGlyphOutline(glyphId);
    }

    /// <summary>
    /// Gets the glyph outline for a glyph index, scaled to the given point size.
    /// </summary>
    public GlyphOutline GetScaledGlyphOutline(int glyphId, double pointSize)
    {
        if (pointSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pointSize), "Point size must be positive.");
        }

        return GetGlyphOutline(glyphId).Scale(pointSize);
    }

    /// <summary>
    /// Returns an ordered list of positioned glyph outlines for a string of text,
    /// scaled to the given point size. Each entry includes the glyph and its
    /// X origin (in PDF points, starting from 0).
    /// </summary>
    /// <param name="text">The text to lay out.</param>
    /// <param name="pointSize">The target size in PDF points.</param>
    /// <returns>
    /// A list of (x, GlyphOutline) pairs in visual order.
    /// </returns>
    public List<(double X, GlyphOutline Glyph)> LayoutText(string text, double pointSize)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (pointSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pointSize), "Point size must be positive.");
        }

        List<(double X, GlyphOutline Glyph)> result =
            new List<(double X, GlyphOutline Glyph)>(text.Length);

        double x = 0;

        foreach (char c in text)
        {
            GlyphOutline scaled = GetGlyphOutlineForChar(c).Scale(pointSize);
            result.Add((x, scaled));
            x += scaled.Metrics.AdvanceWidthAt(pointSize);
        }

        return result;
    }

    /// <summary>
    /// Measures the total advance width of a string in PDF points.
    /// </summary>
    public double MeasureText(string text, double pointSize)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (pointSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pointSize), "Point size must be positive.");
        }

        double width = 0;

        foreach (char c in text)
        {
            int glyphId = GetGlyphIndex(c);
            GlyphMetrics metrics = GetGlyphOutline(glyphId).Metrics;
            width += metrics.AdvanceWidthAt(pointSize);
        }

        return width;
    }

    // ── CFF detection ─────────────────────────────────────────────────────

    // Determines whether <paramref name="fontData"/> is a CFF program that the
    // CffLoader should handle, returning the CFF bytes to parse. Handles two
    // shapes: a bare CFF program (the /FontFile3 Type1C form, which begins with
    // a CFF header rather than an SFNT magic) and an SFNT-wrapped OpenType font
    // whose only outline table is "CFF " (no "glyf"). TrueType programs and any
    // unrecognised data return false so the TrueTypeLoader path is used.
    private static bool TryGetCffProgram(byte[] data, out byte[] cffBytes)
    {
        cffBytes = Array.Empty<byte>();

        if (data.Length < 4)
        {
            return false;
        }

        uint magic = ReadUInt32BE(data, 0);
        bool isSfnt = magic == 0x00010000 // TrueType outlines
            || magic == 0x4F54544F        // 'OTTO' — CFF outlines
            || magic == 0x74727565        // 'true'
            || magic == 0x74797031;       // 'typ1'

        if (isSfnt)
        {
            return TryExtractCffTable(data, out cffBytes);
        }

        // Not an SFNT. A bare CFF program begins with a CFF header:
        // major (1) = 1, minor (1), hdrSize (1), offSize (1) in 1..4.
        if (data[0] == 0x01 && data[2] >= 4 && data[2] <= 64 && data[3] >= 1 && data[3] <= 4)
        {
            cffBytes = data;
            return true;
        }

        return false;
    }

    // Scans an SFNT table directory. When the font has a "CFF " table and no
    // "glyf" table (an OpenType-CFF font), the CFF table bytes are sliced out
    // and returned. TrueType fonts (with "glyf") return false.
    private static bool TryExtractCffTable(byte[] data, out byte[] cffBytes)
    {
        cffBytes = Array.Empty<byte>();

        if (data.Length < 12)
        {
            return false;
        }

        int numTables = (data[4] << 8) | data[5];
        bool hasGlyf = false;
        int cffOffset = -1;
        int cffLength = 0;

        for (int i = 0; i < numTables; i++)
        {
            int entry = 12 + (i * 16);
            if (entry + 16 > data.Length)
            {
                break;
            }

            string tag = ReadTag(data, entry);
            if (tag == "glyf")
            {
                hasGlyf = true;
            }
            else if (tag == "CFF ")
            {
                cffOffset = (int)ReadUInt32BE(data, entry + 8);
                cffLength = (int)ReadUInt32BE(data, entry + 12);
            }
        }

        if (!hasGlyf
            && cffOffset >= 0
            && cffLength > 0
            && (long)cffOffset + cffLength <= data.Length)
        {
            cffBytes = new byte[cffLength];
            Array.Copy(data, cffOffset, cffBytes, 0, cffLength);
            return true;
        }

        return false;
    }

    // Builds a Unicode → glyph-index map for a simple CFF font by resolving each
    // charset glyph name to a Unicode scalar via the Adobe Glyph List and
    // pairing it with the name's glyph index. This mirrors the cmap synthesis
    // used when embedding CFF fonts for SVG output, so raster and SVG select the
    // same glyphs. CID-keyed fonts have no glyph-name charset and yield an empty
    // map (their glyphs are reached through the composite-font code path).
    private static void BuildCffUnicodeToGid(CffLoader cff, Dictionary<int, int> result)
    {
        foreach (KeyValuePair<string, int> kv in cff.GlyphNameToGid)
        {
            int gid = kv.Value;
            if (gid <= 0 || gid > 0xFFFF)
            {
                continue;
            }

            int? codePoint = GlyphNameToUnicode.ResolveSingle(kv.Key);
            if (codePoint is null || codePoint.Value < 0x20)
            {
                continue;
            }

            result.TryAdd(codePoint.Value, gid);
        }
    }

    private static uint ReadUInt32BE(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24)
            | ((uint)data[offset + 1] << 16)
            | ((uint)data[offset + 2] << 8)
            | data[offset + 3];
    }

    private static string ReadTag(byte[] data, int offset)
    {
        return new string(new[]
        {
            (char)data[offset],
            (char)data[offset + 1],
            (char)data[offset + 2],
            (char)data[offset + 3],
        });
    }
}
