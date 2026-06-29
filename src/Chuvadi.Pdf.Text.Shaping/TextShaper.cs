// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
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
        double scale = 1000.0 / loader.UnitsPerEm;
        bool indic = IsIndicScript(script);

        // 1. Map code points to initial glyph ids.
        // For Indic scripts the Cluster field stores the source codepoint so the
        // reorderer can classify each slot by its Unicode category.
        // For Latin the Cluster field holds a sequential index for cluster tracking.
        GlyphBuffer buffer = BuildBuffer(text, loader, scale, storeCodepointInCluster: indic);

        // 2. Indic reordering: reorder syllables and synthesise Left_And_Right
        // decompositions before GSUB runs on the visual-order buffer.
        string scriptTag = ScriptToOtTag(script);
        if (indic)
        {
            buffer = IndicReorderer.Reorder(buffer, script, loader);
        }

        // 3. Apply GSUB substitution
        GsubEngine.Apply(ttf, buffer, feat, scriptTag, "dflt");

        // 4. Reset slot advances to 0 so GPOS accumulates only deltas
        foreach (GlyphSlot s in buffer.ActiveSlots())
        {
            s.XAdvance = 0;
        }

        // 5. Apply GPOS positioning
        GposEngine.Apply(ttf, buffer, feat, scriptTag, "dflt");

        // 6. Collect active slots, re-resolve advances for post-GSUB glyph ids
        return Collect(buffer, loader, scale);
    }

    private static GlyphBuffer BuildBuffer(
        string text, TrueTypeLoader loader, double scale, bool storeCodepointInCluster = false)
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
                ? (int)Math.Round(loader.GetGlyphMetrics(gid).AdvanceWidth * scale)
                : 0;
            int clusterValue = storeCodepointInCluster ? codepoint : cluster;
            slots.Add(new GlyphSlot(gid, clusterValue, advance));
            cluster++;
            i += width;
        }

        return new GlyphBuffer(slots);
    }

    private static List<ShapedGlyph> Collect(GlyphBuffer buffer, TrueTypeLoader loader, double scale)
    {
        List<ShapedGlyph> result = new List<ShapedGlyph>();
        foreach (GlyphSlot slot in buffer.ActiveSlots())
        {
            // Re-fetch design-space advance for the post-GSUB glyph id.
            // slot.XAdvance after step 4-5 holds only the GPOS delta (we zeroed it
            // before GPOS ran). Add the font's base advance for the current glyph.
            int baseAdv = slot.GlyphId > 0
                ? (int)Math.Round(loader.GetGlyphMetrics(slot.GlyphId).AdvanceWidth * scale)
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
