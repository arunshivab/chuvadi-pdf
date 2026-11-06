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
///
/// Every method is safe to call from any number of threads at once: the
/// caches are concurrent and keyed weakly. See docs/THREAD-SAFETY.md.
/// </remarks>
public static class TextShaper
{
    /// <summary>
    /// Shapes a run of text using the specified font and script.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The face loaded from <paramref name="ttf"/> is kept and reused for as
    /// long as that array is alive, so repeated calls with the same array cost
    /// the same as the <see cref="FontFace"/> overload. Changing the array's
    /// bytes after a call is not detected: pass a new array for a different
    /// font program.
    /// </para>
    /// <para>
    /// The face is found again by the array instance, not by its bytes.
    /// <c>FontPack.ReadAllBytes</c> returns a new copy on every call, so
    /// <c>Shape(FontPack.ReadAllBytes(name), …)</c> in a loop loads the font
    /// every time. Keep the array, or load a <see cref="FontFace"/> once and
    /// use that overload.
    /// </para>
    /// <para>
    /// Preferred over the <see cref="FontFace"/> overload when the call is
    /// otherwise ambiguous (a bare <c>null</c> font), so code written before
    /// 3.25 still binds here.
    /// </para>
    /// </remarks>
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

        return Shape(LoadedFaces.GetValue(ttf, static bytes => FontFace.Load(bytes)), text, script, features);
    }

    // The face loaded for each font array passed to Shape(byte[], ...), kept
    // while the array is alive (3.27, D-70: loading the face and building the
    // plan on every call made the byte[] overload ten times slower in 3.26).
    private static readonly ConditionalWeakTable<byte[], FontFace> LoadedFaces = new ConditionalWeakTable<byte[], FontFace>();

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
