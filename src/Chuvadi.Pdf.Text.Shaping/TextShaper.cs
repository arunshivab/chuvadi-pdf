// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Text.Shaping.OpenType;

namespace Chuvadi.Pdf.Text.Shaping;

/// <summary>
/// Shapes a run of text using the OpenType GSUB and GPOS tables embedded in a
/// TrueType/OpenType font. Returns a sequence of positioned glyphs in visual order.
/// </summary>
/// <remarks>
/// Since 3.26 the shaper is a port of HarfBuzz's OpenType shaper: the Indic
/// shaper for Tamil, Devanagari, Bengali, Gurmukhi, Gujarati, Odia, Telugu,
/// Kannada and Malayalam, and the default shaper for <see cref="LipiScript.Latin"/>
/// runs. Glyphs, clusters, advances and offsets match HarfBuzz 15 on the
/// conformance corpora in <c>tools/shaping-data</c>. Not ported: the Arabic,
/// Khmer, Myanmar, Thai, Hangul and USE shapers (no LiPi script needs them),
/// variation sequences (cmap format 14), the legacy 'kern' table, vertical
/// text and variable-font instances other than the default.
/// </remarks>
public static class TextShaper
{
    /// <summary>
    /// Shapes a run of text using the specified font and script.
    /// </summary>
    /// <param name="ttf">Raw TrueType/OpenType font bytes (sfnt).</param>
    /// <param name="text">The text to shape. Must be non-empty.</param>
    /// <param name="script">The script of the text run.</param>
    /// <param name="features">
    /// Feature set to apply. Defaults to <see cref="ShapingFeatures.Default"/> when null.
    /// </param>
    /// <returns>
    /// The shaped glyphs in visual order, with advances and offsets in 1000ths of an em.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="ttf"/> or <paramref name="text"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty.</exception>
    /// <exception cref="FontRenderingException">The font program could not be read.</exception>
    /// <remarks>
    /// Loads the face and builds the shaping plan on every call; in a loop use
    /// the <see cref="FontFace"/> overload. Preferred over the <see cref="FontFace"/> overload when the call is
    /// otherwise ambiguous (a bare <c>null</c> font), so code written before
    /// 3.25 still binds here.
    /// </remarks>
    [OverloadResolutionPriority(1)]
    public static IReadOnlyList<ShapedGlyph> Shape(
        byte[] ttf,
        string text,
        LipiScript script,
        ShapingFeatures? features = null)
    {
        ArgumentNullException.ThrowIfNull(ttf);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            throw new ArgumentException("Text must not be empty.", nameof(text));
        }

        return Shape(FontFace.Load(ttf), text, script, features);
    }

    /// <summary>
    /// Shapes a run of text with a loaded <see cref="FontFace"/>. The face's
    /// tables are parsed once and the GSUB/GPOS feature lookups for each script
    /// are cached with the face, so repeated calls (a line-fitting loop) do no
    /// font parsing. Results equal <see cref="Shape(byte[], string, LipiScript, ShapingFeatures?)"/>
    /// for the same font program. Safe to call concurrently with one face.
    /// </summary>
    /// <param name="face">The loaded face (TrueType or CFF outlines; a collection face works too).</param>
    /// <param name="text">The text to shape. Must be non-empty.</param>
    /// <param name="script">The script of the text run.</param>
    /// <param name="features">
    /// Feature set to apply. Defaults to <see cref="ShapingFeatures.Default"/> when null.
    /// </param>
    /// <returns>
    /// The shaped glyphs in visual order, with advances and offsets in 1000ths of an em.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="face"/> or <paramref name="text"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty.</exception>
    public static IReadOnlyList<ShapedGlyph> Shape(
        FontFace face,
        string text,
        LipiScript script,
        ShapingFeatures? features = null)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            throw new ArgumentException("Text must not be empty.", nameof(text));
        }

        ShapingFeatures feat = features ?? ShapingFeatures.Default;
        FaceCache cache = Caches.GetValue(face, f => new FaceCache(f));
        ShapePlan plan = cache.Plan(script, feat);
        ShapeBuffer buffer = plan.Shape(text);
        double scale = 1000.0 / face.UnitsPerEm;
        List<ShapedGlyph> result = new List<ShapedGlyph>(buffer.Len);
        for (int i = 0; i < buffer.Len; i++)
        {
            GlyphInfo g = buffer.Info[i];
            GlyphPos p = buffer.Pos[i];
            result.Add(new ShapedGlyph(g.Codepoint, p.XAdvance * scale, p.XOffset * scale, p.YOffset * scale, g.Cluster));
        }

        return result;
    }

    // Per-face shaping state: the parsed layout tables and the compiled plans
    // per script and feature set (read-only once built).
    private static readonly ConditionalWeakTable<FontFace, FaceCache> Caches = new ConditionalWeakTable<FontFace, FaceCache>();

    private sealed class FaceCache
    {
        private readonly ConcurrentDictionary<string, ShapePlan> _plans =
            new ConcurrentDictionary<string, ShapePlan>(StringComparer.Ordinal);

        private readonly ShapingFace _face;

        internal FaceCache(FontFace face)
        {
            _face = new ShapingFace(face);
        }

        internal ShapePlan Plan(LipiScript script, ShapingFeatures features)
        {
            string key = script.ToString() + "|" + string.Join(",", features.Toggles());
            return _plans.GetOrAdd(key, _ => new ShapePlan(_face, script, features));
        }
    }
}
