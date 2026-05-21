// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  W3C SVG 1.1 §5.6 — The "use" element
// PHASE: v2.0.0 R2 — SVG renderer

using System.Collections.Generic;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Deduplicates serialised glyph path strings so that repeated glyphs
/// (e.g. every space, every common letter on a page of body text) share
/// one <c>&lt;path&gt;</c> definition in <c>&lt;defs&gt;</c> referenced by
/// many <c>&lt;use&gt;</c> elements.
/// </summary>
/// <remarks>
/// <para>
/// Built in two passes. The first pass walks every
/// <see cref="Chuvadi.Pdf.Rendering.DisplayList.DrawGlyphOp"/> in the
/// display list and counts occurrences keyed by the serialised SVG path
/// data string. Glyph data strings seen twice or more are assigned a
/// stable id (in first-occurrence order) and emitted into the page-level
/// <c>&lt;defs&gt;</c> block once. The second (paint) pass then emits a
/// short <c>&lt;use href="#…"&gt;</c> for each repeated glyph and an inline
/// <c>&lt;path&gt;</c> for singletons.
/// </para>
/// <para>
/// Keying on the path data string — rather than on the source
/// <see cref="Chuvadi.Pdf.Graphics.Path"/> reference — is deliberate.
/// <see cref="Chuvadi.Pdf.Rendering.DisplayList.DisplayListBuilder"/>
/// allocates a fresh <c>Path</c> instance per emitted glyph (CTM-baked
/// geometry), so reference identity would never hit. Distinct
/// <c>Path</c> instances with identical serialised geometry share a
/// single defs entry.
/// </para>
/// </remarks>
internal sealed class GlyphCache
{
    private readonly Dictionary<string, int> _counts;
    private readonly Dictionary<string, string> _idByPathData;

    internal GlyphCache()
    {
        _counts = new Dictionary<string, int>(System.StringComparer.Ordinal);
        _idByPathData = new Dictionary<string, string>(System.StringComparer.Ordinal);
    }

    /// <summary>
    /// First-pass: records one occurrence of a glyph with the given
    /// serialised path data.
    /// </summary>
    internal void Observe(string pathData)
    {
        if (_counts.TryGetValue(pathData, out int existing))
        {
            _counts[pathData] = existing + 1;
        }
        else
        {
            _counts[pathData] = 1;
        }
    }

    /// <summary>
    /// Between passes: allocates a stable id for every glyph observed two
    /// or more times. Ids are allocated in first-observation order to keep
    /// output deterministic. Returns the id-and-data pairs ready to emit
    /// into <c>&lt;defs&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The argument <paramref name="firstSightOrder"/> is the list of
    /// distinct path data strings in the order they were first observed in
    /// pass 1; this preserves ordering across hash-randomised dictionary
    /// iterations.
    /// </remarks>
    internal IReadOnlyList<DefsEntry> AllocateIds(
        IReadOnlyList<string> firstSightOrder, SvgWriter writer)
    {
        List<DefsEntry> entries = new List<DefsEntry>();

        for (int i = 0; i < firstSightOrder.Count; i++)
        {
            string pathData = firstSightOrder[i];

            if (!_counts.TryGetValue(pathData, out int count) || count < 2)
            {
                continue;
            }

            string id = writer.NextGlyphId();
            _idByPathData[pathData] = id;
            entries.Add(new DefsEntry(id, pathData));
        }

        return entries;
    }

    /// <summary>
    /// Second-pass: returns the assigned defs id for a glyph data string,
    /// or null when the glyph is a singleton (must be emitted inline).
    /// </summary>
    internal string? GetDefsId(string pathData)
    {
        if (_idByPathData.TryGetValue(pathData, out string? id))
        {
            return id;
        }

        return null;
    }

    internal readonly struct DefsEntry
    {
        internal DefsEntry(string id, string pathData)
        {
            Id = id;
            PathData = pathData;
        }

        internal string Id { get; }

        internal string PathData { get; }
    }
}
