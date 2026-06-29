// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

namespace Chuvadi.Pdf.Text.Shaping;

/// <summary>
/// Controls which OpenType features <see cref="TextShaper"/> applies when shaping
/// a run. Features absent from the font are silently ignored.
/// </summary>
public sealed class ShapingFeatures
{
    /// <summary>
    /// Gets the default feature set: ccmp, locl, calt, liga, kern, mark, mkmk enabled;
    /// all optional features off.
    /// </summary>
    public static ShapingFeatures Default { get; } = new ShapingFeatures();

    // ── Always-on (structural) ──────────────────────────────────────────────

    /// <summary>Gets or inits whether glyph composition/decomposition (ccmp) is enabled. Default: true.</summary>
    public bool Ccmp { get; init; } = true;

    /// <summary>Gets or inits whether localised forms (locl) are enabled. Default: true.</summary>
    public bool Locl { get; init; } = true;

    /// <summary>Gets or inits whether contextual alternates (calt) are enabled. Default: true.</summary>
    public bool Calt { get; init; } = true;

    /// <summary>Gets or inits whether standard ligatures (liga) are enabled. Default: true.</summary>
    public bool Liga { get; init; } = true;

    // ── Positioning ─────────────────────────────────────────────────────────

    /// <summary>Gets or inits whether capital spacing (cpsp) is enabled. Default: true.</summary>
    public bool Cpsp { get; init; } = true;

    /// <summary>Gets or inits whether kerning (kern) is enabled. Default: true.</summary>
    public bool Kern { get; init; } = true;

    /// <summary>Gets or inits whether mark-to-base attachment (mark) is enabled. Default: true.</summary>
    public bool Mark { get; init; } = true;

    /// <summary>Gets or inits whether mark-to-mark attachment (mkmk) is enabled. Default: true.</summary>
    public bool Mkmk { get; init; } = true;

    // ── Optional substitution ────────────────────────────────────────────────

    /// <summary>Gets or inits whether ordinals (ordn) are enabled. Default: false.</summary>
    public bool Ordn { get; init; }

    /// <summary>Gets or inits whether fractions (frac) are enabled. Default: false.</summary>
    public bool Frac { get; init; }

    /// <summary>Gets or inits whether numerator (numr) forms are enabled. Default: false.</summary>
    public bool Numr { get; init; }

    /// <summary>Gets or inits whether denominator (dnom) forms are enabled. Default: false.</summary>
    public bool Dnom { get; init; }

    /// <summary>Gets or inits whether superscript (sups) forms are enabled. Default: false.</summary>
    public bool Sups { get; init; }

    /// <summary>Gets or inits whether subscript (subs) forms are enabled. Default: false.</summary>
    public bool Subs { get; init; }

    /// <summary>Gets or inits whether scientific inferiors (sinf) are enabled. Default: false.</summary>
    public bool Sinf { get; init; }

    /// <summary>Gets or inits whether case-sensitive forms (case) are enabled. Default: false.</summary>
    public bool Case { get; init; }

    /// <summary>Gets or inits whether slashed zero (zero) is enabled. Default: false.</summary>
    public bool Zero { get; init; }

    /// <summary>Gets or inits whether discretionary ligatures (dlig) are enabled. Default: false.</summary>
    public bool Dlig { get; init; }

    /// <summary>Gets or inits whether proportional numbers (pnum) are enabled. Default: false.</summary>
    public bool Pnum { get; init; }

    /// <summary>Gets or inits whether tabular numbers (tnum) are enabled. Default: false.</summary>
    public bool Tnum { get; init; }

    /// <summary>Gets or inits whether salt alternates (salt) are enabled. Default: false.</summary>
    public bool Salt { get; init; }

    /// <summary>Gets or inits whether all-alternates (aalt) are enabled. Default: false.</summary>
    public bool Aalt { get; init; }

    /// <summary>Gets or inits stylistic set 01 (ss01). Default: false.</summary>
    public bool Ss01 { get; init; }

    /// <summary>Gets or inits stylistic set 02 (ss02). Default: false.</summary>
    public bool Ss02 { get; init; }

    /// <summary>Gets or inits stylistic set 03 (ss03). Default: false.</summary>
    public bool Ss03 { get; init; }

