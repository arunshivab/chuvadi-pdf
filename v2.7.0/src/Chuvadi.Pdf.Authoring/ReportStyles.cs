// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.2.2 — Standard Type 1 fonts (Standard 14)
// PHASE: Phase 2.7 — Report layout
// Style types for the report layer: fonts, paragraphs, lists, page setup,
// headers/footers, and page-number formatting.

using System;
using System.Text;

namespace Chuvadi.Pdf.Authoring;

/// <summary>The Standard-14 font families available to report content.</summary>
public enum ReportFontFamily
{
    /// <summary>Helvetica (sans-serif).</summary>
    Helvetica = 0,

    /// <summary>Times (serif).</summary>
    Times = 1,

    /// <summary>Courier (monospace).</summary>
    Courier = 2,
}

/// <summary>
/// A report font: a Standard-14 family plus bold/italic flags, resolved to
/// the matching Standard-14 PostScript name at draw time.
/// </summary>
public sealed class ReportFont
{
    /// <summary>Gets or initialises the font family. Default: Helvetica.</summary>
    public ReportFontFamily Family { get; init; } = ReportFontFamily.Helvetica;

    /// <summary>Gets or initialises whether the bold variant is used.</summary>
    public bool Bold { get; init; }

    /// <summary>Gets or initialises whether the italic (oblique) variant is used.</summary>
    public bool Italic { get; init; }

    /// <summary>Regular Helvetica.</summary>
    public static ReportFont Helvetica { get; } = new ReportFont();

    /// <summary>Bold Helvetica.</summary>
    public static ReportFont HelveticaBold { get; } = new ReportFont
    {
        Bold = true,
    };

    /// <summary>Regular Times.</summary>
    public static ReportFont Times { get; } = new ReportFont
    {
        Family = ReportFontFamily.Times,
    };

    /// <summary>Bold Times.</summary>
    public static ReportFont TimesBold { get; } = new ReportFont
    {
        Family = ReportFontFamily.Times,
        Bold = true,
    };

    /// <summary>Regular Courier.</summary>
    public static ReportFont Courier { get; } = new ReportFont
    {
        Family = ReportFontFamily.Courier,
    };

    /// <summary>Resolves to the Standard-14 PostScript font name.</summary>
    public string Resolve()
    {
        switch (Family)
        {
            case ReportFontFamily.Times:
                if (Bold && Italic)
                {
                    return StandardFonts.TimesBoldItalic;
                }
                if (Bold)
                {
                    return StandardFonts.TimesBold;
                }
                if (Italic)
                {
                    return StandardFonts.TimesItalic;
                }
                return StandardFonts.TimesRoman;

            case ReportFontFamily.Courier:
                if (Bold && Italic)
                {
                    return StandardFonts.CourierBoldOblique;
                }
                if (Bold)
                {
                    return StandardFonts.CourierBold;
                }
                if (Italic)
                {
                    return StandardFonts.CourierOblique;
                }
                return StandardFonts.Courier;

            default:
                if (Bold && Italic)
                {
                    return StandardFonts.HelveticaBoldOblique;
                }
                if (Bold)
                {
                    return StandardFonts.HelveticaBold;
                }
                if (Italic)
                {
                    return StandardFonts.HelveticaOblique;
                }
                return StandardFonts.Helvetica;
        }
    }
}

/// <summary>Page geometry for a report: paper size and the four margins.</summary>
public sealed class ReportPageSetup
{
    /// <summary>Default setup: A4 portrait with 50-point margins.</summary>
    public static ReportPageSetup Default { get; } = new ReportPageSetup();

    /// <summary>Gets or initialises the paper size. Default: A4. Use <see cref="PageSize.Landscape"/> for landscape.</summary>
    public PageSize PageSize { get; init; } = PageSize.A4;

    /// <summary>Gets or initialises the left margin in points. Default: 50.</summary>
    public double MarginLeft { get; init; } = 50;

    /// <summary>Gets or initialises the top margin in points. Default: 50.</summary>
    public double MarginTop { get; init; } = 50;

    /// <summary>Gets or initialises the right margin in points. Default: 50.</summary>
    public double MarginRight { get; init; } = 50;

    /// <summary>Gets or initialises the bottom margin in points. Default: 50.</summary>
    public double MarginBottom { get; init; } = 50;

    /// <summary>The width of the content area between the side margins.</summary>
    public double ContentWidth => PageSize.Width - MarginLeft - MarginRight;

    /// <summary>The height of the content area between the top and bottom margins.</summary>
    public double ContentHeight => PageSize.Height - MarginTop - MarginBottom;
}

