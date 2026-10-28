// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Text.Shaping.Indic;
using Chuvadi.Pdf.Text.Shaping.OpenType;

namespace Chuvadi.Pdf.Text.Shaping;

/// <summary>
/// Shapes a run of text using the OpenType GSUB and GPOS tables embedded in a
/// TrueType/OpenType font. Returns a sequence of positioned glyphs in visual order.
/// </summary>
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
    /// <remarks>
    /// Preferred over the <see cref="FontFace"/> overload when the call is
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

        ShapingFeatures feat = features ?? ShapingFeatures.Default;
        TrueTypeLoader loader = new TrueTypeLoader(ttf);
        return ShapeCore(
            ttf, loader, gid => loader.GetGlyphMetrics(gid).AdvanceWidth, 1000.0 / loader.UnitsPerEm,
            text, script, feat, cache: null);
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

        FaceCache cache = Caches.GetValue(face, f => new FaceCache(f));
        return ShapeCore(
            face.FontProgram, cache.Loader, face.GetAdvanceWidth, 1000.0 / face.UnitsPerEm,
            text, script, features ?? ShapingFeatures.Default, cache);
    }

    // Per-face shaping state: a loader over the face program, the GSUB/GPOS
    // offsets, and the feature maps per script (read-only once built).
    private static readonly ConditionalWeakTable<FontFace, FaceCache> Caches = new ConditionalWeakTable<FontFace, FaceCache>();

    private sealed class FaceCache
    {
        private readonly ConcurrentDictionary<string, Dictionary<string, List<int>>> _gsubMaps =
            new ConcurrentDictionary<string, Dictionary<string, List<int>>>(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, Dictionary<string, List<int>>> _gposMaps =
            new ConcurrentDictionary<string, Dictionary<string, List<int>>>(StringComparer.Ordinal);

        private readonly byte[] _sfnt;

        internal FaceCache(FontFace face)
        {
            _sfnt = face.FontProgram;
            Loader = new TrueTypeLoader(_sfnt);
            Gsub = OtReader.FindTable(_sfnt, "GSUB");
            Gpos = OtReader.FindTable(_sfnt, "GPOS");
        }

        internal TrueTypeLoader Loader { get; }

        internal int Gsub { get; }

        internal int Gpos { get; }

        internal Dictionary<string, List<int>> GsubMap(string scriptTag) =>
            _gsubMaps.GetOrAdd(scriptTag, tag => OtReader.ReadFeatureMap(_sfnt, Gsub, tag, "dflt"));

        internal Dictionary<string, List<int>> GposMap(string scriptTag) =>
            _gposMaps.GetOrAdd(scriptTag, tag => OtReader.ReadFeatureMap(_sfnt, Gpos, tag, "dflt"));
    }

    private static List<ShapedGlyph> ShapeCore(
        byte[] ttf, TrueTypeLoader loader, Func<int, int> advanceOf, double scale,
        string text, LipiScript script, ShapingFeatures feat, FaceCache? cache)
    {
        bool indic = IsIndicScript(script);

        // 1. Map code points to initial glyph ids.
        // For Indic scripts the Cluster field stores the source codepoint so the
        // reorderer can classify each slot by its Unicode category.
        // For Latin the Cluster field holds a sequential index for cluster tracking.
        GlyphBuffer buffer = BuildBuffer(text, loader, advanceOf, scale, storeCodepointInCluster: indic);

        // 2. Indic reordering: reorder syllables and synthesise Left_And_Right
        // decompositions before GSUB runs on the visual-order buffer.
        string scriptTag = ScriptToOtTag(script);
        if (indic)
        {
            buffer = IndicReorderer.Reorder(buffer, script, loader);
        }

        // 3. Apply GSUB substitution
        if (cache is null)
        {
            GsubEngine.Apply(ttf, buffer, feat, scriptTag, "dflt");
        }
        else if (cache.Gsub >= 0)
        {
            GsubEngine.Apply(ttf, cache.Gsub, cache.GsubMap(scriptTag), buffer, feat);
        }

        // 4. Reset slot advances to 0 so GPOS accumulates only deltas
        foreach (GlyphSlot s in buffer.ActiveSlots())
        {
            s.XAdvance = 0;
        }

        // 5. Apply GPOS positioning
        if (cache is null)
        {
            GposEngine.Apply(ttf, buffer, feat, scriptTag, "dflt");
        }
        else if (cache.Gpos >= 0)
        {
            GposEngine.Apply(ttf, cache.Gpos, cache.GposMap(scriptTag), buffer, feat);
        }

        // 6. Collect active slots, re-resolve advances for post-GSUB glyph ids
        return Collect(buffer, advanceOf, scale);
    }

    private static GlyphBuffer BuildBuffer(
        string text, TrueTypeLoader loader, Func<int, int> advanceOf, double scale, bool storeCodepointInCluster = false)
    {
        List<GlyphSlot> slots = new List<GlyphSlot>();
        int cluster = 0;
        int i = 0;
        while (i < text.Length)
        {
            int codepoint = char.ConvertToUtf32(text, i);
            int width = char.IsSurrogatePair(text, i) ? 2 : 1;
            int gid = loader.GetGlyphIndex(codepoint);
            int advance = gid > 0
                ? (int)Math.Round(advanceOf(gid) * scale)
                : 0;
            int clusterValue = storeCodepointInCluster ? codepoint : cluster;
            slots.Add(new GlyphSlot(gid, clusterValue, advance));
            cluster++;
            i += width;
        }

        return new GlyphBuffer(slots);
    }

    private static List<ShapedGlyph> Collect(GlyphBuffer buffer, Func<int, int> advanceOf, double scale)
    {
        List<ShapedGlyph> result = new List<ShapedGlyph>();
        foreach (GlyphSlot slot in buffer.ActiveSlots())
        {
            // Re-fetch design-space advance for the post-GSUB glyph id.
            // slot.XAdvance after step 4-5 holds only the GPOS delta (we zeroed it
            // before GPOS ran). Add the font's base advance for the current glyph.
            int baseAdv = slot.GlyphId > 0
                ? (int)Math.Round(advanceOf(slot.GlyphId) * scale)
                : 0;

            result.Add(new ShapedGlyph(
                slot.GlyphId,
                baseAdv + slot.XAdvance,
                slot.XOffset,
                slot.YOffset,
                slot.Cluster));
        }

        return result;
    }

    private static bool IsIndicScript(LipiScript script) => script switch
    {
        LipiScript.Latin => false,
        _ => true,
    };

    private static string ScriptToOtTag(LipiScript script) => script switch
    {
        LipiScript.Latin => "latn",
        LipiScript.Tamil => "taml",
        LipiScript.Devanagari => "deva",
        LipiScript.Bengali => "beng",
        LipiScript.Gurmukhi => "guru",
        LipiScript.Gujarati => "gujr",
        LipiScript.Odia => "orya",
        LipiScript.Telugu => "telu",
        LipiScript.Kannada => "knda",
        LipiScript.Malayalam => "mlym",
        _ => "latn",
    };
}