    /// <summary>Gets or inits stylistic set 04 (ss04). Default: false.</summary>
    public bool Ss04 { get; init; }

    /// <summary>Gets or inits stylistic set 05 (ss05). Default: false.</summary>
    public bool Ss05 { get; init; }

    /// <summary>Gets or inits stylistic set 06 (ss06). Default: false.</summary>
    public bool Ss06 { get; init; }

    /// <summary>Gets or inits stylistic set 07 (ss07). Default: false.</summary>
    public bool Ss07 { get; init; }

    /// <summary>Gets or inits stylistic set 08 (ss08). Default: false.</summary>
    public bool Ss08 { get; init; }

    /// <summary>Gets or inits character variant 01 (cv01). Default: false.</summary>
    public bool Cv01 { get; init; }

    /// <summary>Gets or inits character variant 02 (cv02). Default: false.</summary>
    public bool Cv02 { get; init; }

    /// <summary>Gets or inits character variant 03 (cv03). Default: false.</summary>
    public bool Cv03 { get; init; }

    /// <summary>Gets or inits character variant 04 (cv04). Default: false.</summary>
    public bool Cv04 { get; init; }

    /// <summary>Gets or inits character variant 05 (cv05). Default: false.</summary>
    public bool Cv05 { get; init; }

    /// <summary>Gets or inits character variant 06 (cv06). Default: false.</summary>
    public bool Cv06 { get; init; }

    /// <summary>Gets or inits character variant 07 (cv07). Default: false.</summary>
    public bool Cv07 { get; init; }

    /// <summary>Gets or inits character variant 08 (cv08). Default: false.</summary>
    public bool Cv08 { get; init; }

    /// <summary>Gets or inits character variant 09 (cv09). Default: false.</summary>
    public bool Cv09 { get; init; }

    /// <summary>Gets or inits character variant 10 (cv10). Default: false.</summary>
    public bool Cv10 { get; init; }

    /// <summary>Gets or inits character variant 11 (cv11). Default: false.</summary>
    public bool Cv11 { get; init; }

    /// <summary>Gets or inits character variant 12 (cv12). Default: false.</summary>
    public bool Cv12 { get; init; }

    /// <summary>Gets or inits character variant 13 (cv13). Default: false.</summary>
    public bool Cv13 { get; init; }

    /// <summary>Gets or inits character variant 14 (cv14). Default: false.</summary>
    public bool Cv14 { get; init; }

    /// <summary>
    /// Returns whether a four-character OpenType feature tag is enabled by this
    /// feature set. Tags not explicitly listed are treated as disabled.
    /// </summary>
    /// <param name="tag">The four-character ASCII feature tag, e.g. "liga".</param>
    /// <returns><see langword="true"/> if the feature is enabled.</returns>
    public bool IsEnabled(string tag) => tag switch
    {
        "ccmp" => Ccmp,
        "locl" => Locl,
        "calt" => Calt,
        "liga" => Liga,
        "cpsp" => Cpsp,
        "kern" => Kern,
        "mark" => Mark,
        "mkmk" => Mkmk,
        "ordn" => Ordn,
        "frac" => Frac,
        "numr" => Numr,
        "dnom" => Dnom,
        "sups" => Sups,
        "subs" => Subs,
        "sinf" => Sinf,
        "case" => Case,
        "zero" => Zero,
        "dlig" => Dlig,
        "pnum" => Pnum,
        "tnum" => Tnum,
        "salt" => Salt,
        "aalt" => Aalt,
        "ss01" => Ss01,
        "ss02" => Ss02,
        "ss03" => Ss03,
        "ss04" => Ss04,
        "ss05" => Ss05,
        "ss06" => Ss06,
        "ss07" => Ss07,
        "ss08" => Ss08,
        "cv01" => Cv01,
        "cv02" => Cv02,
        "cv03" => Cv03,
        "cv04" => Cv04,
        "cv05" => Cv05,
        "cv06" => Cv06,
        "cv07" => Cv07,
        "cv08" => Cv08,
        "cv09" => Cv09,
        "cv10" => Cv10,
        "cv11" => Cv11,
        "cv12" => Cv12,
        "cv13" => Cv13,
        "cv14" => Cv14,
        _ => false,
    };
}