/// <summary>Paragraph styling: font, size, colour, alignment, spacing, and indents.</summary>
public sealed class ParagraphStyle
{
    /// <summary>Default body style: 11-point Helvetica, left-aligned, 1.25 line spacing.</summary>
    public static ParagraphStyle Default { get; } = new ParagraphStyle();

    /// <summary>Gets or initialises the font. Default: regular Helvetica.</summary>
    public ReportFont Font { get; init; } = ReportFont.Helvetica;

    /// <summary>Gets or initialises the font size in points. Default: 11.</summary>
    public double FontSize { get; init; } = 11;

    /// <summary>Gets or initialises the text colour. Default: black.</summary>
    public Color Color { get; init; } = Colors.Black;

    /// <summary>Gets or initialises the horizontal alignment. Default: left. <see cref="TextAlignment.Justify"/> stretches every full line to the column width.</summary>
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;

    /// <summary>Gets or initialises the line spacing as a multiple of the font size. Default: 1.25.</summary>
    public double LineSpacing { get; init; } = 1.25;

    /// <summary>Gets or initialises the vertical space, in points, inserted before the paragraph. Default: 0.</summary>
    public double SpaceBefore { get; init; }

    /// <summary>Gets or initialises the vertical space, in points, inserted after the paragraph. Default: 6.</summary>
    public double SpaceAfter { get; init; } = 6;

    /// <summary>Gets or initialises the extra indent, in points, applied to the first line only. Default: 0.</summary>
    public double FirstLineIndent { get; init; }

    /// <summary>Gets or initialises the left indent, in points, applied to every line. Default: 0.</summary>
    public double LeftIndent { get; init; }

    /// <summary>Gets or initialises the right indent, in points, applied to every line. Default: 0.</summary>
    public double RightIndent { get; init; }
}

/// <summary>The numbering scheme of an ordered list or a page number.</summary>
public enum NumberingFormat
{
    /// <summary>Arabic numerals: 1, 2, 3 …</summary>
    Arabic = 0,

    /// <summary>Lower-case roman numerals: i, ii, iii …</summary>
    RomanLower = 1,

    /// <summary>Upper-case roman numerals: I, II, III …</summary>
    RomanUpper = 2,

    /// <summary>Lower-case letters: a, b, … z, aa, ab …</summary>
    LetterLower = 3,

    /// <summary>Upper-case letters: A, B, … Z, AA, AB …</summary>
    LetterUpper = 4,
}

/// <summary>List styling for bulleted and numbered lists.</summary>
public sealed class ListStyle
{
    /// <summary>Default list style: 11-point Helvetica, "•" bullets, 18-point indent.</summary>
    public static ListStyle Default { get; } = new ListStyle();

    /// <summary>Gets or initialises the font. Default: regular Helvetica.</summary>
    public ReportFont Font { get; init; } = ReportFont.Helvetica;

    /// <summary>Gets or initialises the font size in points. Default: 11.</summary>
    public double FontSize { get; init; } = 11;

    /// <summary>Gets or initialises the text colour. Default: black.</summary>
    public Color Color { get; init; } = Colors.Black;

    /// <summary>Gets or initialises the bullet marker for unordered lists. Default: "•".</summary>
    public string Bullet { get; init; } = "\u2022";

    /// <summary>Gets or initialises the numbering scheme for ordered lists. Default: Arabic.</summary>
    public NumberingFormat Numbering { get; init; } = NumberingFormat.Arabic;

    /// <summary>Gets or initialises the suffix appended after an ordered-list number. Default: ".".</summary>
    public string NumberSuffix { get; init; } = ".";

    /// <summary>Gets or initialises the first number of an ordered list. Default: 1.</summary>
    public int StartAt { get; init; } = 1;

    /// <summary>Gets or initialises the indent, in points, from the column edge to the marker. Default: 6.</summary>
    public double MarkerIndent { get; init; } = 6;

    /// <summary>Gets or initialises the indent, in points, from the column edge to the item text. Default: 24.</summary>
    public double TextIndent { get; init; } = 24;

    /// <summary>Gets or initialises the line spacing as a multiple of the font size. Default: 1.25.</summary>
    public double LineSpacing { get; init; } = 1.25;

    /// <summary>Gets or initialises the vertical space, in points, between list items. Default: 2.</summary>
    public double ItemSpacing { get; init; } = 2;

    /// <summary>Gets or initialises the vertical space, in points, after the whole list. Default: 6.</summary>
    public double SpaceAfter { get; init; } = 6;
}

