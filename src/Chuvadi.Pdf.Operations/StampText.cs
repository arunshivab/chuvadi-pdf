// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.2.2 (standard 14 fonts), §9.4.3 (text showing)
// PHASE: Document operations — shared stamp text metrics and rendering.

using System.Globalization;
using System.Text;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Operations;

/// <summary>
/// Shared text helpers for stamping: a Helvetica advance-width table (AFM,
/// WinAnsi) for accurate placement, a standard-14 font dictionary builder, PDF
/// string escaping, and a single-line text-show stream builder. Kept local to
/// Operations so no dependency on the font-rendering stack is introduced.
/// </summary>
internal static class StampText
{
    internal const string FontResourceName = "CvStampFont";

    /// <summary>Builds a Helvetica (WinAnsi) standard-14 font dictionary.</summary>
    internal static PdfDictionary BuildHelveticaFont()
    {
        PdfDictionary font = new PdfDictionary();
        font.Set(PdfName.Type, PdfName.Font);
        font.Set(PdfName.Subtype, PdfName.Intern("Type1"));
        font.Set(PdfName.Intern("BaseFont"), PdfName.Intern("Helvetica"));
        font.Set(PdfName.Intern("Encoding"), PdfName.Intern("WinAnsiEncoding"));
        return font;
    }

    /// <summary>
    /// Measures a single line of text in Helvetica at the given font size,
    /// returning the advance width in points.
    /// </summary>
    internal static double MeasureWidth(string text, double fontSize)
    {
        int units = 0;
        foreach (char c in text)
        {
            units += AdvanceWidth(c);
        }

        return units / 1000.0 * fontSize;
    }

    /// <summary>Escapes a string for a PDF literal-string operand.</summary>
    internal static string EscapePdfString(string text)
    {
        StringBuilder sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '(':
                    sb.Append("\\(");
                    break;
                case ')':
                    sb.Append("\\)");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a text-showing content fragment (no surrounding q/Q) that places
    /// a single line at the given transform with the given colour and size.
    /// </summary>
    internal static string BuildShowText(
        string text, Transform placement, double fontSize, ColorF color)
    {
        ColorF rgb = color.ToRgb();
        string escaped = EscapePdfString(text);

        StringBuilder sb = new StringBuilder();
        sb.Append(Fmt(rgb.R)).Append(' ').Append(Fmt(rgb.G)).Append(' ')
            .Append(Fmt(rgb.B)).Append(" rg\n");
        sb.Append("BT\n");
        sb.Append(Fmt(placement.A)).Append(' ').Append(Fmt(placement.B)).Append(' ')
            .Append(Fmt(placement.C)).Append(' ').Append(Fmt(placement.D)).Append(' ')
            .Append(Fmt(placement.E)).Append(' ').Append(Fmt(placement.F)).Append(" Tm\n");
        sb.Append('/').Append(FontResourceName).Append(' ').Append(Fmt(fontSize)).Append(" Tf\n");
        sb.Append('(').Append(escaped).Append(") Tj\n");
        sb.Append("ET\n");
        return sb.ToString();
    }

    internal static string Fmt(double value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    // Helvetica AFM advance widths (1000 units/em) for WinAnsi code points
    // 32..126. Characters outside this range fall back to the average (556).
    private static int AdvanceWidth(char c)
    {
        if (c < 32 || c > 126)
        {
            return 556;
        }

        return HelveticaWidths[c - 32];
    }

    private static readonly int[] HelveticaWidths =
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    };
}
