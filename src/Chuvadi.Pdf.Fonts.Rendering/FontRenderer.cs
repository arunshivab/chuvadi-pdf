// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType specification — character mapping and glyph metrics
// PHASE: Phase 2 — Chuvadi.Pdf.Fonts.Rendering
// Public API: font bytes + text → glyph outlines ready for rasterization.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// High-level API for extracting glyph outlines from a TrueType or OpenType font.
/// </summary>
/// <remarks>
/// <see cref="FontRenderer"/> wraps a <see cref="TrueTypeLoader"/> and provides
/// convenient methods for text rendering pipelines:
/// <list type="bullet">
///   <item>Map a character to its glyph index via the font's cmap table.</item>
///   <item>Get the scaled glyph outline for a given point size.</item>
///   <item>Enumerate glyphs for a string with advance-width positioning.</item>
/// </list>
///
/// Glyph outlines are cached after first access to avoid repeated parsing.
/// The cache is per-<see cref="FontRenderer"/> instance and is not thread-safe.
/// </remarks>
public sealed class FontRenderer
{
    private readonly TrueTypeLoader _loader;
    private readonly Dictionary<int, GlyphOutline> _cache;
    private readonly Dictionary<long, GlyphOutline?> _hintedCache;

    /// <summary>
    /// Initialises a <see cref="FontRenderer"/> from raw font bytes.
    /// </summary>
    /// <param name="fontData">The raw TTF or OTF file bytes.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fontData"/> is null.
    /// </exception>
    /// <exception cref="FontRenderingException">
    /// Thrown when the font data is invalid or missing required tables.
    /// </exception>
    public FontRenderer(byte[] fontData)
    {
        if (fontData is null)
        {
            throw new ArgumentNullException(nameof(fontData));
        }

        _loader = new TrueTypeLoader(fontData);
        _cache = new Dictionary<int, GlyphOutline>();
        _hintedCache = new Dictionary<long, GlyphOutline?>();
    }

    /// <summary>Gets the number of font design units per em square.</summary>
    public int UnitsPerEm => _loader.UnitsPerEm;

    /// <summary>Gets the total number of glyphs in the font.</summary>
    public int NumGlyphs => _loader.NumGlyphs;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a Unicode code point to its glyph index.
    /// Returns 0 (.notdef) when the character is not present in the font.
    /// </summary>
    public int GetGlyphIndex(int codePoint)
    {
        return _loader.GetGlyphIndex(codePoint);
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

        GlyphOutline outline = _loader.GetGlyphOutline(glyphId);
        _cache[glyphId] = outline;
        return outline;
    }

    /// <summary>
    /// Gets the glyph outline grid-fitted (hinted) at the given pixels-per-em,
    /// in device space, or <c>null</c> when the glyph cannot be hinted (for
    /// example a composite glyph or a font without instructions). Callers should
    /// fall back to <see cref="GetGlyphOutline(int)"/> scaled to the same size.
    /// Results are cached per (glyph, ppem).
    /// </summary>
    /// <param name="glyphId">Zero-based glyph index.</param>
    /// <param name="ppem">Target size in pixels per em; must be positive.</param>
    /// <param name="light">When true, grid-fit the Y axis only.</param>
    public GlyphOutline? GetHintedGlyphOutline(int glyphId, int ppem, bool light)
    {
        if (ppem <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ppem), "Pixels-per-em must be positive.");
        }

        long key = ((long)ppem << 33) | ((long)(light ? 1 : 0) << 32) | (uint)glyphId;

        if (_hintedCache.TryGetValue(key, out GlyphOutline? cached))
        {
            return cached;
        }

        GlyphOutline? outline = _loader.GetHintedGlyphOutline(glyphId, ppem, light);
        _hintedCache[key] = outline;
        return outline;
    }

    /// <summary>
    /// Gets the glyph outline for a Unicode code point, in font design units.
    /// Returns the .notdef glyph when the character is not present.
    /// </summary>
    public GlyphOutline GetGlyphOutlineForChar(char c)
    {
        int glyphId = _loader.GetGlyphIndex(c);
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
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

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
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (pointSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pointSize), "Point size must be positive.");
        }

        double width = 0;

        foreach (char c in text)
        {
            int glyphId = _loader.GetGlyphIndex(c);
            GlyphMetrics metrics = _loader.GetGlyphMetrics(glyphId);
            width += metrics.AdvanceWidthAt(pointSize);
        }

        return width;
    }
}