/// <summary>
/// Header / footer band styling. The text may contain the tokens
/// <c>{page}</c>, <c>{total}</c>, <c>{title}</c>, and <c>{date}</c>, replaced
/// per page at save time; page numbers honour <see cref="PageNumbering"/>.
/// </summary>
public sealed class HeaderFooterStyle
{
    /// <summary>Gets or initialises the band text (with optional tokens). Default: empty.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Gets or initialises the font. Default: regular Helvetica.</summary>
    public ReportFont Font { get; init; } = ReportFont.Helvetica;

    /// <summary>Gets or initialises the font size in points. Default: 9.</summary>
    public double FontSize { get; init; } = 9;

    /// <summary>Gets or initialises the text colour. Default: mid gray.</summary>
    public Color Color { get; init; } = Colors.Gray;

    /// <summary>Gets or initialises the horizontal alignment within the content width. Default: centre.</summary>
    public TextAlignment Alignment { get; init; } = TextAlignment.Center;

    /// <summary>Gets or initialises the numbering scheme applied to {page} and {total}. Default: Arabic.</summary>
    public NumberingFormat PageNumbering { get; init; } = NumberingFormat.Arabic;

    /// <summary>Gets or initialises whether the band also draws on page 1. Default: true.</summary>
    public bool ShowOnFirstPage { get; init; } = true;

    /// <summary>
    /// Gets or initialises the distance, in points, from the page edge (top
    /// edge for headers, bottom edge for footers) to the band. Default: 25.
    /// </summary>
    public double EdgeOffset { get; init; } = 25;

    /// <summary>Gets or initialises whether a thin rule line separates the band from the content. Default: false.</summary>
    public bool RuleLine { get; init; }
}

/// <summary>Formats integers in the report numbering schemes.</summary>
public static class PageNumberFormatter
{
    /// <summary>
    /// Formats <paramref name="value"/> (1-based) in the given scheme.
    /// Values below 1 format as Arabic digits in every scheme.
    /// </summary>
    public static string Format(int value, NumberingFormat format)
    {
        if (value < 1)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        switch (format)
        {
            case NumberingFormat.RomanLower:
                return Roman(value).ToLowerInvariant();
            case NumberingFormat.RomanUpper:
                return Roman(value);
            case NumberingFormat.LetterLower:
                return Letters(value).ToLowerInvariant();
            case NumberingFormat.LetterUpper:
                return Letters(value);
            default:
                return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static readonly int[] RomanValues =
        { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };

    private static readonly string[] RomanSymbols =
        { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

    private static string Roman(int value)
    {
        StringBuilder sb = new();
        for (int i = 0; i < RomanValues.Length && value > 0; i++)
        {
            while (value >= RomanValues[i])
            {
                sb.Append(RomanSymbols[i]);
                value -= RomanValues[i];
            }
        }
        return sb.ToString();
    }

    private static string Letters(int value)
    {
        // Excel-style bijective base-26: A..Z, AA..AZ, BA…
        StringBuilder sb = new();
        while (value > 0)
        {
            value--;
            sb.Insert(0, (char)('A' + (value % 26)));
            value /= 26;
        }
        return sb.ToString();
    }
}

/// <summary>
/// Maps common typographic Unicode characters to their WinAnsi code points so
/// they render correctly under the Standard-14 WinAnsiEncoding fonts.
/// </summary>
internal static class WinAnsiText
{
    /// <summary>Maps typographic characters (bullets, dashes, smart quotes, ellipsis) to WinAnsi bytes.</summary>
    internal static string Map(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        StringBuilder? sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char mapped = MapChar(text[i]);
            if (mapped != text[i] && sb is null)
            {
                sb = new StringBuilder(text.Length);
                sb.Append(text, 0, i);
            }
            sb?.Append(mapped);
        }
        return sb?.ToString() ?? text;
    }

    private static char MapChar(char ch)
    {
        switch (ch)
        {
            case '\u2022': return '\u0095';   // bullet
            case '\u2013': return '\u0096';   // en dash
            case '\u2014': return '\u0097';   // em dash
            case '\u2018': return '\u0091';   // left single quote
            case '\u2019': return '\u0092';   // right single quote
            case '\u201C': return '\u0093';   // left double quote
            case '\u201D': return '\u0094';   // right double quote
            case '\u2026': return '\u0085';   // ellipsis
            case '\u20AC': return '\u0080';   // euro sign
            case '\u2122': return '\u0099';   // trade mark
            default: return ch;
        }
    }
}
