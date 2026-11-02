// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.2.2 (standard 14 fonts), §9.8.2 (font descriptor flags)
// PHASE: 3.30 — substitutes for fonts a PDF does not embed (D-105)

using System;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Chooses a standard 14 font to draw text whose font the PDF names but does
/// not embed ("Arial", "TimesNewRomanPSMT", "Calibri,Bold").
/// </summary>
/// <remarks>
/// The standard 14 outlines are Liberation Sans, Liberation Serif and
/// Liberation Mono (metric-compatible with Arial and Helvetica, Times New
/// Roman and Times, and Courier New and Courier), plus URW symbol fonts for
/// Symbol and ZapfDingbats. The family comes from the font's name, then from
/// its descriptor flags (fixed pitch, serif); bold and italic from the name,
/// the flags, <c>/FontWeight</c> and <c>/ItalicAngle</c>.
/// </remarks>
public static class Standard14Substitute
{
    // Descriptor /Flags bits (Table 123).
    private const int FixedPitchFlag = 1;
    private const int SerifFlag = 2;
    private const int ItalicFlag = 64;
    private const int ForceBoldFlag = 1 << 18;

    private static readonly string[] MonoNames = { "courier", "mono", "consol", "typewriter", "fixedsys" };
    private static readonly string[] SerifNames =
    {
        "times", "roman", "georgia", "garamond", "cambria", "bookantiqua", "bookman", "palatino",
        "minion", "century", "bodoni", "baskerville", "caslon", "didot", "constantia", "serif",
    };

    private static readonly string[] SansMarkers = { "sans", "grotesk", "gothic" };
    private static readonly string[] BoldNames = { "bold", "black", "heavy", "demi", "semibold" };
    private static readonly string[] ItalicNames = { "italic", "oblique", "slanted" };
    private static readonly string[] NoSubstitute = { "wingding", "webding", "marlett" };

    /// <summary>
    /// Returns the standard 14 font that best stands in for a font that is
    /// not embedded, or null when none can (a symbol font such as Wingdings,
    /// whose pictures no standard 14 font has).
    /// </summary>
    /// <param name="baseFont">The font's <c>/BaseFont</c> name; a subset prefix ("ABCDEF+") is ignored.</param>
    /// <param name="flags">The font descriptor's <c>/Flags</c>, or 0.</param>
    /// <param name="italicAngle">The descriptor's <c>/ItalicAngle</c>, or 0.</param>
    /// <param name="fontWeight">The descriptor's <c>/FontWeight</c>, or 0 when absent.</param>
    /// <returns>A standard 14 font name, or null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="baseFont"/> is null.</exception>
    public static string? Choose(string baseFont, int flags, double italicAngle, double fontWeight)
    {
        ArgumentNullException.ThrowIfNull(baseFont);
        int plus = baseFont.IndexOf('+', StringComparison.Ordinal);
        string name = (plus >= 0 ? baseFont[(plus + 1)..] : baseFont).ToLowerInvariant()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        if (ContainsAny(name, NoSubstitute))
        {
            return null;
        }

        if (name.Contains("dingbat", StringComparison.Ordinal))
        {
            return "ZapfDingbats";
        }

        if (name.Contains("symbol", StringComparison.Ordinal))
        {
            return "Symbol";
        }

        bool bold = ContainsAny(name, BoldNames) || (flags & ForceBoldFlag) != 0 || fontWeight >= 600;
        bool italic = ContainsAny(name, ItalicNames) || (flags & ItalicFlag) != 0 || Math.Abs(italicAngle) > 0.5;

        if (ContainsAny(name, MonoNames) || (flags & FixedPitchFlag) != 0)
        {
            return Compose("Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique", bold, italic);
        }

        bool sansNamed = ContainsAny(name, SansMarkers);
        bool serifNamed = !sansNamed && ContainsAny(name, SerifNames);
        if (serifNamed || (!sansNamed && (flags & SerifFlag) != 0 && !IsKnownSans(name)))
        {
            return Compose("Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic", bold, italic);
        }

        return Compose("Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique", bold, italic);
    }

    /// <summary>
    /// Returns the name of the typeface whose outlines draw the given
    /// standard 14 font ("Liberation Sans" for Helvetica), for messages.
    /// </summary>
    /// <param name="standard14Name">A standard 14 font name.</param>
    /// <returns>The typeface name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="standard14Name"/> is null.</exception>
    public static string OutlineSource(string standard14Name)
    {
        ArgumentNullException.ThrowIfNull(standard14Name);
        if (standard14Name.StartsWith("Helvetica", StringComparison.Ordinal))
        {
            return "Liberation Sans";
        }

        if (standard14Name.StartsWith("Times", StringComparison.Ordinal))
        {
            return "Liberation Serif";
        }

        if (standard14Name.StartsWith("Courier", StringComparison.Ordinal))
        {
            return "Liberation Mono";
        }

        return standard14Name == "Symbol" ? "URW Standard Symbols PS" : "URW D050000L";
    }

    // Sans faces whose producers sometimes set the serif flag.
    private static bool IsKnownSans(string name) =>
        name.StartsWith("arial", StringComparison.Ordinal)
        || name.StartsWith("helvetica", StringComparison.Ordinal)
        || name.StartsWith("calibri", StringComparison.Ordinal)
        || name.StartsWith("verdana", StringComparison.Ordinal)
        || name.StartsWith("tahoma", StringComparison.Ordinal)
        || name.StartsWith("segoe", StringComparison.Ordinal);

    private static bool ContainsAny(string name, string[] parts)
    {
        foreach (string part in parts)
        {
            if (name.Contains(part, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Compose(string regular, string bold, string italic, string boldItalic, bool isBold, bool isItalic) =>
        isBold && isItalic ? boldItalic : isBold ? bold : isItalic ? italic : regular;
}
